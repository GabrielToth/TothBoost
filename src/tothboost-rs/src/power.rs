use windows::Win32::Foundation::*;
use windows::Win32::System::Threading::*;

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
