use crate::blocklist::Blocklist;
use crate::models::{ActionReport, RiskLevel, StartupKind};
use crate::scanner::SystemScanner;
use crate::service::{ServiceManager, ServiceStartKind};
use crate::startup::StartupManager;
use crate::tweaks::RegistryTweaks;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StartupState {
    pub name: String,
    #[serde(default)]
    pub kind: Option<StartupKind>,
    #[serde(default)]
    pub location: String,
    pub enabled: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProfileData {
    pub name: String,
    pub created_utc: String,
    pub services: Vec<(String, u32)>,
    pub tweaks: Vec<(String, bool)>,
    #[serde(default)]
    pub startup: Vec<StartupState>,
    #[serde(default)]
    pub tasks: Vec<(String, bool)>,
    #[serde(default)]
    pub blocked_processes: Vec<String>,
}

impl ProfileData {
    pub fn services_disabled(&self) -> usize {
        self.services.iter().filter(|(_, start)| *start == 4).count()
    }

    pub fn tweaks_applied(&self) -> usize {
        self.tweaks.iter().filter(|(_, applied)| *applied).count()
    }
}

pub struct ProfileService;

impl ProfileService {
    pub fn get_profiles_dir() -> PathBuf {
        if let Ok(custom) = std::env::var("TOTHBOOST_PROFILE_DIR") {
            let path = PathBuf::from(custom);
            let _ = fs::create_dir_all(&path);
            return path;
        }
        let base = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| {
            std::env::var("ProgramData").unwrap_or_else(|_| "C:\\ProgramData".into())
        });
        let mut path = PathBuf::from(base);
        path.push("TothBoost");
        path.push("Profiles");
        let _ = fs::create_dir_all(&path);
        path
    }

    pub fn list_profiles() -> Vec<String> {
        let dir = Self::get_profiles_dir();
        let mut profiles = Vec::new();
        if let Ok(entries) = fs::read_dir(dir) {
            for entry in entries.flatten() {
                let path = entry.path();
                if path.extension().map_or(false, |ext| ext == "json") {
                    if let Some(stem) = path.file_stem().and_then(|s| s.to_str()) {
                        profiles.push(stem.to_string());
                    }
                }
            }
        }
        profiles.sort_by_key(|p| {
            if p.eq_ignore_ascii_case("Default") {
                (0, String::new())
            } else if p.eq_ignore_ascii_case("Extreme") {
                (1, String::new())
            } else {
                (2, p.to_lowercase())
            }
        });
        profiles
    }

    pub fn save_profile(data: &ProfileData) -> Result<(), String> {
        let dir = Self::get_profiles_dir();
        fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        let path = dir.join(format!("{}.json", sanitize_name(&data.name)));
        let json = serde_json::to_string_pretty(data).map_err(|e| e.to_string())?;
        fs::write(path, json).map_err(|e| e.to_string())
    }

    pub fn load_profile(name: &str) -> Result<ProfileData, String> {
        let path = Self::get_profiles_dir().join(format!("{}.json", sanitize_name(name)));
        let content = fs::read_to_string(path).map_err(|e| e.to_string())?;
        serde_json::from_str(&content).map_err(|e| e.to_string())
    }

    pub fn delete_profile(name: &str) -> Result<(), String> {
        if name.eq_ignore_ascii_case("Default") {
            return Err("The Default baseline profile cannot be deleted.".into());
        }
        let path = Self::get_profiles_dir().join(format!("{}.json", sanitize_name(name)));
        fs::remove_file(path).map_err(|e| format!("Cannot delete '{}': {}", name, e))
    }

    /// Captures the current system state into a profile.
    pub fn capture(name: &str) -> ProfileData {
        let services = SystemScanner::scan_services()
            .into_iter()
            .map(|service| (service.name, service.start_type))
            .collect();

        let tweaks = RegistryTweaks::get_available_tweaks()
            .into_iter()
            .map(|tweak| (tweak.id, tweak.is_applied))
            .collect();

        let startup = SystemScanner::scan_startup()
            .into_iter()
            .map(|item| StartupState {
                name: item.name,
                kind: Some(item.kind),
                location: item.location,
                enabled: item.is_enabled,
            })
            .collect();

        ProfileData {
            name: name.to_string(),
            created_utc: now_label(),
            services,
            tweaks,
            startup,
            tasks: Vec::new(),
            blocked_processes: Blocklist::load(),
        }
    }

    /// Creates the Default baseline on first run so the very first state of the
    /// machine can always be restored. Also ensures the suggested "Extreme"
    /// profile exists on first run (barebones, maximum performance).
    pub fn ensure_default() -> Result<(), String> {
        let path = Self::get_profiles_dir().join("Default.json");
        let mut need_default = true;
        if path.exists() {
            if Self::load_profile("Default").is_ok() {
                need_default = false;
            } else {
                let stamp = std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .map(|d| d.as_secs())
                    .unwrap_or(0);
                let legacy_dir = Self::get_profiles_dir().join("legacy");
                let _ = fs::create_dir_all(&legacy_dir);
                let backup = legacy_dir.join(format!("Default-{}.json", stamp));
                std::fs::rename(&path, &backup)
                    .map_err(|e| format!("Cannot back up legacy Default profile: {}", e))?;
                let _ = std::fs::remove_file(Self::get_profiles_dir().join("profiles.json"));
            }
        }
        if need_default {
            let data = Self::capture("Default");
            Self::save_profile(&data)?;
        }
        let _ = Self::ensure_extreme();
        Ok(())
    }

    /// Generates an extremely aggressive profile:
    /// - 100% of available tweaks applied.
    /// - 100% of non-critical services DISABLED (only protected core OS services remain).
    /// - 100% of non-critical startup apps & scheduled tasks DISABLED.
    /// - Background telemetry/updater processes added to blocklist.
    pub fn create_extreme_profile() -> ProfileData {
        let tweaks = RegistryTweaks::get_available_tweaks()
            .into_iter()
            .map(|tweak| (tweak.id, true))
            .collect();

        let services = SystemScanner::scan_services()
            .into_iter()
            .map(|service| {
                let is_core = service.risk == RiskLevel::Protected || service.is_protected;
                let start_type = if is_core {
                    service.start_type
                } else {
                    4 // Disabled
                };
                (service.name, start_type)
            })
            .collect();

        let startup = SystemScanner::scan_startup()
            .into_iter()
            .map(|item| {
                let keep_enabled = item.classification().is_critical();
                StartupState {
                    name: item.name,
                    kind: Some(item.kind),
                    location: item.location,
                    enabled: keep_enabled,
                }
            })
            .collect();

        let blocked_processes = vec![
            "wermgr.exe".into(),
            "compattelrunner.exe".into(),
            "devicecensus.exe".into(),
            "gamebarpresencewriter.exe".into(),
            "smartscreen.exe".into(),
            "microsoftedgeupdate.exe".into(),
            "googleupdate.exe".into(),
        ];

        let tasks = SystemScanner::scan_scheduled_tasks()
            .into_iter()
            .map(|task| {
                let keep_enabled = is_critical_task_path(&task.full_path);
                (task.full_path, keep_enabled)
            })
            .collect();

        ProfileData {
            name: "Extreme".into(),
            created_utc: now_label(),
            services,
            tweaks,
            startup,
            tasks,
            blocked_processes,
        }
    }

    /// Ensures the suggested Extreme profile exists.
    pub fn ensure_extreme() -> Result<(), String> {
        let path = Self::get_profiles_dir().join("Extreme.json");
        if path.exists() {
            if Self::load_profile("Extreme").is_ok() {
                return Ok(());
            }
        }
        let data = Self::create_extreme_profile();
        Self::save_profile(&data)
    }

    /// Applies every element of a profile.
    pub fn apply(data: &ProfileData) -> ActionReport {
        let mut report = ActionReport::default();

        // Services: never touch services that are classified as protected.
        let current = SystemScanner::scan_services();
        let protected: Vec<String> = current
            .iter()
            .filter(|s| s.risk == RiskLevel::Protected || s.is_protected)
            .map(|s| s.name.clone())
            .collect();

        for (name, start_type) in &data.services {
            if protected.iter().any(|p| p.eq_ignore_ascii_case(name)) {
                continue;
            }
            let kind = match start_type {
                0 => ServiceStartKind::Boot,
                1 => ServiceStartKind::System,
                2 => ServiceStartKind::Automatic,
                3 => ServiceStartKind::Manual,
                _ => ServiceStartKind::Disabled,
            };
            match ServiceManager::set_start_type(name, kind) {
                Ok(()) => report.add(name, "service start type", true, kind.label()),
                Err(e) => report.add(name, "service start type", false, &e),
            }
        }

        // Tweaks
        for (id, applied) in &data.tweaks {
            let result = if *applied {
                RegistryTweaks::apply_tweak(id)
            } else {
                RegistryTweaks::revert_tweak(id)
            };
            match result {
                Ok(message) => report.add(id, "tweak", true, &message),
                Err(e) => report.add(id, "tweak", false, &e),
            }
        }

        // Startup items
        let current_startup = SystemScanner::scan_startup();
        for state in &data.startup {
            let found = current_startup.iter().find(|item| {
                item.name.eq_ignore_ascii_case(&state.name)
                    && state.kind.map(|k| k == item.kind).unwrap_or(true)
            });
            let Some(item) = found else { continue };
            if item.is_enabled == state.enabled {
                continue;
            }
            let result = if state.enabled {
                StartupManager::enable(item)
            } else {
                StartupManager::disable(item)
            };
            match result {
                Ok(()) => report.add(
                    &state.name,
                    if state.enabled { "startup enable" } else { "startup disable" },
                    true,
                    &state.location,
                ),
                Err(e) => report.add(&state.name, "startup", false, &e),
            }
        }

        // Scheduled tasks
        for (path, enabled) in &data.tasks {
            if is_critical_task_path(path) && !*enabled {
                continue;
            }
            match StartupManager::set_task_enabled(path, *enabled) {
                Ok(()) => report.add(
                    path,
                    if *enabled { "task enable" } else { "task disable" },
                    true,
                    "",
                ),
                Err(e) => report.add(path, "task", false, &e),
            }
        }

        // Persistent blocklist
        match Blocklist::save(&data.blocked_processes) {
            Ok(()) => report.add(
                "blocklist",
                "apply",
                true,
                &format!("{} blocked process(es)", data.blocked_processes.len()),
            ),
            Err(e) => report.add("blocklist", "apply", false, &e),
        }

        report
    }
}

/// Determines if a scheduled task path belongs to a critical Windows system function.
pub fn is_critical_task_path(path: &str) -> bool {
    let lower = path.to_ascii_lowercase();
    lower.contains("cachetask")
        || lower.contains("msctfmonitor")
        || lower.contains("autochk\\proxy")
        || lower.contains("spaceport")
        || lower.contains("softwareprotectionplatform")
        || lower.contains("data integrity scan")
        || lower.contains("verifiedpublishercertstorecheck")
        || lower.contains("userinit")
        || lower.contains("winlogon")
}

fn sanitize_name(name: &str) -> String {
    let cleaned: String = name
        .chars()
        .filter(|c| !matches!(c, '\\' | '/' | ':' | '*' | '?' | '"' | '<' | '>' | '|'))
        .collect();
    let trimmed = cleaned.trim();
    if trimmed.is_empty() {
        "Profile".to_string()
    } else {
        trimmed.chars().take(64).collect()
    }
}

/// UTC timestamp such as `2026-10-24 13:05:00 UTC`.
fn now_label() -> String {
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default();
    let secs = now.as_secs() as i64;
    let days = secs.div_euclid(86_400);
    let rem = secs.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02} UTC",
        year,
        month,
        day,
        rem / 3600,
        (rem % 3600) / 60,
        rem % 60
    )
}

/// Howard Hinnant's `civil_from_days` algorithm (no external crate needed).
fn civil_from_days(days: i64) -> (i32, u32, u32) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = if m <= 2 { y + 1 } else { y };
    (year as i32, m as u32, d as u32)
}

#[cfg(test)]
mod tests {
    use super::civil_from_days;

    #[test]
    fn civil_from_days_matches_known_dates() {
        assert_eq!(civil_from_days(0), (1970, 1, 1));
        assert_eq!(civil_from_days(19_000), (2022, 1, 8));
    }
}
