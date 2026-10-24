use std::path::Path;
use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::ProcessStatus::*;
use windows::Win32::System::Threading::*;

/// Processes that must never be terminated by TothBoost, regardless of the
/// user's confirmation. Killing these can bugcheck or log the user out.
pub const PROTECTED_PROCESSES: &[&str] = &[
    "system",
    "registry",
    "memory compression",
    "smss.exe",
    "csrss.exe",
    "wininit.exe",
    "services.exe",
    "lsass.exe",
    "winlogon.exe",
    "svchost.exe",
    "fontdrvhost.exe",
    "dwm.exe",
    "sihost.exe",
    "ctfmon.exe",
    "taskhostw.exe",
    "explorer.exe",
    "audiodg.exe",
    "spoolsv.exe",
    "msmpeng.exe",
    "securityhealthservice.exe",
    "lsaiso.exe",
];

pub fn is_protected_name(name: &str) -> bool {
    let lower = name.trim().to_ascii_lowercase();
    PROTECTED_PROCESSES.iter().any(|p| *p == lower)
}

/// File name of the running executable (lowercase), used to make sure the
/// blocklist never kills TothBoost itself.
pub fn own_process_name() -> String {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.file_name().map(|n| n.to_string_lossy().to_ascii_lowercase()))
        .unwrap_or_else(|| "tothboost.exe".to_string())
}

pub struct ProcessController;

impl ProcessController {
    /// Enumerates (pid, image name) pairs using the ToolHelp-free PSAPI API.
    /// Each OpenProcess is closed immediately so the footprint stays tiny.
    pub fn enumerate() -> Vec<(u32, String)> {
        let mut result = Vec::new();
        let mut pids = vec![0u32; 4096];
        let mut bytes_returned = 0u32;

        unsafe {
            if !K32EnumProcesses(
                pids.as_mut_ptr(),
                (pids.len() * std::mem::size_of::<u32>()) as u32,
                &mut bytes_returned,
            )
            .as_bool()
            {
                return result;
            }

            let count = (bytes_returned as usize) / std::mem::size_of::<u32>();
            for &pid in &pids[..count] {
                if pid == 0 {
                    continue;
                }
                if let Some(name) = process_name(pid) {
                    result.push((pid, name));
                }
            }
        }
        result
    }

    /// Safe process termination. Refuses protected system processes and self.
    pub fn terminate_process(pid: u32) -> std::result::Result<(), String> {
        if pid < 100 {
            return Err("Cannot terminate core system process (PID < 100)".into());
        }
        let name = unsafe { process_name(pid) }.unwrap_or_default();
        if is_protected_name(&name) {
            return Err(format!("'{}' is a protected system process", name));
        }
        if name.eq_ignore_ascii_case(&own_process_name()) {
            return Err("Refusing to terminate TothBoost itself".into());
        }
        terminate_raw(pid)
    }

    /// Terminates every process whose image name matches `name`.
    /// Returns the number of processes killed. Never kills protected processes.
    pub fn terminate_by_name(name: &str) -> std::result::Result<usize, String> {
        let lower = name.trim().to_ascii_lowercase();
        if lower.is_empty() {
            return Err("Empty process name".into());
        }
        if is_protected_name(&lower) {
            return Err(format!("'{}' is a protected system process", lower));
        }
        if lower == own_process_name() {
            return Err("Refusing to terminate TothBoost itself".into());
        }

        let mut killed = 0usize;
        for (pid, pname) in Self::enumerate() {
            if pname.eq_ignore_ascii_case(&lower) && terminate_raw(pid).is_ok() {
                killed += 1;
            }
        }
        Ok(killed)
    }

    /// Trims the working set of every non-trivial process.
    /// Returns (processes trimmed, bytes released).
    pub fn trim_working_sets() -> (usize, u64) {
        let mut trimmed = 0usize;
        let mut freed = 0u64;

        unsafe {
            for (pid, _) in Self::enumerate() {
                if pid < 100 {
                    continue;
                }
                if let Ok(handle) = OpenProcess(
                    PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                    FALSE,
                    pid,
                ) {
                    let mut before = PROCESS_MEMORY_COUNTERS::default();
                    before.cb = std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32;
                    if K32GetProcessMemoryInfo(handle, &mut before, before.cb).as_bool()
                        && before.WorkingSetSize >= 10 * 1024 * 1024
                        && K32EmptyWorkingSet(handle).as_bool()
                    {
                        let mut after = PROCESS_MEMORY_COUNTERS::default();
                        after.cb = std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32;
                        if K32GetProcessMemoryInfo(handle, &mut after, after.cb).as_bool() {
                            trimmed += 1;
                            freed += before
                                .WorkingSetSize
                                .saturating_sub(after.WorkingSetSize) as u64;
                        }
                    }
                    let _ = CloseHandle(handle);
                }
            }
        }

        (trimmed, freed)
    }
}

fn terminate_raw(pid: u32) -> std::result::Result<(), String> {
    unsafe {
        let handle = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid)
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

/// Reads the base image name of a process (e.g. "chrome.exe").
pub unsafe fn process_name(pid: u32) -> Option<String> {
    let path = process_path(pid);
    if path.is_empty() {
        return None;
    }
    Path::new(&path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .filter(|n| !n.is_empty())
}

/// Reads the full executable path of a process (empty when inaccessible).
pub unsafe fn process_path(pid: u32) -> String {
    let Ok(handle) = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid) else {
        return String::new();
    };
    let mut buffer = [0u16; 1024];
    let mut size = buffer.len() as u32;
    let ok = QueryFullProcessImageNameW(
        handle,
        PROCESS_NAME_WIN32,
        PWSTR(buffer.as_mut_ptr()),
        &mut size,
    )
    .is_ok();
    let _ = CloseHandle(handle);

    if ok {
        String::from_utf16_lossy(&buffer[..size as usize])
    } else {
        String::new()
    }
}

/// Runs a Windows command without flashing a console window and returns stdout.
pub fn run_hidden(program: &str, args: &[&str]) -> std::result::Result<String, String> {
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;

    let output = std::process::Command::new(program)
        .args(args)
        .creation_flags(CREATE_NO_WINDOW)
        .output()
        .map_err(|e| format!("{} could not start: {}", program, e))?;

    if output.status.success() {
        Ok(String::from_utf8_lossy(&output.stdout).to_string())
    } else {
        let err = String::from_utf8_lossy(&output.stderr).to_string();
        Err(format!(
            "{} exited with {}: {}",
            program,
            output.status.code().unwrap_or(-1),
            err.trim()
        ))
    }
}

/// Flushes the Windows DNS resolver cache.
pub fn flush_dns() -> std::result::Result<(), String> {
    run_hidden("ipconfig.exe", &["/flushdns"]).map(|_| ())
}

/// Deletes junk from well-known cache locations. Locked files are skipped.
/// Returns (files removed, bytes freed, errors).
pub fn clean_junk() -> (usize, u64, Vec<String>) {
    let mut targets: Vec<std::path::PathBuf> = Vec::new();
    if let Ok(temp) = std::env::var("TEMP") {
        targets.push(temp.into());
    }
    if let Ok(local) = std::env::var("LOCALAPPDATA") {
        targets.push(Path::new(&local).join("D3DSCache"));
        targets.push(Path::new(&local).join("Microsoft\\Windows\\INetCache"));
    }
    targets.push(Path::new("C:\\Windows\\Temp").to_path_buf());
    targets.push(Path::new("C:\\Windows\\Prefetch").to_path_buf());
    targets.push(Path::new("C:\\Windows\\SoftwareDistribution\\Download").to_path_buf());

    let mut files = 0usize;
    let mut bytes = 0u64;
    let mut errors = Vec::new();

    for target in targets {
        if !target.exists() {
            continue;
        }
        clean_dir(&target, &mut files, &mut bytes, &mut errors, 0);
    }

    (files, bytes, errors)
}

fn clean_dir(
    dir: &Path,
    files: &mut usize,
    bytes: &mut u64,
    errors: &mut Vec<String>,
    depth: u32,
) {
    if depth > 8 {
        return;
    }
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let Ok(meta) = entry.metadata() else { continue };
        if meta.is_dir() {
            clean_dir(&path, files, bytes, errors, depth + 1);
            let _ = std::fs::remove_dir(&path);
        } else if std::fs::remove_file(&path).is_ok() {
            *files += 1;
            *bytes += meta.len();
        } else if errors.len() < 20 {
            errors.push(format!("locked: {}", path.display()));
        }
    }
}

#[cfg(test)]
pub fn os_name_from_path(path: &str) -> String {
    Path::new(path)
        .file_name()
        .map(|n| n.to_string_lossy().to_ascii_lowercase())
        .unwrap_or_default()
}
