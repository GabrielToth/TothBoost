use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProfileData {
    pub name: String,
    pub created_utc: String,
    pub services: Vec<(String, u32)>,
    pub tweaks: Vec<(String, bool)>,
    pub startup: Vec<(String, bool)>,
    pub tasks: Vec<(String, bool)>,
}

pub struct ProfileService;

impl ProfileService {
    pub fn get_profiles_dir() -> PathBuf {
        let local_appdata = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| "C:\\ProgramData".into());
        let mut path = PathBuf::from(local_appdata);
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
                let p = entry.path();
                if p.extension().map_or(false, |ext| ext == "json") {
                    if let Some(stem) = p.file_stem().and_then(|s| s.to_str()) {
                        profiles.push(stem.to_string());
                    }
                }
            }
        }
        if profiles.is_empty() {
            profiles.push("Default".into());
        }
        profiles
    }

    pub fn save_profile(data: &ProfileData) -> Result<(), String> {
        let mut path = Self::get_profiles_dir();
        path.push(format!("{}.json", data.name));
        let json = serde_json::to_string_pretty(data).map_err(|e| e.to_string())?;
        fs::write(path, json).map_err(|e| e.to_string())
    }

    pub fn load_profile(name: &str) -> Result<ProfileData, String> {
        let mut path = Self::get_profiles_dir();
        path.push(format!("{}.json", name));
        let content = fs::read_to_string(path).map_err(|e| e.to_string())?;
        serde_json::from_str(&content).map_err(|e| e.to_string())
    }
}
