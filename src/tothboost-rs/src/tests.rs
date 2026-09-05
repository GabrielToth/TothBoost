use crate::elevation::ElevationManager;
use crate::models::{ActionReport, RiskLevel};
use crate::power::ProcessController;
use crate::profile::{ProfileData, ProfileService};
use crate::scanner::SystemScanner;
use crate::tweaks::RegistryTweaks;

#[test]
fn test_models_action_report() {
    let mut report = ActionReport::default();
    assert_eq!(report.total_actions, 0);
    assert_eq!(report.success_count, 0);

    report.add("Target1", "Action1", true, "OK detail");
    assert_eq!(report.total_actions, 1);
    assert_eq!(report.success_count, 1);

    report.add("Target2", "Action2", false, "Fail detail");
    assert_eq!(report.total_actions, 2);
    assert_eq!(report.error_count, 1);
}

#[test]
fn test_risk_level_labels() {
    assert_eq!(RiskLevel::Protected.label(), "CRITICAL — Protected");
    assert_eq!(RiskLevel::Recommended.label(), "Recommended for Gaming");
    assert_eq!(RiskLevel::Warning.label(), "Caution");
    assert_eq!(RiskLevel::Optional.label(), "Optional");
    assert_eq!(RiskLevel::Info.label(), "Unclassified");
}

#[test]
fn test_tweaks_catalog() {
    let tweaks = RegistryTweaks::get_available_tweaks();
    assert!(!tweaks.is_empty());
    assert!(tweaks.iter().any(|t| t.id == "GameDvr"));
    assert!(tweaks.iter().any(|t| t.id == "VisualEffects"));
    assert!(tweaks.iter().any(|t| t.id == "Telemetry"));
    assert!(tweaks.iter().any(|t| t.id == "Hibernate"));
    assert!(tweaks.iter().any(|t| t.id == "UltimatePower"));
    assert!(tweaks.iter().any(|t| t.id == "HighPerformance"));
}

#[test]
fn test_process_scanner() {
    let procs = SystemScanner::scan_processes();
    assert!(!procs.is_empty(), "Processes should be enumerated");
    // Processes are enumerated and non-empty
    assert!(procs.iter().any(|p| !p.name.is_empty() || p.pid > 0));
}

#[test]
fn test_service_scanner() {
    let services = SystemScanner::scan_services();
    assert!(!services.is_empty(), "Services should be enumerated");
}

#[test]
fn test_startup_scanner() {
    let items = SystemScanner::scan_startup();
    // Non-failing scan validation
    let _ = items;
}

#[test]
fn test_scheduled_task_scanner() {
    let tasks = SystemScanner::scan_scheduled_tasks();
    // Non-failing scan validation
    let _ = tasks;
}

#[test]
fn test_process_terminate_protection() {
    assert!(ProcessController::terminate_process(0).is_err());
    assert!(ProcessController::terminate_process(4).is_err());
}

#[test]
fn test_elevation_check() {
    let _is_admin = ElevationManager::is_elevated();
}

#[test]
fn test_profile_save_and_load() {
    let dummy = ProfileData {
        name: "TestProfile".into(),
        created_utc: "2026-09-04T00:00:00Z".into(),
        services: vec![("SysMain".into(), 4)],
        tweaks: vec![("GameDvr".into(), true)],
        startup: vec![("TestApp".into(), false)],
        tasks: vec![("\\TestTask".into(), false)],
    };

    assert!(ProfileService::save_profile(&dummy).is_ok());
    let loaded = ProfileService::load_profile("TestProfile");
    assert!(loaded.is_ok());
    let loaded_data = loaded.unwrap();
    assert_eq!(loaded_data.name, "TestProfile");
    assert_eq!(loaded_data.services.len(), 1);
}
