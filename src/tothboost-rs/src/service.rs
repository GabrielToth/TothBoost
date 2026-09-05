use windows::core::*;
use windows::Win32::System::Registry::*;
use windows::Win32::System::Services::*;

/// Mirrors the C# ServiceManager behavior:
/// - Disable/Enable writes to HKLM\SYSTEM\CurrentControlSet\Services\{name}\Start
///   (non-destructive, fully reversible, same as the C# app).
/// - Stop/Start/Restart go through the Service Control Manager.
pub struct ServiceManager;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ServiceStartKind {
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
}

impl ServiceManager {
    fn services_root() -> Vec<u16> {
        "SYSTEM\\CurrentControlSet\\Services".encode_utf16().chain(std::iter::once(0)).collect()
    }

    fn service_subkey(name: &str) -> Vec<u16> {
        format!("SYSTEM\\CurrentControlSet\\Services\\{}", name)
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect()
    }

    /// Set Start value in registry (like C# EnableService/DisableService).
    pub fn set_start_type(name: &str, kind: ServiceStartKind) -> std::result::Result<(), String> {
        unsafe {
            let subkey = Self::service_subkey(name);
            let mut key = HKEY::default();
            let res = RegOpenKeyExW(HKEY_LOCAL_MACHINE, PCWSTR(subkey.as_ptr()), 0, KEY_WRITE, &mut key);
            if res.is_err() {
                return Err(format!("Cannot open service key '{}' (access denied / not found)", name));
            }
            let start_bytes = (kind as u32).to_ne_bytes();
            let r = RegSetValueExW(key, w!("Start"), 0, REG_DWORD, Some(&start_bytes));
            let _ = RegCloseKey(key);
            if r.is_ok() {
                Ok(())
            } else {
                Err(format!("Failed writing Start for '{}': code {}", name, r.0))
            }
        }
    }

    /// Read current Start value from registry.
    pub fn get_start_type(name: &str) -> std::result::Result<u32, String> {
        unsafe {
            let subkey = Self::service_subkey(name);
            let mut key = HKEY::default();
            if RegOpenKeyExW(HKEY_LOCAL_MACHINE, PCWSTR(subkey.as_ptr()), 0, KEY_READ, &mut key).is_err() {
                return Err(format!("Cannot open service key '{}'", name));
            }
            let mut buf = [0u8; 4];
            let mut len = 4u32;
            let mut ty = REG_VALUE_TYPE::default();
            let r = RegQueryValueExW(key, w!("Start"), None, Some(&mut ty), Some(buf.as_mut_ptr()), Some(&mut len));
            let _ = RegCloseKey(key);
            if r.is_ok() && ty == REG_DWORD {
                Ok(u32::from_ne_bytes(buf))
            } else {
                Err(format!("Failed reading Start for '{}'", name))
            }
        }
    }

    pub fn disable(name: &str) -> std::result::Result<(), String> {
        Self::set_start_type(name, ServiceStartKind::Disabled)
    }

    pub fn enable_auto(name: &str) -> std::result::Result<(), String> {
        Self::set_start_type(name, ServiceStartKind::Automatic)
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

    pub fn stop(name: &str) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm()?;
            let svc = Self::open_service(scm, name, SERVICE_STOP | SERVICE_QUERY_STATUS)?;
            let mut status = SERVICE_STATUS::default();
            let res = ControlService(svc, SERVICE_CONTROL_STOP, &mut status);
            let _ = CloseServiceHandle(svc);
            let _ = CloseServiceHandle(scm);
            res.is_ok().then_some(()).ok_or_else(|| format!("Stop failed for '{}'", name))
        }
    }

    pub fn start(name: &str) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm()?;
            let svc = Self::open_service(scm, name, SERVICE_START)?;
            let res = StartServiceW(svc, None);
            let _ = CloseServiceHandle(svc);
            let _ = CloseServiceHandle(scm);
            res.is_ok().then_some(()).ok_or_else(|| format!("Start failed for '{}'", name))
        }
    }

    pub fn restart(name: &str) -> std::result::Result<(), String> {
        let _ = Self::stop(name);
        Self::start(name)
    }
}
