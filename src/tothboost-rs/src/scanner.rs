use crate::models::{ProcessItem, RiskLevel, ScheduledTaskItem, ServiceItem, StartupItem};
use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;
use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::ProcessStatus::*;
use windows::Win32::System::Registry::*;
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
                            || name_lower == "svchost.exe"
                            || name_lower == "explorer.exe";

                        processes.push(ProcessItem {
                            pid,
                            name,
                            memory_mb: mem_mb,
                            cpu_percent: 0.0,
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
                    Some(&mut resume_handle as *mut u32),
                    PCWSTR::null(),
                );

                if bytes_needed > 0 {
                    let mut buffer = vec![0u8; bytes_needed as usize];
                    if EnumServicesStatusExW(
                        scm,
                        SC_ENUM_PROCESS_INFO,
                        SERVICE_WIN32,
                        SERVICE_STATE_ALL,
                        Some(buffer.as_mut_slice()),
                        &mut bytes_needed,
                        &mut services_returned,
                        Some(&mut resume_handle as *mut u32),
                        PCWSTR::null(),
                    )
                    .is_ok()                    {
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

    pub fn scan_startup() -> Vec<StartupItem> {
        let mut items = Vec::new();
        let run_keys = [
            (HKEY_LOCAL_MACHINE, w!("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"), "HKLM Run"),
            (HKEY_CURRENT_USER, w!("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"), "HKCU Run"),
        ];

        for (hive, key_path, location) in run_keys {
            unsafe {
                let mut key = HKEY::default();
                if RegOpenKeyExW(hive, key_path, 0, KEY_READ, &mut key).is_ok() {
                    let mut index = 0u32;
                    loop {
                        let mut name_buf = [0u16; 260];
                        let mut name_len = name_buf.len() as u32;
                        let mut type_val = 0u32;
                        let mut data_buf = [0u8; 1024];
                        let mut data_len = data_buf.len() as u32;

                        if RegEnumValueW(
                            key,
                            index,
                            PWSTR(name_buf.as_mut_ptr()),
                            &mut name_len,
                            None,
                            Some(&mut type_val),
                            Some(data_buf.as_mut_ptr()),
                            Some(&mut data_len),
                        )
                        .is_ok()
                        {
                            let name = String::from_utf16_lossy(&name_buf[..name_len as usize]);
                            let cmd = if type_val == REG_SZ.0 || type_val == REG_EXPAND_SZ.0 {
                                let slice = std::slice::from_raw_parts(data_buf.as_ptr() as *const u16, (data_len / 2) as usize);
                                let clean_len = slice.iter().position(|&c| c == 0).unwrap_or(slice.len());
                                String::from_utf16_lossy(&slice[..clean_len])
                            } else {
                                String::new()
                            };

                            items.push(StartupItem {
                                name,
                                location: location.into(),
                                location_key: "Run".into(),
                                command: cmd,
                                is_enabled: true,
                            });
                            index += 1;
                        } else {
                            break;
                        }
                    }
                    let _ = RegCloseKey(key);
                }
            }
        }
        items
    }

    pub fn scan_scheduled_tasks() -> Vec<ScheduledTaskItem> {
        let mut tasks = Vec::new();
        if let Ok(output) = std::process::Command::new("schtasks.exe").args(&["/query", "/fo", "CSV", "/v"]).output() {
            let csv = String::from_utf8_lossy(&output.stdout);
            for line in csv.lines().skip(1) {
                let parts: Vec<&str> = line.split(',').map(|s| s.trim_matches('"')).collect();
                if parts.len() >= 3 {
                    let task_name = parts[1].to_string();
                    let status = parts[2].to_string();
                    if !task_name.is_empty() && task_name != "TaskName" {
                        tasks.push(ScheduledTaskItem {
                            full_path: parts[0].to_string(),
                            name: task_name,
                            author: parts.get(4).unwrap_or(&"System").to_string(),
                            is_enabled: status.to_lowercase() != "disabled",
                        });
                    }
                }
            }
        }
        tasks
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
