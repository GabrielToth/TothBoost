use crate::elevation::ElevationManager;
use crate::models::{ActionReport, RiskLevel};
use crate::power::{NetworkManager, PowerManager, ProcessController};
use crate::scanner::{SystemScanner, Win32Ops};
use crate::service::ServiceController;
use crate::tweaks::RegistryTweaks;

#[test]
fn test_models_action_report() {
    let mut report = ActionReport::default();
    assert_eq!(report.total_actions, 0);
    assert_eq!(report.success_count, 0);
    assert_eq!(report.error_count, 0);

    report.add("TestTarget", "TestAction", true, "Success detail");
    assert_eq!(report.total_actions, 1);
    assert_eq!(report.success_count, 1);
    assert_eq!(report.error_count, 0);
    assert_eq!(report.messages.len(), 1);
    assert!(report.messages[0].contains("OK"));

    report.add("TestTarget2", "TestAction2", false, "Failure detail");
    assert_eq!(report.total_actions, 2);
    assert_eq!(report.success_count, 1);
    assert_eq!(report.error_count, 1);
    assert_eq!(report.messages.len(), 2);
    assert!(report.messages[1].contains("FAIL"));
}

#[test]
fn test_risk_level_label() {
    assert_eq!(RiskLevel::Protected.label(), "CRITICAL — Protected");
    assert_eq!(RiskLevel::Recommended.label(), "Recommended for Gaming");
    assert_eq!(RiskLevel::Warning.label(), "Caution");
    assert_eq!(RiskLevel::Optional.label(), "Optional");
    assert_eq!(RiskLevel::Info.label(), "Unclassified");
}

#[test]
fn test_available_tweaks() {
    let tweaks = RegistryTweaks::get_available_tweaks();
    assert!(!tweaks.is_empty());
    assert!(tweaks.iter().any(|t| t.id == "gamedvr"));
    assert!(tweaks.iter().any(|t| t.id == "net_throttling"));
    assert!(tweaks.iter().any(|t| t.id == "telemetry"));
}

#[test]
fn test_process_scanner() {
    let procs = SystemScanner::scan_processes();
    assert!(!procs.is_empty(), "Process list should not be empty on Windows");
}

#[test]
fn test_service_scanner() {
    let services = SystemScanner::scan_services();
    assert!(!services.is_empty(), "Service list should not be empty on Windows");
}

#[test]
fn test_junk_cleaner() {
    let (files, bytes) = Win32Ops::clean_junk();
    let _ = files;
    let _ = bytes;
}

#[test]
fn test_elevation_check() {
    let _is_admin = ElevationManager::is_elevated();
}

#[test]
fn test_gaming_safe_services_list() {
    let list = ServiceController::gaming_safe_services();
    assert!(!list.is_empty());
    assert!(list.contains(&"SysMain"));
    assert!(list.contains(&"WSearch"));
}

#[test]
fn test_process_terminate_protection() {
    // Attempting to terminate PID < 100 should return Err
    assert!(ProcessController::terminate_process(0).is_err());
    assert!(ProcessController::terminate_process(4).is_err());
}

#[test]
fn test_network_flush_dns() {
    let res = NetworkManager::flush_dns();
    assert!(res.is_ok(), "DNS flush command should succeed on Windows");
}

#[test]
fn test_power_active_scheme() {
    let scheme = PowerManager::get_active_scheme();
    assert!(scheme.is_ok(), "Power active scheme query should return GUID");
}
