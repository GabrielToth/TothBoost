use crate::power::{is_protected_name, own_process_name, ProcessController};
use std::path::PathBuf;

/// Persistent process blocklist.
///
/// Stored as JSON under `%ProgramData%\TothBoost\blocked_processes.json`
/// (machine-wide, survives reboots and user changes). A lightweight timer in
/// the UI calls [`Blocklist::enforce`] every two seconds; any matching process
/// is terminated immediately and can never stay running while TothBoost is up.
pub struct Blocklist;

impl Blocklist {
    pub fn data_dir() -> PathBuf {
        if let Ok(custom) = std::env::var("TOTHBOOST_DATA_DIR") {
            return PathBuf::from(custom);
        }
        match std::env::var("ProgramData") {
            Ok(program_data) if !program_data.trim().is_empty() => {
                PathBuf::from(program_data).join("TothBoost")
            }
            _ => std::env::temp_dir().join("TothBoost"),
        }
    }

    pub fn file_path() -> PathBuf {
        Self::data_dir().join("blocked_processes.json")
    }

    /// Normalizes a process name to a lowercase base file name.
    /// Returns `None` for empty/invalid entries or protected processes.
    pub fn normalize(name: &str) -> Option<String> {
        let trimmed = name.trim().trim_matches('"');
        if trimmed.is_empty() {
            return None;
        }
        let base = trimmed
            .rsplit(['\\', '/'])
            .next()
            .unwrap_or(trimmed)
            .trim()
            .to_ascii_lowercase();

        if base.is_empty() || base.len() > 260 {
            return None;
        }
        if is_protected_name(&base) || base == own_process_name() {
            return None;
        }
        Some(base)
    }

    pub fn load() -> Vec<String> {
        let path = Self::file_path();
        let Ok(content) = std::fs::read_to_string(path) else {
            return Vec::new();
        };
        let Ok(raw) = serde_json::from_str::<Vec<String>>(&content) else {
            return Vec::new();
        };
        Self::normalize_list(&raw)
    }

    pub fn save(list: &[String]) -> Result<(), String> {
        let dir = Self::data_dir();
        std::fs::create_dir_all(&dir)
            .map_err(|e| format!("Cannot create {}: {}", dir.display(), e))?;

        let mut normalized = Self::normalize_list(list);
        normalized.sort();
        normalized.dedup();

        let json = serde_json::to_string_pretty(&normalized)
            .map_err(|e| format!("Cannot serialize blocklist: {}", e))?;

        // Write to a temp file first so a crash never corrupts the blocklist.
        let path = Self::file_path();
        let tmp = path.with_extension("json.tmp");
        std::fs::write(&tmp, json)
            .map_err(|e| format!("Cannot write {}: {}", tmp.display(), e))?;
        std::fs::rename(&tmp, &path)
            .map_err(|e| format!("Cannot replace {}: {}", path.display(), e))?;
        Ok(())
    }

    pub fn add_many(names: &[String]) -> Result<Vec<String>, String> {
        let mut list = Self::load();
        for name in names {
            if let Some(normalized) = Self::normalize(name) {
                list.push(normalized);
            }
        }
        list.sort();
        list.dedup();
        Self::save(&list)?;
        Ok(list)
    }

    pub fn remove_many(names: &[String]) -> Result<Vec<String>, String> {
        let remove: Vec<String> = names
            .iter()
            .filter_map(|n| Self::normalize(n))
            .collect();
        let mut list = Self::load();
        list.retain(|entry| !remove.contains(entry));
        Self::save(&list)?;
        Ok(list)
    }

    pub fn contains(list: &[String], name: &str) -> bool {
        let lower = name.trim().to_ascii_lowercase();
        list.iter().any(|entry| entry == &lower)
    }

    /// Kills every running process that matches an entry in the persistent
    /// blocklist. Returns the number of processes terminated.
    pub fn enforce() -> usize {
        let blocked = Self::load();
        if blocked.is_empty() {
            return 0;
        }
        let own = own_process_name();
        let mut killed = 0usize;

        for (pid, name) in ProcessController::enumerate() {
            let lower = name.to_ascii_lowercase();
            if lower == own || is_protected_name(&lower) {
                continue;
            }
            if blocked.iter().any(|entry| entry == &lower)
                && ProcessController::terminate_process(pid).is_ok()
            {
                killed += 1;
            }
        }

        killed
    }

    fn normalize_list(list: &[String]) -> Vec<String> {
        let mut out: Vec<String> = list
            .iter()
            .filter_map(|name| Self::normalize(name))
            .collect();
        out.sort();
        out.dedup();
        out
    }
}
