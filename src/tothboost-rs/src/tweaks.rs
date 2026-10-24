use crate::models::TweakItem;
use crate::power::run_hidden;
use std::path::PathBuf;
use windows::core::*;
use windows::Win32::System::Registry::*;

pub struct RegistryTweaks;

const HIGH_PERFORMANCE_GUID: &str = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
const BALANCED_GUID: &str = "381b4222-f694-41f0-9685-ff5bb260df2e";
const ULTIMATE_TEMPLATE_GUID: &str = "e9a42b02-d5df-448d-aa00-03f14749eb61";

impl RegistryTweaks {
    pub fn get_available_tweaks() -> Vec<TweakItem> {
        let active = active_scheme_guid();
        Self::catalog()
            .into_iter()
            .map(|mut tweak| {
                tweak.is_applied = Self::is_applied_inner(&tweak.id, active.as_deref());
                tweak
            })
            .collect()
    }

    fn catalog() -> Vec<TweakItem> {
        vec![
            TweakItem {
                id: "GameDvr".into(),
                name: "Disable Background Recording (Game DVR)".into(),
                description: "Prevents Xbox Game Bar from recording in the background, freeing CPU/GPU.".into(),
                category: "Gaming".into(),
                is_applied: false,
            },
            TweakItem {
                id: "VisualEffects".into(),
                name: "Visuals: Best Performance".into(),
                description: "Disables animations and transparency to reduce GPU/CPU workload.".into(),
                category: "Visual".into(),
                is_applied: false,
            },
            TweakItem {
                id: "Telemetry".into(),
                name: "Telemetry: Reduce Data Collection".into(),
                description: "Sets AllowTelemetry = 0 via local policies. Reversible at any time.".into(),
                category: "Privacy".into(),
                is_applied: false,
            },
            TweakItem {
                id: "Hibernate".into(),
                name: "Disable Hibernation".into(),
                description: "Removes hiberfil.sys and frees disk space. The PC will not hibernate.".into(),
                category: "Power".into(),
                is_applied: false,
            },
            TweakItem {
                id: "NetworkThrottling".into(),
                name: "Network: Disable Throttling".into(),
                description: "Disables Windows network throttling and sets SystemResponsiveness = 0.".into(),
                category: "Network".into(),
                is_applied: false,
            },
            TweakItem {
                id: "UltimatePower".into(),
                name: "Power Plan: Ultimate Performance".into(),
                description: "Unlocks and activates Microsoft's Ultimate Performance plan.".into(),
                category: "Power".into(),
                is_applied: false,
            },
            TweakItem {
                id: "HighPerformance".into(),
                name: "Power Plan: High Performance".into(),
                description: "Activates the standard Windows High Performance plan.".into(),
                category: "Power".into(),
                is_applied: false,
            },
        ]
    }

    fn is_applied_inner(id: &str, active_scheme: Option<&str>) -> bool {
        match id {
            "GameDvr" => get_dword(
                HKEY_CURRENT_USER,
                "System\\GameConfigStore",
                "GameDVR_Enabled",
            ) == Some(0),
            "VisualEffects" => get_dword(
                HKEY_CURRENT_USER,
                "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects",
                "VisualFXSetting",
            ) == Some(2),
            "Telemetry" => get_dword(
                HKEY_LOCAL_MACHINE,
                "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection",
                "AllowTelemetry",
            ) == Some(0),
            "Hibernate" => get_dword(
                HKEY_LOCAL_MACHINE,
                "SYSTEM\\CurrentControlSet\\Control\\Power",
                "HibernateEnabled",
            ) == Some(0),
            "NetworkThrottling" => {
                get_dword(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile",
                    "NetworkThrottlingIndex",
                ) == Some(0xFFFF_FFFF)
            }
            "HighPerformance" => active_scheme
                .map(|g| g.eq_ignore_ascii_case(HIGH_PERFORMANCE_GUID))
                .unwrap_or(false),
            "UltimatePower" => {
                let Some(stored) = read_ultimate_guid() else {
                    return false;
                };
                active_scheme
                    .map(|g| g.eq_ignore_ascii_case(&stored))
                    .unwrap_or(false)
            }
            _ => false,
        }
    }

    pub fn apply_tweak(id: &str) -> std::result::Result<String, String> {
        match id {
            "GameDvr" => {
                set_dword(
                    HKEY_CURRENT_USER,
                    "System\\GameConfigStore",
                    "GameDVR_Enabled",
                    0,
                )?;
                let _ = set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR",
                    "AllowGameDVR",
                    0,
                );
                Ok("Disabled GameDVR background recording".into())
            }
            "VisualEffects" => {
                set_dword(
                    HKEY_CURRENT_USER,
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects",
                    "VisualFXSetting",
                    2,
                )?;
                Ok("Visual effects set to Best Performance".into())
            }
            "Telemetry" => {
                set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection",
                    "AllowTelemetry",
                    0,
                )?;
                Ok("Reduced telemetry collection".into())
            }
            "Hibernate" => {
                let _ = run_hidden("powercfg.exe", &["-h", "off"]);
                let _ = set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SYSTEM\\CurrentControlSet\\Control\\Power",
                    "HibernateEnabled",
                    0,
                );
                Ok("Disabled hibernation".into())
            }
            "NetworkThrottling" => {
                set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile",
                    "NetworkThrottlingIndex",
                    0xFFFF_FFFF,
                )?;
                let _ = set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile",
                    "SystemResponsiveness",
                    0,
                );
                Ok("Disabled network throttling".into())
            }
            "HighPerformance" => {
                set_active_scheme(HIGH_PERFORMANCE_GUID)?;
                Ok("Activated High Performance power plan".into())
            }
            "UltimatePower" => {
                let guid = ensure_ultimate_scheme()?;
                set_active_scheme(&guid)?;
                Ok("Activated Ultimate Performance power plan".into())
            }
            _ => Err("Unknown tweak ID".into()),
        }
    }

    pub fn revert_tweak(id: &str) -> std::result::Result<String, String> {
        match id {
            "GameDvr" => {
                set_dword(
                    HKEY_CURRENT_USER,
                    "System\\GameConfigStore",
                    "GameDVR_Enabled",
                    1,
                )?;
                let _ = delete_value(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR",
                    "AllowGameDVR",
                );
                Ok("Re-enabled GameDVR".into())
            }
            "VisualEffects" => {
                set_dword(
                    HKEY_CURRENT_USER,
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects",
                    "VisualFXSetting",
                    0,
                )?;
                Ok("Restored automatic visual effects".into())
            }
            "Telemetry" => {
                let _ = delete_value(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection",
                    "AllowTelemetry",
                );
                Ok("Restored default telemetry policy".into())
            }
            "Hibernate" => {
                let _ = run_hidden("powercfg.exe", &["-h", "on"]);
                let _ = set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SYSTEM\\CurrentControlSet\\Control\\Power",
                    "HibernateEnabled",
                    1,
                );
                Ok("Re-enabled hibernation".into())
            }
            "NetworkThrottling" => {
                set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile",
                    "NetworkThrottlingIndex",
                    10,
                )?;
                let _ = set_dword(
                    HKEY_LOCAL_MACHINE,
                    "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile",
                    "SystemResponsiveness",
                    20,
                );
                Ok("Restored Windows network throttling defaults".into())
            }
            "HighPerformance" => {
                set_active_scheme(BALANCED_GUID)?;
                Ok("Activated Balanced power plan".into())
            }
            "UltimatePower" => {
                set_active_scheme(BALANCED_GUID)?;
                Ok("Activated Balanced power plan".into())
            }
            _ => Err("Unknown tweak ID".into()),
        }
    }

    pub fn set_balanced_plan() -> std::result::Result<(), String> {
        set_active_scheme(BALANCED_GUID)
    }
}

fn active_scheme_guid() -> Option<String> {
    let output = run_hidden("powercfg.exe", &["/getactivescheme"]).ok()?;
    let marker = output.find("GUID:")?;
    let rest = &output[marker + 5..];
    let guid: String = rest
        .trim_start()
        .chars()
        .take_while(|c| c.is_ascii_hexdigit() || *c == '-')
        .collect();
    if guid.len() >= 36 {
        Some(guid.to_lowercase())
    } else {
        None
    }
}

fn set_active_scheme(guid: &str) -> std::result::Result<(), String> {
    run_hidden("powercfg.exe", &["/setactive", guid]).map(|_| ())
}

fn ultimate_guid_path() -> PathBuf {
    crate::blocklist::Blocklist::data_dir().join("ultimate_plan.txt")
}

fn read_ultimate_guid() -> Option<String> {
    std::fs::read_to_string(ultimate_guid_path())
        .ok()
        .map(|s| s.trim().to_lowercase())
        .filter(|s| s.len() >= 36)
}

/// Ensures an Ultimate Performance scheme exists, returning its GUID.
fn ensure_ultimate_scheme() -> std::result::Result<String, String> {
    if let Some(guid) = read_ultimate_guid() {
        if scheme_exists(&guid) {
            return Ok(guid);
        }
    }

    let output = run_hidden(
        "powercfg.exe",
        &["-duplicatescheme", ULTIMATE_TEMPLATE_GUID],
    )
    .or_else(|_| run_hidden("powercfg.exe", &["-duplicatescheme", ULTIMATE_TEMPLATE_GUID]))?;

    let guid = parse_guid(&output).ok_or("Could not read Ultimate Performance GUID")?;
    let dir = crate::blocklist::Blocklist::data_dir();
    let _ = std::fs::create_dir_all(&dir);
    let _ = std::fs::write(ultimate_guid_path(), &guid);
    Ok(guid)
}

fn scheme_exists(guid: &str) -> bool {
    run_hidden("powercfg.exe", &["/list"])
        .map(|out| out.to_lowercase().contains(&guid.to_lowercase()))
        .unwrap_or(false)
}

fn parse_guid(text: &str) -> Option<String> {
    let marker = text.find("GUID:")?;
    let rest = &text[marker + 5..];
    let guid: String = rest
        .trim_start()
        .chars()
        .take_while(|c| c.is_ascii_hexdigit() || *c == '-')
        .collect();
    (guid.len() >= 36).then(|| guid.to_lowercase())
}

fn wide(value: &str) -> Vec<u16> {
    value.encode_utf16().chain(std::iter::once(0)).collect()
}

fn set_dword(
    hkey: HKEY,
    subkey: &str,
    value_name: &str,
    val: u32,
) -> std::result::Result<(), String> {
    unsafe {
        let mut key = HKEY::default();
        let sub = wide(subkey);
        if RegCreateKeyExW(
            hkey,
            PCWSTR(sub.as_ptr()),
            0,
            PCWSTR::null(),
            REG_OPTION_NON_VOLATILE,
            KEY_SET_VALUE,
            None,
            &mut key,
            None,
        )
        .is_err()
        {
            return Err(format!("Cannot open HKLM\\{} (administrator required)", subkey));
        }

        let bytes = val.to_ne_bytes();
        let name = wide(value_name);
        let res = RegSetValueExW(key, PCWSTR(name.as_ptr()), 0, REG_DWORD, Some(&bytes));
        let _ = RegCloseKey(key);
        res.ok()
            .map_err(|e| format!("RegSetValueExW({}) failed: {}", value_name, e))
    }
}

fn delete_value(hkey: HKEY, subkey: &str, value_name: &str) -> std::result::Result<(), String> {
    unsafe {
        let mut key = HKEY::default();
        let sub = wide(subkey);
        if RegOpenKeyExW(hkey, PCWSTR(sub.as_ptr()), 0, KEY_SET_VALUE, &mut key).is_err() {
            return Ok(());
        }
        let name = wide(value_name);
        let res = RegDeleteValueW(key, PCWSTR(name.as_ptr()));
        let _ = RegCloseKey(key);
        res.ok().map_err(|e| e.to_string())
    }
}

fn get_dword(hkey: HKEY, subkey: &str, value_name: &str) -> Option<u32> {
    unsafe {
        let mut key = HKEY::default();
        let sub = wide(subkey);
        if RegOpenKeyExW(hkey, PCWSTR(sub.as_ptr()), 0, KEY_READ, &mut key).is_err() {
            return None;
        }
        let mut buf = [0u8; 4];
        let mut len = 4u32;
        let mut ty = REG_VALUE_TYPE::default();
        let name = wide(value_name);
        let res = RegQueryValueExW(
            key,
            PCWSTR(name.as_ptr()),
            None,
            Some(&mut ty),
            Some(buf.as_mut_ptr()),
            Some(&mut len),
        );
        let _ = RegCloseKey(key);
        if res.is_ok() && ty == REG_DWORD && len == 4 {
            Some(u32::from_ne_bytes(buf))
        } else {
            None
        }
    }
}
