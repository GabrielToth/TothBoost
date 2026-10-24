use crate::models::{StartupItem, StartupKind};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use windows::core::{PCWSTR, PWSTR};
use windows::Win32::System::Registry::*;

/// Reversible startup manager.
///
/// Disabling never deletes anything permanently: registry values are copied to
/// a JSON manifest and removed from the Run key, while Startup-folder files are
/// moved to `%ProgramData%\TothBoost\Restoration\files`. Restoring puts
/// everything back exactly where it was.
pub struct StartupManager;

#[derive(Debug, Clone, Serialize, Deserialize)]
struct QuarantineEntry {
    kind: String, // "registry" | "file"
    name: String,
    location: String,
    hive: String,
    key_path: String,
    command: String,
    file_path: String,
    quarantined_path: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
struct QuarantineManifest {
    entries: Vec<QuarantineEntry>,
}

const RUN_KEYS: &[(&str, &str, &str)] = &[
    (
        "HKLM",
        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run",
        "HKLM Run",
    ),
    (
        "HKCU",
        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run",
        "HKCU Run",
    ),
    (
        "HKLM",
        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
        "HKLM RunOnce",
    ),
    (
        "HKCU",
        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
        "HKCU RunOnce",
    ),
    (
        "HKLM",
        "SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Run",
        "HKLM Run (32-bit)",
    ),
];

impl StartupManager {
    fn root() -> PathBuf {
        crate::blocklist::Blocklist::data_dir().join("Restoration")
    }

    fn manifest_path() -> PathBuf {
        Self::root().join("startup_quarantine.json")
    }

    fn load_manifest() -> QuarantineManifest {
        let Ok(content) = std::fs::read_to_string(Self::manifest_path()) else {
            return QuarantineManifest::default();
        };
        serde_json::from_str(&content).unwrap_or_default()
    }

    fn save_manifest(manifest: &QuarantineManifest) -> Result<(), String> {
        let root = Self::root();
        std::fs::create_dir_all(&root)
            .map_err(|e| format!("Cannot create {}: {}", root.display(), e))?;
        let json = serde_json::to_string_pretty(manifest)
            .map_err(|e| format!("Cannot serialize quarantine manifest: {}", e))?;
        std::fs::write(Self::manifest_path(), json)
            .map_err(|e| format!("Cannot write quarantine manifest: {}", e))
    }

    /// Full scan: enabled entries from the registry and Startup folders plus
    /// every entry currently held in quarantine (reported as disabled).
    pub fn scan_all() -> Vec<StartupItem> {
        let mut items: Vec<StartupItem> = Vec::new();

        for (hive, key_path, label) in RUN_KEYS {
            for (name, command) in Self::read_values(hive, key_path) {
                items.push(StartupItem {
                    name,
                    location: (*label).to_string(),
                    kind: StartupKind::Registry,
                    hive: (*hive).to_string(),
                    key_path: (*key_path).to_string(),
                    command,
                    file_path: String::new(),
                    is_enabled: true,
                });
            }
        }

        for folder in Self::startup_folders() {
            let label = if folder.to_string_lossy().contains("ProgramData") {
                "Common Startup Folder"
            } else {
                "User Startup Folder"
            };
            if let Ok(entries) = std::fs::read_dir(&folder) {
                for entry in entries.flatten() {
                    let path = entry.path();
                    if !path.is_file() {
                        continue;
                    }
                    let name = path
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or_default();
                    if name.eq_ignore_ascii_case("desktop.ini") {
                        continue;
                    }
                    items.push(StartupItem {
                        name,
                        location: label.to_string(),
                        kind: StartupKind::File,
                        hive: String::new(),
                        key_path: String::new(),
                        command: path.to_string_lossy().to_string(),
                        file_path: path.to_string_lossy().to_string(),
                        is_enabled: true,
                    });
                }
            }
        }

        // Add quarantined entries that are no longer present in the live lists.
        for entry in Self::load_manifest().entries {
            let already = items.iter().any(|i| {
                i.name.eq_ignore_ascii_case(&entry.name)
                    && i.kind
                        == if entry.kind == "registry" {
                            StartupKind::Registry
                        } else {
                            StartupKind::File
                        }
            });
            if already {
                continue;
            }
            items.push(StartupItem {
                name: entry.name,
                location: format!("{} (quarantined)", entry.location),
                kind: if entry.kind == "registry" {
                    StartupKind::Registry
                } else {
                    StartupKind::File
                },
                hive: entry.hive,
                key_path: entry.key_path,
                command: entry.command,
                file_path: entry.file_path,
                is_enabled: false,
            });
        }

        items.sort_by(|a, b| {
            a.location
                .cmp(&b.location)
                .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
        });
        items
    }

    /// Moves an enabled startup entry into quarantine.
    pub fn disable(item: &StartupItem) -> Result<(), String> {
        if !item.is_enabled {
            return Ok(());
        }

        let mut manifest = Self::load_manifest();
        manifest.entries.retain(|e| {
            !(e.kind == "registry" && item.kind == StartupKind::Registry
                && e.hive == item.hive
                && e.key_path == item.key_path
                && e.name.eq_ignore_ascii_case(&item.name))
        });

        match item.kind {
            StartupKind::Registry => {
                let command = Self::read_single_value(&item.hive, &item.key_path, &item.name)
                    .unwrap_or_else(|| item.command.clone());
                Self::delete_value(&item.hive, &item.key_path, &item.name)?;
                manifest.entries.push(QuarantineEntry {
                    kind: "registry".into(),
                    name: item.name.clone(),
                    location: item.location.clone(),
                    hive: item.hive.clone(),
                    key_path: item.key_path.clone(),
                    command,
                    file_path: String::new(),
                    quarantined_path: String::new(),
                });
            }
            StartupKind::File => {
                let source = PathBuf::from(&item.file_path);
                if !source.exists() {
                    return Err(format!("File not found: {}", item.file_path));
                }
                let files_dir = Self::root().join("files");
                std::fs::create_dir_all(&files_dir)
                    .map_err(|e| format!("Cannot create quarantine folder: {}", e))?;
                let stamp = std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .map(|d| d.as_secs())
                    .unwrap_or(0);
                let file_name = source
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_else(|| "startup-item".into());
                let target = files_dir.join(format!("{}_{}", stamp, file_name));
                std::fs::rename(&source, &target)
                    .or_else(|_| {
                        std::fs::copy(&source, &target)
                            .and_then(|_| std::fs::remove_file(&source))
                    })
                    .map_err(|e| format!("Cannot quarantine {}: {}", source.display(), e))?;

                manifest.entries.push(QuarantineEntry {
                    kind: "file".into(),
                    name: item.name.clone(),
                    location: item.location.clone(),
                    hive: String::new(),
                    key_path: String::new(),
                    command: item.command.clone(),
                    file_path: item.file_path.clone(),
                    quarantined_path: target.to_string_lossy().to_string(),
                });
            }
        }

        Self::save_manifest(&manifest)
    }

    /// Restores a quarantined entry to its original location.
    pub fn enable(item: &StartupItem) -> Result<(), String> {
        let mut manifest = Self::load_manifest();
        let position = manifest.entries.iter().position(|e| {
            e.name.eq_ignore_ascii_case(&item.name)
                && ((e.kind == "registry" && item.kind == StartupKind::Registry)
                    || (e.kind == "file" && item.kind == StartupKind::File))
        });

        let Some(index) = position else {
            return Err("Entry is not in quarantine".into());
        };
        let entry = manifest.entries[index].clone();

        match entry.kind.as_str() {
            "registry" => {
                Self::write_value(&entry.hive, &entry.key_path, &entry.name, &entry.command)?;
            }
            "file" => {
                let source = PathBuf::from(&entry.quarantined_path);
                let target = PathBuf::from(&entry.file_path);
                if let Some(parent) = target.parent() {
                    let _ = std::fs::create_dir_all(parent);
                }
                std::fs::rename(&source, &target)
                    .or_else(|_| {
                        std::fs::copy(&source, &target)
                            .and_then(|_| std::fs::remove_file(&source))
                    })
                    .map_err(|e| format!("Cannot restore {}: {}", source.display(), e))?;
            }
            _ => return Err("Unknown quarantine entry".into()),
        }

        manifest.entries.remove(index);
        Self::save_manifest(&manifest)
    }

    /// Quarantines every startup entry whose command references `image_name`
    /// (e.g. "someapp.exe"). Used by the "Kill + Block" action so a blocked
    /// process cannot resurrect itself at the next logon.
    pub fn disable_matching(image_name: &str) -> usize {
        let needle = image_name.trim().to_ascii_lowercase();
        if needle.is_empty() {
            return 0;
        }
        let mut count = 0usize;
        for item in Self::scan_all() {
            if item.is_enabled && item.command.to_ascii_lowercase().contains(&needle) {
                if Self::disable(&item).is_ok() {
                    count += 1;
                }
            }
        }
        count
    }

    fn startup_folders() -> Vec<PathBuf> {
        let mut folders = Vec::new();
        if let Ok(appdata) = std::env::var("APPDATA") {
            folders.push(
                Path::new(&appdata)
                    .join("Microsoft\\Windows\\Start Menu\\Programs\\Startup"),
            );
        }
        if let Ok(program_data) = std::env::var("ProgramData") {
            folders.push(
                Path::new(&program_data)
                    .join("Microsoft\\Windows\\Start Menu\\Programs\\Startup"),
            );
        }
        folders
    }

    fn hive_handle(hive: &str) -> HKEY {
        if hive.eq_ignore_ascii_case("HKLM") {
            HKEY_LOCAL_MACHINE
        } else {
            HKEY_CURRENT_USER
        }
    }

    fn wide(value: &str) -> Vec<u16> {
        value.encode_utf16().chain(std::iter::once(0)).collect()
    }

    fn read_values(hive: &str, key_path: &str) -> Vec<(String, String)> {
        let mut result = Vec::new();
        unsafe {
            let mut key = HKEY::default();
            let path = Self::wide(key_path);
            if RegOpenKeyExW(Self::hive_handle(hive), PCWSTR(path.as_ptr()), 0, KEY_READ, &mut key)
                .is_err()
            {
                return result;
            }

            let mut index = 0u32;
            loop {
                let mut name_buf = [0u16; 512];
                let mut name_len = name_buf.len() as u32;
                let mut type_val = 0u32;
                let mut data_buf = [0u8; 4096];
                let mut data_len = data_buf.len() as u32;

                let ok = RegEnumValueW(
                    key,
                    index,
                    PWSTR(name_buf.as_mut_ptr()),
                    &mut name_len,
                    None,
                    Some(&mut type_val),
                    Some(data_buf.as_mut_ptr()),
                    Some(&mut data_len),
                );
                if ok.is_err() {
                    break;
                }

                let name = String::from_utf16_lossy(&name_buf[..name_len as usize]);
                let command = if type_val == REG_SZ.0 || type_val == REG_EXPAND_SZ.0 {
                    let slice = std::slice::from_raw_parts(
                        data_buf.as_ptr() as *const u16,
                        (data_len / 2) as usize,
                    );
                    let clean_len = slice.iter().position(|&c| c == 0).unwrap_or(slice.len());
                    String::from_utf16_lossy(&slice[..clean_len])
                } else {
                    String::new()
                };

                if !name.is_empty() && !command.is_empty() {
                    result.push((name, command));
                }
                index += 1;
            }
            let _ = RegCloseKey(key);
        }
        result
    }

    fn read_single_value(hive: &str, key_path: &str, name: &str) -> Option<String> {
        Self::read_values(hive, key_path)
            .into_iter()
            .find(|(n, _)| n.eq_ignore_ascii_case(name))
            .map(|(_, command)| command)
    }

    fn delete_value(hive: &str, key_path: &str, name: &str) -> Result<(), String> {
        unsafe {
            let mut key = HKEY::default();
            let path = Self::wide(key_path);
            let res = RegOpenKeyExW(
                Self::hive_handle(hive),
                PCWSTR(path.as_ptr()),
                0,
                KEY_SET_VALUE,
                &mut key,
            );
            if res.is_err() {
                return Err(format!(
                    "Cannot open {} (administrator required)",
                    key_path
                ));
            }
            let value_name = Self::wide(name);
            let del = RegDeleteValueW(key, PCWSTR(value_name.as_ptr()));
            let _ = RegCloseKey(key);
            del.ok()
                .map_err(|e| format!("Cannot remove '{}': {}", name, e))
        }
    }

    fn write_value(hive: &str, key_path: &str, name: &str, value: &str) -> Result<(), String> {
        unsafe {
            let mut key = HKEY::default();
            let path = Self::wide(key_path);
            let mut disposition = REG_CREATE_KEY_DISPOSITION(0);
            let res = RegCreateKeyExW(
                Self::hive_handle(hive),
                PCWSTR(path.as_ptr()),
                0,
                PCWSTR::null(),
                REG_OPTION_NON_VOLATILE,
                KEY_SET_VALUE,
                None,
                &mut key,
                Some(&mut disposition),
            );
            if res.is_err() {
                return Err(format!("Cannot create {}", key_path));
            }

            let value_name = Self::wide(name);
            let mut data: Vec<u16> = value.encode_utf16().collect();
            data.push(0);
            let bytes = std::slice::from_raw_parts(
                data.as_ptr() as *const u8,
                data.len() * std::mem::size_of::<u16>(),
            );
            let set = RegSetValueExW(key, PCWSTR(value_name.as_ptr()), 0, REG_SZ, Some(bytes));
            let _ = RegCloseKey(key);
            set.ok()
                .map_err(|e| format!("Cannot restore '{}': {}", name, e))
        }
    }
}
