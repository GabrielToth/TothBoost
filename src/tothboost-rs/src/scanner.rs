use crate::blocklist::Blocklist;
use crate::models::{ProcessItem, RiskLevel, ScheduledTaskItem, ServiceItem, StartupItem};
use crate::power::{is_protected_name, process_path, ProcessController};
use crate::service::ServiceManager;
use crate::startup::StartupManager;
use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::ProcessStatus::*;
use windows::Win32::System::Services::*;
use windows::Win32::System::Threading::*;

pub struct SystemScanner;

impl SystemScanner {
    pub fn scan_processes() -> Vec<ProcessItem> {
        let blocked = Blocklist::load();
        let mut processes = Vec::with_capacity(192);

        unsafe {
            for (pid, name) in ProcessController::enumerate() {
                let mut memory_mb = 0u64;
                if let Ok(handle) = OpenProcess(
                    PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                    FALSE,
                    pid,
                ) {
                    let mut counters = PROCESS_MEMORY_COUNTERS::default();
                    counters.cb = std::mem::size_of::<PROCESS_MEMORY_COUNTERS>() as u32;
                    if K32GetProcessMemoryInfo(handle, &mut counters, counters.cb).as_bool() {
                        memory_mb = counters.WorkingSetSize as u64 / (1024 * 1024);
                    }
                    let _ = CloseHandle(handle);
                }

                let protected = pid < 100 || is_protected_name(&name);
                processes.push(ProcessItem {
                    pid,
                    path: process_path(pid),
                    is_blocked: Blocklist::contains(&blocked, &name),
                    is_protected: protected,
                    name,
                    memory_mb,
                });
            }
        }

        processes.sort_by(|a, b| {
            b.memory_mb
                .cmp(&a.memory_mb)
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
        });
        processes
    }

    pub fn scan_services() -> Vec<ServiceItem> {
        let mut services = Vec::new();
        unsafe {
            let Ok(scm) = OpenSCManagerW(PCWSTR::null(), PCWSTR::null(), SC_MANAGER_ENUMERATE_SERVICE)
            else {
                return services;
            };

            let mut buffer_size: u32 = 64 * 1024;
            let mut success = false;

            for _ in 0..4 {
                let mut needed = 0u32;
                let mut returned = 0u32;
                let mut buffer = vec![0u8; buffer_size as usize];

                let res = EnumServicesStatusExW(
                    scm,
                    SC_ENUM_PROCESS_INFO,
                    SERVICE_WIN32,
                    SERVICE_STATE_ALL,
                    Some(buffer.as_mut_slice()),
                    &mut needed,
                    &mut returned,
                    None,
                    PCWSTR::null(),
                );

                if res.is_err() {
                    if needed > buffer_size {
                        buffer_size = needed + 16 * 1024;
                        continue;
                    }
                    break;
                }

                let status_ptr = buffer.as_ptr() as *const ENUM_SERVICE_STATUS_PROCESSW;
                for i in 0..returned as usize {
                    let entry = &*status_ptr.add(i);
                    let name = entry.lpServiceName.to_string().unwrap_or_default();
                    let display_name = entry.lpDisplayName.to_string().unwrap_or_default();
                    let running = entry.ServiceStatusProcess.dwCurrentState == SERVICE_RUNNING;

                    let start_type = ServiceManager::get_start_type(&name).unwrap_or(2);
                    let (risk, protected) = classify_service(&name);

                    services.push(ServiceItem {
                        name,
                        display_name,
                        description: String::new(),
                        start_type,
                        status: if running { "Running".into() } else { "Stopped".into() },
                        risk,
                        is_protected: protected,
                        process_id: entry.ServiceStatusProcess.dwProcessId,
                    });
                }
                success = true;
                break;
            }

            let _ = CloseServiceHandle(scm);
            if !success && services.is_empty() {
                // Enumeration failed entirely (rare); keep the UI usable.
                return services;
            }
        }

        services.sort_by(|a, b| {
            a.display_name
                .to_lowercase()
                .cmp(&b.display_name.to_lowercase())
        });
        services
    }

    pub fn scan_startup() -> Vec<StartupItem> {
        StartupManager::scan_all()
    }

    #[allow(dead_code)]
    pub fn scan_scheduled_tasks() -> Vec<ScheduledTaskItem> {
        let mut tasks = Vec::new();
        let Ok(output) = crate::power::run_hidden("schtasks.exe", &["/query", "/fo", "CSV", "/v"])
        else {
            return tasks;
        };

        for line in output.lines().skip(1) {
            let columns = parse_csv_line(line);
            if columns.len() < 3 {
                continue;
            }
            let full_path = columns[0].clone();
            let name = full_path.rsplit('\\').next().unwrap_or("").to_string();
            if name.is_empty() {
                continue;
            }
            let status = columns
                .get(2)
                .cloned()
                .unwrap_or_else(|| "Ready".into())
                .to_lowercase();
            tasks.push(ScheduledTaskItem {
                full_path,
                name,
                author: columns.get(4).cloned().unwrap_or_else(|| "System".into()),
                is_enabled: !status.contains("disabled"),
            });
        }
        tasks
    }
}

/// Minimal CSV splitter that respects double quotes (schtasks output).
#[allow(dead_code)]
fn parse_csv_line(line: &str) -> Vec<String> {
    let mut fields = Vec::new();
    let mut current = String::new();
    let mut in_quotes = false;
    let mut chars = line.chars().peekable();

    while let Some(c) = chars.next() {
        match c {
            '"' if in_quotes && chars.peek() == Some(&'"') => {
                current.push('"');
                chars.next();
            }
            '"' => in_quotes = !in_quotes,
            ',' if !in_quotes => {
                fields.push(std::mem::take(&mut current));
            }
            _ => current.push(c),
        }
    }
    fields.push(current);
    fields
}

fn classify_service(name: &str) -> (RiskLevel, bool) {
    let lower = name.to_lowercase();
    let protected = matches!(
        lower.as_str(),
        "rpcss"
            | "dcomlaunch"
            | "samss"
            | "lsass"
            | "eventlog"
            | "windefend"
            | "brokerinfrastructure"
            | "cryptsvc"
            | "trustedinstaller"
            | "schedule"
    );
    if protected {
        return (RiskLevel::Protected, true);
    }

    let recommended = matches!(
        lower.as_str(),
        "sysmain"
            | "diagtrack"
            | "dmwappushservice"
            | "mapsbroker"
            | "wsearch"
            | "fax"
            | "retaildemo"
            | "walletservice"
            | "phonesvc"
            | "xboxgipsvc"
            | "xboxnetapisvc"
            | "xblauthmanager"
            | "xblgamesave"
            | "xbgm"
    );
    if recommended {
        return (RiskLevel::Recommended, false);
    }

    let caution = matches!(
        lower.as_str(),
        "spooler" | "bits" | "wuauserv" | "w32time" | "defragsvc"
    );
    if caution {
        return (RiskLevel::Warning, false);
    }

    (RiskLevel::Optional, false)
}
