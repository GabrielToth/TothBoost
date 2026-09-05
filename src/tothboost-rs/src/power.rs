use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::Power::*;
use windows::Win32::System::Threading::*;

pub struct PowerManager;

/// Ultimate Performance / High Performance GUIDs
const GUID_HIGH_PERFORMANCE: GUID = GUID::from_u128(0x8c5e7fda_e8bf_4a96_9a85_a6e23a8c635c);

impl PowerManager {
    /// Switches Windows active power scheme to High Performance.
    pub fn set_high_performance() -> std::result::Result<(), String> {
        unsafe {
            let res = PowerSetActiveScheme(None, Some(&GUID_HIGH_PERFORMANCE));
            if res.is_ok() {
                Ok(())
            } else {
                Err(format!("PowerSetActiveScheme failed with code {}", res.0))
            }
        }
    }

    /// Query current active scheme GUID.
    pub fn get_active_scheme() -> std::result::Result<GUID, String> {
        unsafe {
            let mut ptr: *mut GUID = std::ptr::null_mut();
            let res = PowerGetActiveScheme(None, &mut ptr);
            if res.is_ok() && !ptr.is_null() {
                let guid = *ptr;
                let _ = LocalFree(HLOCAL(ptr as _));
                Ok(guid)
            } else {
                Err(format!("PowerGetActiveScheme failed: {}", res.0))
            }
        }
    }
}

pub struct ProcessController;

impl ProcessController {
    /// Safe process termination. Refuses protected system processes.
    pub fn terminate_process(pid: u32) -> std::result::Result<(), String> {
        if pid < 100 {
            return Err("Cannot terminate core system process (PID < 100)".into());
        }

        unsafe {
            let handle = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_INFORMATION, FALSE, pid)
                .map_err(|e| format!("OpenProcess({}) failed: {}", pid, e))?;

            let res = TerminateProcess(handle, 1);
            let _ = CloseHandle(handle);

            if res.is_ok() {
                Ok(())
            } else {
                Err(format!("TerminateProcess({}) failed", pid))
            }
        }
    }
}

pub struct NetworkManager;

impl NetworkManager {
    /// Flushes DNS Resolver cache using ipconfig /flushdns (subprocess execution)
    pub fn flush_dns() -> std::result::Result<String, String> {
        let output = std::process::Command::new("ipconfig")
            .arg("/flushdns")
            .output()
            .map_err(|e| format!("Failed to run ipconfig /flushdns: {}", e))?;

        if output.status.success() {
            Ok(String::from_utf8_lossy(&output.stdout).trim().to_string())
        } else {
            Err(String::from_utf8_lossy(&output.stderr).trim().to_string())
        }
    }
}
