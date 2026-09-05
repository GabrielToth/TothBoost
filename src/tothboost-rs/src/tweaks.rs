use crate::models::TweakItem;
use windows::core::*;
use windows::Win32::System::Registry::*;

pub struct RegistryTweaks;

impl RegistryTweaks {
    pub fn get_available_tweaks() -> Vec<TweakItem> {
        vec![
            TweakItem {
                id: "GameDvr".into(),
                name: "Disable Background Recording (Game DVR)".into(),
                description: "Prevents Xbox Game Bar from recording video/audio in background, freeing up CPU and GPU.".into(),
                category: "Gaming".into(),
                is_applied: false,
            },
            TweakItem {
                id: "VisualEffects".into(),
                name: "Visuals: Best Performance".into(),
                description: "Disables system animations, transparency, and visual effects (reduces GPU/CPU workload).".into(),
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
                description: "Removes hibernation file (hiberfil.sys) and frees up disk space. Note: PC will not hibernate.".into(),
                category: "Power".into(),
                is_applied: false,
            },
            TweakItem {
                id: "UltimatePower".into(),
                name: "Power Plan: Ultimate Performance".into(),
                description: "Unlocks and activates Microsoft's 'Ultimate Performance' power plan for maximum FPS.".into(),
                category: "Power".into(),
                is_applied: false,
            },
            TweakItem {
                id: "HighPerformance".into(),
                name: "Power Plan: High Performance".into(),
                description: "Activates standard Windows High Performance scheme.".into(),
                category: "Power".into(),
                is_applied: false,
            },
        ]
    }

    pub fn apply_tweak(id: &str) -> std::result::Result<String, String> {
        match id {
            "GameDvr" => {
                Self::set_dword(HKEY_CURRENT_USER, w!("System\\GameConfigStore"), w!("GameDVR_Enabled"), 0)?;
                Self::set_dword(HKEY_LOCAL_MACHINE, w!("SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR"), w!("AllowGameDVR"), 0)?;
                Ok("Disabled GameDVR background recording".into())
            }
            "VisualEffects" => {
                Self::set_dword(HKEY_CURRENT_USER, w!("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects"), w!("VisualFXSetting"), 2)?;
                Ok("Set Visual Effects to Best Performance".into())
            }
            "Telemetry" => {
                Self::set_dword(HKEY_LOCAL_MACHINE, w!("SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection"), w!("AllowTelemetry"), 0)?;
                Ok("Reduced Telemetry collection".into())
            }
            "Hibernate" => {
                let _ = std::process::Command::new("powercfg").args(&["-h", "off"]).output();
                Ok("Disabled Hibernation".into())
            }
            "UltimatePower" => {
                let _ = std::process::Command::new("powercfg").args(&["-duplicatescheme", "e9a42b02-d5df-448d-aa00-03f14749eb61"]).output();
                let _ = std::process::Command::new("powercfg").args(&["-setactive", "e9a42b02-d5df-448d-aa00-03f14749eb61"]).output();
                Ok("Activated Ultimate Performance Power Plan".into())
            }
            "HighPerformance" => {
                let _ = std::process::Command::new("powercfg").args(&["-setactive", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"]).output();
                Ok("Activated High Performance Power Plan".into())
            }
            _ => Err("Unknown tweak ID".into()),
        }
    }

    fn set_dword(hkey: HKEY, subkey: PCWSTR, value_name: PCWSTR, val: u32) -> std::result::Result<(), String> {
        unsafe {
            let mut key = HKEY::default();
            if RegCreateKeyExW(
                hkey,
                subkey,
                0,
                PCWSTR::null(),
                REG_OPTION_NON_VOLATILE,
                KEY_WRITE,
                None,
                &mut key,
                None,
            )
            .is_ok()
            {
                let bytes = val.to_ne_bytes();
                let res = RegSetValueExW(key, value_name, 0, REG_DWORD, Some(&bytes));
                let _ = RegCloseKey(key);
                if res.is_ok() {
                    Ok(())
                } else {
                    Err(format!("RegSetValueExW failed with code {}", res.0))
                }
            } else {
                Err("Failed to open registry key (admin required)".into())
            }
        }
    }
}
