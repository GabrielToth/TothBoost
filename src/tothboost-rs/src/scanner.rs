use crate::models::{ProcessItem, RiskLevel, ServiceItem};
use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;
use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::ProcessStatus::*;
use windows::Win32::System::Services::*;
use windows::Win32::System::Threading::*;

pub struct SystemScanner;

impl SystemScanner {
    pub fn scan_processes() -> Vec<ProcessItem> {
        let mut processes = Vec::new();
        let mut pids = [0u32; 2048];
        let mut bytes_returned = 0u32;

        unsafe {
            if K32EnumProcesses(
                pids.as_mut_ptr(),
                (pids.len() * std::mem::size_of::<u32>()) as u32,
                &mut bytes_returned,
            )
            .as_bool()
            {
                let count = (bytes_returned as usize) / std::mem::size_of::<u32>();
                for &pid in &pids[..count] {
                    if pid == 0 {
                        continue;
                    }
                    if let Ok(handle) = OpenProcess(
                        PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                        FALSE,
                        pid,
                    ) {
                        let mut name_buf = [0u16; 260];
                        let mut counters = PROCESS_MEMORY_COUNTERS::default();
                        counters.cb = std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32;

                        let name = if K32GetModuleBaseNameW(handle, HMODULE::default(), &mut name_buf) > 0 {
                            let len = name_buf.iter().position(|&c| c == 0).unwrap_or(name_buf.len());
                            OsString::from_wide(&name_buf[..len]).to_string_lossy().to_string()
                        } else {
                            format!("PID {}", pid)
                        };

                        let mem_mb = if K32GetProcessMemoryInfo(handle, &mut counters, counters.cb).as_bool() {
                            (counters.WorkingSetSize / (1024 * 1024)) as u64
                        } else {
                            0
                        };

                        let name_lower = name.to_lowercase();
                        let is_protected = pid < 100
                            || name_lower == "system"
                            || name_lower == "smss.exe"
                            || name_lower == "csrss.exe"
                            || name_lower == "wininit.exe"
                            || name_lower == "services.exe"
                            || name_lower == "lsass.exe"
                            || name_lower == "svchost.exe";

                        processes.push(ProcessItem {
                            pid,
                            name,
                            memory_mb: mem_mb,
                            is_protected,
                        });

                        let _ = CloseHandle(handle);
                    }
                }
            }
        }
        processes.sort_by(|a, b| b.memory_mb.cmp(&a.memory_mb));
        processes
    }

    pub fn scan_services() -> Vec<ServiceItem> {
        let mut services = Vec::new();
        unsafe {
            if let Ok(scm) = OpenSCManagerW(PCWSTR::null(), PCWSTR::null(), SC_MANAGER_ENUMERATE_SERVICE) {
                let mut bytes_needed = 0u32;
                let mut services_returned = 0u32;
                let mut resume_handle = 0u32;

                let _ = EnumServicesStatusExW(
                    scm,
                    SC_ENUM_PROCESS_INFO,
                    SERVICE_WIN32,
                    SERVICE_STATE_ALL,
                    None,
                    &mut bytes_needed,
                    &mut services_returned,
                    Some(&mut resume_handle),
                    PCWSTR::null(),
                );

                if bytes_needed > 0 {
                    let mut buffer = vec![0u8; bytes_needed as usize];
                    if EnumServicesStatusExW(
                        scm,
                        SC_ENUM_PROCESS_INFO,
                        SERVICE_WIN32,
                        SERVICE_STATE_ALL,
                        Some(&mut buffer),
                        &mut bytes_needed,
                        &mut services_returned,
                        Some(&mut resume_handle),
                        PCWSTR::null(),
                    )
                    .is_ok()
                    {
                        let status_ptr = buffer.as_ptr() as *const ENUM_SERVICE_STATUS_PROCESSW;
                        for i in 0..services_returned as usize {
                            let entry = &*status_ptr.add(i);
                            let name = entry.lpServiceName.to_string().unwrap_or_default();
                            let display_name = entry.lpDisplayName.to_string().unwrap_or_default();
                            let running = entry.ServiceStatusProcess.dwCurrentState == SERVICE_RUNNING;

                            let (risk, is_protected) = classify_service(&name);

                            services.push(ServiceItem {
                                name,
                                display_name,
                                description: String::new(),
                                start_type: 2,
                                status: if running { "Running".into() } else { "Stopped".into() },
                                risk,
                                is_protected,
                            });
                        }
                    }
                }
                let _ = CloseServiceHandle(scm);
            }
        }
        services
    }
}

fn classify_service(name: &str) -> (RiskLevel, bool) {
    let lower = name.to_lowercase();
    match lower.as_str() {
        "sysmain" | "diagtrack" | "mapsbroker" | "wsearch" | "fax" | "spooler" => (RiskLevel::Recommended, false),
        "xboxnetapisvc" | "xbnetapisvc" | "xboxgipsvc" | "xblauthmanager" | "xblgamesaves" => (RiskLevel::Recommended, false),
        "rpcss" | "dcomlaunch" | "samss" | "lsass" | "eventlog" | "windefend" => (RiskLevel::Protected, true),
        _ => (RiskLevel::Optional, false),
    }
}

pub struct Win32Ops;

impl Win32Ops {
    pub fn trim_memory() -> (u32, u64) {
        let mut trimmed_count = 0u32;
        let mut freed_bytes = 0u64;

        unsafe {
            let mut pids = [0u32; 2048];
            let mut bytes_returned = 0u32;
            if K32EnumProcesses(
                pids.as_mut_ptr(),
                (pids.len() * std::mem::size_of::<u32>()) as u32,
                &mut bytes_returned,
            )
            .as_bool()
            {
                let count = (bytes_returned as usize) / std::mem::size_of::<u32>();
                for &pid in &pids[..count] {
                    if pid == 0 { continue; }
                    if let Ok(handle) = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, FALSE, pid) {
                        let mut counters = PROCESS_MEMORY_COUNTERS::default();
                        counters.cb = std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32;
                        let before = if K32GetProcessMemoryInfo(handle, &mut counters, counters.cb).as_bool() {
                            counters.WorkingSetSize as u64
                        } else { 0 };

                        if K32EmptyWorkingSet(handle).as_bool() {
                            trimmed_count += 1;
                            let after = if K32GetProcessMemoryInfo(handle, &mut counters, counters.cb).as_bool() {
                                counters.WorkingSetSize as u64
                            } else { 0 };
                            if before > after {
                                freed_bytes += before - after;
                            }
                        }
                        let _ = CloseHandle(handle);
                    }
                }
            }
        }
        (trimmed_count, freed_bytes)
    }

    pub fn clean_junk() -> (usize, u64) {
        let mut count = 0;
        let mut bytes = 0u64;

        let temp_dir = std::env::temp_dir();
        if let Ok(entries) = std::fs::read_dir(temp_dir) {
            for entry in entries.flatten() {
                let path = entry.path();
                if path.is_file() {
                    if let Ok(meta) = path.metadata() {
                        let len = meta.len();
                        if std::fs::remove_file(&path).is_ok() {
                            count += 1;
                            bytes += len;
                        }
                    }
                }
            }
        }
        (count, bytes)
    }
}
