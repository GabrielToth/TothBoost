use windows::core::*;
use windows::Win32::Foundation::GetLastError;
use windows::Win32::System::Registry::*;
use windows::Win32::System::Services::{
    ENUM_SERVICE_TYPE, SERVICE_ERROR, SERVICE_START_TYPE, *,
};

/// Service control with two persistence layers:
/// 1. SCM (`ChangeServiceConfigW`) so the change applies immediately.
/// 2. Registry `Start` value so the change survives reboot even when the SCM
///    call is rejected (e.g. protected services with restrictive ACLs).
pub struct ServiceManager;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ServiceStartKind {
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
}

impl ServiceStartKind {
    pub fn label(&self) -> &'static str {
        match self {
            Self::Boot => "Boot",
            Self::System => "System",
            Self::Automatic => "Automatic",
            Self::Manual => "Manual",
            Self::Disabled => "Disabled",
        }
    }
}

const ERROR_SERVICE_NOT_ACTIVE: u32 = 1062;
const ERROR_SERVICE_ALREADY_RUNNING: u32 = 1056;

impl ServiceManager {
    fn service_subkey(name: &str) -> Vec<u16> {
        format!("SYSTEM\\CurrentControlSet\\Services\\{}", name)
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect()
    }

    fn set_registry_start(name: &str, kind: ServiceStartKind) -> std::result::Result<(), String> {
        unsafe {
            let subkey = Self::service_subkey(name);
            let mut key = HKEY::default();
            let res = RegOpenKeyExW(HKEY_LOCAL_MACHINE, PCWSTR(subkey.as_ptr()), 0, KEY_SET_VALUE, &mut key);
            if res.is_err() {
                return Err(format!("Cannot open service key '{}' (access denied)", name));
            }
            let start_bytes = (kind as u32).to_ne_bytes();
            let r = RegSetValueExW(key, w!("Start"), 0, REG_DWORD, Some(&start_bytes));
            // "DelayedAutostart" only makes sense for automatic services.
            if kind != ServiceStartKind::Automatic {
                let _ = RegDeleteValueW(key, w!("DelayedAutostart"));
            }
            let _ = RegCloseKey(key);
            if r.is_ok() {
                Ok(())
            } else {
                Err(format!("Failed writing Start for '{}': code {}", name, r.0))
            }
        }
    }

    /// Writes the start type to the SCM and the registry. Succeeds when at
    /// least one layer accepted the change, so persistent disabling works even
    /// for services that reject `ChangeServiceConfigW`.
    pub fn set_start_type(name: &str, kind: ServiceStartKind) -> std::result::Result<(), String> {
        let scm_result = Self::set_scm_start(name, kind);
        let reg_result = Self::set_registry_start(name, kind);

        if reg_result.is_ok() || scm_result.is_ok() {
            Ok(())
        } else {
            Err(format!(
                "Could not change '{}' (SCM: {}; registry: {})",
                name,
                scm_result.err().unwrap_or_default(),
                reg_result.err().unwrap_or_default()
            ))
        }
    }

    fn set_scm_start(name: &str, kind: ServiceStartKind) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm()?;
            let svc = Self::open_service(scm, name, SERVICE_CHANGE_CONFIG)?;
            let res = ChangeServiceConfigW(
                svc,
                ENUM_SERVICE_TYPE(SERVICE_NO_CHANGE),
                SERVICE_START_TYPE(kind as u32),
                SERVICE_ERROR(SERVICE_NO_CHANGE),
                PCWSTR::null(),
                PCWSTR::null(),
                None,
                PCWSTR::null(),
                PCWSTR::null(),
                PCWSTR::null(),
                PCWSTR::null(),
            );
            let _ = CloseServiceHandle(svc);
            let _ = CloseServiceHandle(scm);
            res.map_err(|e| e.to_string())
        }
    }

    /// Reads the current Start value from the registry.
    /// The parent services key stays open so a full scan does not pay a
    /// registry open for every service (that froze the Services tab).
    pub fn get_start_type(name: &str) -> std::result::Result<u32, String> {
        unsafe {
            static mut SERVICES_KEY: Option<HKEY> = None;
            let key_ptr = std::ptr::addr_of_mut!(SERVICES_KEY);
            if (*key_ptr).is_none() {
                let mut key = HKEY::default();
                if RegOpenKeyExW(
                    HKEY_LOCAL_MACHINE,
                    w!("SYSTEM\\CurrentControlSet\\Services"),
                    0,
                    KEY_READ,
                    &mut key,
                )
                .is_err()
                {
                    return Err("Cannot open services key".into());
                }
                *key_ptr = Some(key);
            }

            let parent = (*key_ptr).unwrap_or_default();
            let subkey: Vec<u16> = name.encode_utf16().chain(std::iter::once(0)).collect();
            let mut key = HKEY::default();
            if RegOpenKeyExW(parent, PCWSTR(subkey.as_ptr()), 0, KEY_READ, &mut key).is_err() {
                return Err(format!("Cannot open service key '{}'", name));
            }
            let mut buf = [0u8; 4];
            let mut len = 4u32;
            let mut ty = REG_VALUE_TYPE::default();
            let result = RegQueryValueExW(
                key,
                w!("Start"),
                None,
                Some(&mut ty),
                Some(buf.as_mut_ptr()),
                Some(&mut len),
            );
            let _ = RegCloseKey(key);
            if result.is_ok() && ty == REG_DWORD {
                Ok(u32::from_ne_bytes(buf))
            } else {
                Err(format!("Failed reading Start for '{}'", name))
            }
        }
    }

    pub fn disable(name: &str) -> std::result::Result<(), String> {
        let config = Self::set_start_type(name, ServiceStartKind::Disabled);
        // Best effort: stop a running service so the setting takes effect now.
        let _ = Self::stop(name);
        config
    }

    pub fn enable_auto(name: &str) -> std::result::Result<(), String> {
        Self::set_start_type(name, ServiceStartKind::Automatic)
    }

    pub fn enable_manual(name: &str) -> std::result::Result<(), String> {
        Self::set_start_type(name, ServiceStartKind::Manual)
    }

    fn open_scm() -> std::result::Result<SC_HANDLE, String> {
        unsafe {
            OpenSCManagerW(PCWSTR::null(), PCWSTR::null(), SC_MANAGER_CONNECT)
                .map_err(|e| format!("OpenSCManager failed: {}", e))
        }
    }

    fn open_service(scm: SC_HANDLE, name: &str, access: u32) -> std::result::Result<SC_HANDLE, String> {
        let wide: Vec<u16> = name.encode_utf16().chain(std::iter::once(0)).collect();
        unsafe {
            OpenServiceW(scm, PCWSTR(wide.as_ptr()), access)
                .map_err(|e| format!("OpenService('{}') failed: {}", name, e))
        }
    }

    /// Stops the service and waits (up to ~5s) for it to reach STOPPED.
    pub fn stop(name: &str) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm()?;
            let svc = Self::open_service(scm, name, SERVICE_STOP | SERVICE_QUERY_STATUS)?;
            let mut status = SERVICE_STATUS::default();
            let res = ControlService(svc, SERVICE_CONTROL_STOP, &mut status);

            if res.is_err() {
                let code = GetLastError().0;
                let _ = CloseServiceHandle(svc);
                let _ = CloseServiceHandle(scm);
                if code == ERROR_SERVICE_NOT_ACTIVE {
                    return Ok(());
                }
                return Err(format!("Stop failed for '{}' (code {})", name, code));
            }

            for _ in 0..50 {
                let mut buffer = [0u8; std::mem::size_of::<SERVICE_STATUS_PROCESS>()];
                let mut needed = 0u32;
                if QueryServiceStatusEx(
                    svc,
                    SC_STATUS_PROCESS_INFO,
                    Some(&mut buffer),
                    &mut needed,
                )
                .is_ok()
                {
                    let sp = &*(buffer.as_ptr() as *const SERVICE_STATUS_PROCESS);
                    if sp.dwCurrentState == SERVICE_STOPPED {
                        break;
                    }
                }
                std::thread::sleep(std::time::Duration::from_millis(100));
            }

            let _ = CloseServiceHandle(svc);
            let _ = CloseServiceHandle(scm);
            Ok(())
        }
    }

    pub fn start(name: &str) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm()?;
            let svc = Self::open_service(scm, name, SERVICE_START | SERVICE_QUERY_STATUS)?;
            let res = StartServiceW(svc, None);
            let _ = CloseServiceHandle(svc);
            let _ = CloseServiceHandle(scm);

            if res.is_ok() || GetLastError().0 == ERROR_SERVICE_ALREADY_RUNNING {
                Ok(())
            } else {
                Err(format!("Start failed for '{}' (code {})", name, GetLastError().0))
            }
        }
    }

    #[allow(dead_code)]
    pub fn restart(name: &str) -> std::result::Result<(), String> {
        let _ = Self::stop(name);
        Self::start(name)
    }
}
