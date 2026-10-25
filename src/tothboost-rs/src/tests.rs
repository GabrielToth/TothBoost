use crate::blocklist::Blocklist;
use crate::elevation::ElevationManager;
use crate::models::{ActionReport, RiskLevel, StartupKind};
use crate::power::ProcessController;
use crate::profile::{ProfileData, ProfileService, StartupState};
use crate::scanner::{classify_service, parse_csv_line, SystemScanner};
use crate::service::{ServiceManager, ServiceStartKind};
use crate::tweaks::RegistryTweaks;
use std::os::windows::process::CommandExt;
use std::process::{Command, Stdio};
use std::sync::Mutex;

/// Serializes tests that mutate the process-wide profile directory env var.
static PROFILE_ENV_LOCK: Mutex<()> = Mutex::new(());
/// Serializes tests that mutate the process-wide blocklist directory env var.
static BLOCKLIST_ENV_LOCK: Mutex<()> = Mutex::new(());

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
    assert_eq!(report.failures().len(), 1);
    assert!(report.summary().contains("2 action(s)"));
}

#[test]
fn test_risk_level_labels() {
    assert_eq!(RiskLevel::Protected.label(), "CRITICAL — Protected");
    assert_eq!(RiskLevel::Recommended.label(), "Recommended for Gaming");
    assert_eq!(RiskLevel::Warning.label(), "Caution");
    assert_eq!(RiskLevel::Optional.label(), "Optional");
    assert_eq!(RiskLevel::Info.label(), "Unclassified");
    assert_eq!(RiskLevel::Protected.short(), "PROTECTED");
}

#[test]
fn test_tweaks_catalog() {
    let tweaks = RegistryTweaks::get_available_tweaks();
    assert!(!tweaks.is_empty());
    for id in [
        "GameDvr",
        "VisualEffects",
        "Telemetry",
        "Hibernate",
        "NetworkThrottling",
        "UltimatePower",
        "HighPerformance",
    ] {
        assert!(
            tweaks.iter().any(|t| t.id == id),
            "missing tweak {}",
            id
        );
    }
}

#[test]
fn test_process_scanner() {
    let procs = SystemScanner::scan_processes();
    assert!(!procs.is_empty(), "Processes should be enumerated");
    assert!(procs.iter().any(|p| p.pid > 0 && !p.name.is_empty()));
    // Protected processes must be flagged so the UI can refuse to kill them.
    assert!(procs.iter().any(|p| p.is_protected));
}

#[test]
fn test_service_scanner() {
    let services = SystemScanner::scan_services();
    assert!(!services.is_empty(), "Services should be enumerated");
    assert!(
        services.iter().all(|s| s.start_type <= 4),
        "valid start types only"
    );
    assert!(services.iter().any(|s| !s.name.is_empty()));
}

#[test]
fn test_startup_scanner() {
    let items = SystemScanner::scan_startup();
    // Must never panic, even with no startup entries.
    for item in &items {
        assert!(!item.name.is_empty());
    }
}

#[test]
fn test_scheduled_task_scanner_does_not_panic() {
    let tasks = SystemScanner::scan_scheduled_tasks();
    for task in &tasks {
        assert!(!task.name.is_empty());
    }
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
fn test_blocklist_normalization() {
    assert_eq!(
        Blocklist::normalize("C:\\Games\\BadApp.EXE"),
        Some("badapp.exe".into())
    );
    assert_eq!(Blocklist::normalize("  tool.exe  "), Some("tool.exe".into()));
    assert_eq!(Blocklist::normalize("lsass.exe"), None);
    assert_eq!(Blocklist::normalize("svchost.exe"), None);
    assert_eq!(Blocklist::normalize(""), None);
}

#[test]
fn test_blocklist_load_is_safe() {
    // Must never panic and always return normalized lowercase names.
    for name in Blocklist::load() {
        assert_eq!(name, name.to_lowercase());
        assert!(!name.is_empty());
    }
}

#[test]
fn test_blocklist_roundtrip_persistence() {
    let _guard = BLOCKLIST_ENV_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let dir = std::env::temp_dir().join(format!("tothboost-blocklist-test-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::env::set_var("TOTHBOOST_DATA_DIR", &dir);

    let list = Blocklist::add_many(&[
        "C:\\Games\\BadApp.EXE".into(),
        "lsass.exe".into(), // protected: must be dropped
        "tool.exe".into(),
    ])
    .expect("blocklist should save");
    assert_eq!(list, vec!["badapp.exe".to_string(), "tool.exe".to_string()]);
    assert_eq!(Blocklist::load(), list);
    assert!(Blocklist::file_path().exists());

    let remaining = Blocklist::remove_many(&["tool.exe".into()]).unwrap();
    assert_eq!(remaining, vec!["badapp.exe".to_string()]);
    assert_eq!(Blocklist::load(), remaining);

    std::env::remove_var("TOTHBOOST_DATA_DIR");
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn test_ensure_default_replaces_legacy_profile() {
    let _guard = PROFILE_ENV_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let dir = std::env::temp_dir().join(format!("tothboost-default-test-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(dir.join("Default.json"), "{ \"Name\": \"Default\", \"Services\": [] }").unwrap();
    std::env::set_var("TOTHBOOST_PROFILE_DIR", &dir);

    ProfileService::ensure_default().expect("default should be recreated");
    let loaded = ProfileService::load_profile("Default").expect("new Default must parse");
    assert_eq!(loaded.name, "Default");
    assert!(!loaded.services.is_empty());

    let legacy_dir = dir.join("legacy");
    let backups: Vec<_> = std::fs::read_dir(&legacy_dir)
        .unwrap()
        .flatten()
        .collect();
    assert_eq!(backups.len(), 1, "legacy file must be preserved");

    std::env::remove_var("TOTHBOOST_PROFILE_DIR");
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn test_profile_save_and_load() {
    let _guard = PROFILE_ENV_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let dir = std::env::temp_dir().join(format!("tothboost-profile-test-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::env::set_var("TOTHBOOST_PROFILE_DIR", &dir);
    let name = "__tothboost_unit_test_profile__";
    let dummy = ProfileData {
        name: name.into(),
        created_utc: "2026-10-24 00:00:00 UTC".into(),
        services: vec![("SysMain".into(), 4), ("Spooler".into(), 3)],
        tweaks: vec![("GameDvr".into(), true), ("Telemetry".into(), false)],
        startup: vec![StartupState {
            name: "TestApp".into(),
            kind: Some(StartupKind::Registry),
            location: "HKCU Run".into(),
            enabled: false,
        }],
        tasks: vec![],
        blocked_processes: vec!["badapp.exe".into()],
    };

    assert!(ProfileService::save_profile(&dummy).is_ok());
    let loaded = ProfileService::load_profile(name).expect("profile should load");
    assert_eq!(loaded.name, name);
    assert_eq!(loaded.services.len(), 2);
    assert_eq!(loaded.services_disabled(), 1);
    assert_eq!(loaded.tweaks_applied(), 1);
    assert_eq!(loaded.blocked_processes, vec!["badapp.exe".to_string()]);
    assert!(ProfileService::list_profiles().iter().any(|p| p == name));
    assert!(ProfileService::delete_profile(name).is_ok());

    std::env::remove_var("TOTHBOOST_PROFILE_DIR");
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn test_default_profile_cannot_be_deleted() {
    assert!(ProfileService::delete_profile("Default").is_err());
}

#[test]
fn test_process_path_helper() {
    use crate::power::os_name_from_path;
    assert_eq!(os_name_from_path("C:\\Windows\\System32\\cmd.exe"), "cmd.exe");
    assert_eq!(os_name_from_path(""), "");
}

#[test]
fn test_blocklist_never_accepts_self_or_protected() {
    assert!(Blocklist::normalize(&crate::power::own_process_name()).is_none());
    for name in ["svchost.exe", "lsass.exe", "winlogon.exe", "services.exe"] {
        assert!(Blocklist::normalize(name).is_none(), "{} must stay protected", name);
    }
}

#[test]
fn test_blocklist_enforce_is_noop_when_empty() {
    let _guard = BLOCKLIST_ENV_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let dir = std::env::temp_dir().join(format!("tothboost-blocklist-empty-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::env::set_var("TOTHBOOST_DATA_DIR", &dir);
    Blocklist::save(&[]).unwrap();
    assert_eq!(Blocklist::enforce(), 0);
    std::env::remove_var("TOTHBOOST_DATA_DIR");
    let _ = std::fs::remove_dir_all(&dir);
}

/// End-to-end proof of persistent blocking: a uniquely named copy of a real
/// executable is started, added to the on-disk blocklist, and the enforcement
/// loop must terminate it. Uses a copy so no real system process is harmed.
#[test]
fn test_blocklist_enforcement_terminates_blocked_process() {
    let _guard = BLOCKLIST_ENV_LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let dir = std::env::temp_dir().join(format!("tothboost-blocklist-enforce-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    std::env::set_var("TOTHBOOST_DATA_DIR", &dir);

    let system_root = std::env::var("SystemRoot").unwrap_or_else(|_| "C:\\Windows".into());
    let ping = std::path::Path::new(&system_root).join("System32\\ping.exe");
    let child_name = format!("tothboost_block_test_{}.exe", std::process::id());
    let child_path = dir.join(&child_name);
    std::fs::copy(&ping, &child_path).expect("copy ping.exe into temp dir");

    let mut child = Command::new(&child_path)
        .args(["-n", "120", "127.0.0.1"])
        .creation_flags(0x0800_0000) // CREATE_NO_WINDOW
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .expect("start test child");

    let mut visible = false;
    for _ in 0..80 {
        if ProcessController::enumerate()
            .iter()
            .any(|(_, name)| name.eq_ignore_ascii_case(&child_name))
        {
            visible = true;
            break;
        }
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    assert!(visible, "test child {} never appeared in enumeration", child_name);

    Blocklist::save(&[child_name.clone()]).expect("blocklist should save");
    let killed = Blocklist::enforce();
    assert!(killed >= 1, "enforcement should terminate at least the test child");

    let mut exited = false;
    for _ in 0..80 {
        if child.try_wait().expect("try_wait").is_some() {
            exited = true;
            break;
        }
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    if !exited {
        let _ = child.kill();
    }
    let _ = child.wait();
    assert!(exited, "blocked child is still running after enforcement");

    std::env::remove_var("TOTHBOOST_DATA_DIR");
    let _ = std::fs::remove_dir_all(&dir);
}

#[test]
fn test_service_start_type_is_readable_and_valid() {
    let name = if ServiceManager::get_start_type("RpcSs").is_ok() {
        "RpcSs"
    } else {
        "DcomLaunch"
    };
    let start = ServiceManager::get_start_type(name)
        .unwrap_or_else(|e| panic!("cannot read Start for {}: {}", name, e));
    assert!(start <= 4, "unexpected Start value {} for {}", start, name);

    assert_eq!(ServiceStartKind::Boot.label(), "Boot");
    assert_eq!(ServiceStartKind::System.label(), "System");
    assert_eq!(ServiceStartKind::Automatic.label(), "Automatic");
    assert_eq!(ServiceStartKind::Manual.label(), "Manual");
    assert_eq!(ServiceStartKind::Disabled.label(), "Disabled");
}

#[test]
fn test_service_risk_classification() {
    assert_eq!(classify_service("RpcSs"), (RiskLevel::Protected, true));
    assert_eq!(classify_service("SamSs"), (RiskLevel::Protected, true));
    assert_eq!(classify_service("DiagTrack"), (RiskLevel::Recommended, false));
    assert_eq!(classify_service("SysMain"), (RiskLevel::Recommended, false));
    assert_eq!(classify_service("Spooler"), (RiskLevel::Warning, false));
    assert_eq!(classify_service("SomeVendorService"), (RiskLevel::Optional, false));
}

#[test]
fn test_tweak_catalog_is_complete_and_unique() {
    let tweaks = RegistryTweaks::get_available_tweaks();
    assert_eq!(tweaks.len(), 7);
    let mut ids: Vec<&str> = tweaks.iter().map(|t| t.id.as_str()).collect();
    ids.sort();
    ids.dedup();
    assert_eq!(ids.len(), tweaks.len(), "tweak IDs must be unique");
    for tweak in &tweaks {
        assert!(!tweak.name.trim().is_empty());
        assert!(!tweak.description.trim().is_empty());
        assert!(!tweak.category.trim().is_empty());
    }
}

#[test]
fn test_profile_capture_reflects_current_system() {
    let profile = ProfileService::capture("__capture_test__");
    assert!(!profile.services.is_empty(), "capture must include services");
    assert_eq!(profile.tweaks.len(), 7, "capture must include every tweak");
    assert!(profile.created_utc.ends_with("UTC"));
    assert!(profile.created_utc.len() >= 20);
    assert!(profile.services.iter().all(|(name, start)| !name.is_empty() && *start <= 4));
}

#[test]
fn test_startup_items_have_consistent_fields() {
    for item in SystemScanner::scan_startup() {
        assert!(!item.name.is_empty());
        assert!(!item.location.is_empty());
        match item.kind {
            StartupKind::Registry => {
                assert!(!item.hive.is_empty(), "registry item without hive: {}", item.name);
                assert!(!item.key_path.is_empty(), "registry item without key: {}", item.name);
                assert!(!item.command.is_empty(), "registry item without command: {}", item.name);
            }
            StartupKind::File => {
                assert!(!item.file_path.is_empty(), "file item without path: {}", item.name);
            }
        }
    }
}

#[test]
fn test_scheduled_task_csv_parser() {
    assert_eq!(parse_csv_line("a,b,c"), vec!["a", "b", "c"]);
    assert_eq!(
        parse_csv_line("\"task,name\",\"\\Folder\",\"Ready\""),
        vec!["task,name", "\\Folder", "Ready"]
    );
    assert_eq!(
        parse_csv_line("\"say \"\"hi\"\"\",x"),
        vec!["say \"hi\"", "x"]
    );
}
