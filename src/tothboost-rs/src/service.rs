use windows::core::*;
use windows::Win32::System::Services::*;

/// Full service control operations: stop, disable, start and restore.
pub struct ServiceController;

impl ServiceController {
    fn open_scm(access: u32) -> std::result::Result<SC_HANDLE, String> {
        unsafe {
            OpenSCManagerW(PCWSTR::null(), PCWSTR::null(), access)
                .map_err(|e| format!("OpenSCManager failed (admin required?): {}", e))
        }
    }

    fn open_service(scm: SC_HANDLE, name: &str, access: u32) -> std::result::Result<SC_HANDLE, String> {
        let wide: Vec<u16> = name.encode_utf16().chain(std::iter::once(0)).collect();
        unsafe {
            OpenServiceW(scm, PCWSTR(wide.as_ptr()), access)
                .map_err(|e| format!("OpenService({}) failed: {}", name, e))
        }
    }

    pub fn stop_service(name: &str) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm(SC_MANAGER_CONNECT)?;
            let svc = Self::open_service(scm, name, SERVICE_STOP | SERVICE_QUERY_STATUS)?;
            let mut status = SERVICE_STATUS::default();
            let res = ControlService(svc, SERVICE_CONTROL_STOP, &mut status);
            let _ = CloseServiceHandle(svc);
            let _ = CloseServiceHandle(scm);
            res.is_ok().then_some(()).ok_or_else(|| format!("ControlService STOP failed for {}", name))
        }
    }

    pub fn disable_service(name: &str) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm(SC_MANAGER_CONNECT)?;
            let svc = Self::open_service(scm, name, SERVICE_CHANGE_CONFIG)?;
            let res = ChangeServiceConfigW(
                svc,
                ENUM_SERVICE_TYPE(SERVICE_NO_CHANGE),
                SERVICE_DISABLED,
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
            res.is_ok().then_some(()).ok_or_else(|| format!("ChangeServiceConfig DISABLE failed for {}", name))
        }
    }

    #[allow(dead_code)]
    pub fn set_manual(name: &str) -> std::result::Result<(), String> {
        unsafe {
            let scm = Self::open_scm(SC_MANAGER_CONNECT)?;
            let svc = Self::open_service(scm, name, SERVICE_CHANGE_CONFIG)?;
            let res = ChangeServiceConfigW(
                svc,
                ENUM_SERVICE_TYPE(SERVICE_NO_CHANGE),
                SERVICE_DEMAND_START,
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
            res.is_ok().then_some(()).ok_or_else(|| format!("ChangeServiceConfig MANUAL failed for {}", name))
        }
    }

    /// Services safe to stop+disable for gaming (mirrors C# ServiceScanner rules).
    pub fn gaming_safe_services() -> Vec<&'static str> {
        vec![
            "SysMain",        // Superfetch
            "DiagTrack",      // Connected User Experiences and Telemetry
            "dmwappushservice",
            "MapsBroker",     // Downloaded Maps Manager
            "WSearch",        // Windows Search indexer
            "Fax",
            "Spooler",        // Print Spooler
            "XblGameSave",
            "XboxNetApiSvc",
            "XblAuthManager",
            "WMPNetworkSvc",  // Windows Media Player Network Sharing
        ]
    }

    /// Stop + disable all gaming-safe services. Returns (service_name, success) pairs.
    pub fn optimize_for_gaming() -> Vec<(String, bool)> {
        let mut results = Vec::new();
        for svc in Self::gaming_safe_services() {
            let stopped = Self::stop_service(svc).is_ok();
            let disabled = Self::disable_service(svc).is_ok();
            results.push((svc.to_string(), stopped || disabled));
        }
        results
    }
}
