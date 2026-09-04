use crate::models::{ActionReport, TweakItem};
use windows::core::*;
use windows::Win32::System::Registry::*;

pub struct RegistryTweaks;

impl RegistryTweaks {
    pub fn get_available_tweaks() -> Vec<TweakItem> {
        vec![
            TweakItem {
                id: "gamedvr".into(),
                name: "Disable Windows GameDVR & Game Bar".into(),
                description: "Stops background screen recording and overlay overhead".into(),
                category: "Performance".into(),
                is_applied: false,
            },
            TweakItem {
                id: "net_throttling".into(),
                name: "Disable Network Throttling".into(),
                description: "Sets NetworkThrottlingIndex to 0xFFFFFFFF for minimum ping".into(),
                category: "Network".into(),
                is_applied: false,
            },
            TweakItem {
                id: "telemetry".into(),
                name: "Disable Windows Telemetry".into(),
                description: "Disables AllowTelemetry & DiagTrack overhead".into(),
                category: "Privacy & CPU".into(),
                is_applied: false,
            },
            TweakItem {
                id: "system_responsiveness".into(),
                name: "Set System Responsiveness to 0".into(),
                description: "Prioritizes gaming network packets over background tasks".into(),
                category: "Network".into(),
                is_applied: false,
            },
        ]
    }

    pub fn apply_all() -> ActionReport {
        let mut report = ActionReport::default();
        let tweaks = Self::get_available_tweaks();
        for tweak in tweaks {
            let res = Self::apply_tweak(&tweak.id);
            report.add(&tweak.name, "Apply Tweak", res.is_ok(), &res.err().unwrap_or("Applied successfully".into()));
        }
        report
    }

    pub fn apply_tweak(id: &str) -> std::result::Result<String, String> {
        match id {
            "gamedvr" => {
                Self::set_dword(HKEY_CURRENT_USER, w!("System\\GameConfigStore"), w!("GameDVR_Enabled"), 0)?;
                Self::set_dword(HKEY_LOCAL_MACHINE, w!("SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR"), w!("AllowGameDVR"), 0)?;
                Ok("Disabled GameDVR".into())
            }
            "net_throttling" => {
                Self::set_dword(
                    HKEY_LOCAL_MACHINE,
                    w!("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile"),
                    w!("NetworkThrottlingIndex"),
                    0xffffffff,
                )?;
                Ok("Disabled Network Throttling".into())
            }
            "telemetry" => {
                Self::set_dword(
                    HKEY_LOCAL_MACHINE,
                    w!("SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection"),
                    w!("AllowTelemetry"),
                    0,
                )?;
                Ok("Disabled Telemetry".into())
            }
            "system_responsiveness" => {
                Self::set_dword(
                    HKEY_LOCAL_MACHINE,
                    w!("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile"),
                    w!("SystemResponsiveness"),
                    0,
                )?;
                Ok("Set System Responsiveness to 0".into())
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
                Err("Failed to open or create registry key (admin required)".into())
            }
        }
    }
}
