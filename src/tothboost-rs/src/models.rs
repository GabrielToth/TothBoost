use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
pub enum RiskLevel {
    Protected,
    Warning,
    Recommended,
    Optional,
    Info,
}

impl RiskLevel {
    pub fn label(&self) -> &'static str {
        match self {
            Self::Protected => "CRITICAL — Protected",
            Self::Warning => "Caution",
            Self::Recommended => "Recommended for Gaming",
            Self::Optional => "Optional",
            Self::Info => "Unclassified",
        }
    }

    #[allow(dead_code)]
    pub fn short(&self) -> &'static str {
        match self {
            Self::Protected => "PROTECTED",
            Self::Warning => "CAUTION",
            Self::Recommended => "GAMING",
            Self::Optional => "OPTIONAL",
            Self::Info => "INFO",
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProcessItem {
    pub pid: u32,
    pub name: String,
    pub path: String,
    pub memory_bytes: u64,
    pub is_protected: bool,
    pub is_blocked: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceItem {
    pub name: String,
    pub display_name: String,
    pub description: String,
    pub start_type: u32, // 0=Boot 1=System 2=Auto 3=Manual 4=Disabled
    pub status: String,  // Running / Stopped / Paused ...
    pub risk: RiskLevel,
    pub is_protected: bool,
    pub process_id: u32,
}

impl ServiceItem {
    pub fn start_label(&self) -> &'static str {
        match self.start_type {
            0 => "Boot",
            1 => "System",
            2 => "Automatic",
            3 => "Manual",
            4 => "Disabled",
            _ => "Unknown",
        }
    }

    #[allow(dead_code)]
    pub fn is_running(&self) -> bool {
        self.status == "Running"
    }
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
pub enum StartupKind {
    Registry,
    File,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StartupItem {
    pub name: String,
    pub location: String,
    pub kind: StartupKind,
    /// "HKLM" or "HKCU" for registry entries.
    pub hive: String,
    /// Registry subkey (without hive) for registry entries.
    pub key_path: String,
    pub command: String,
    /// Absolute file path for Startup-folder entries.
    pub file_path: String,
    pub is_enabled: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TweakItem {
    pub id: String,
    pub name: String,
    pub description: String,
    pub category: String,
    pub is_applied: bool,
}

#[allow(dead_code)]
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScheduledTaskItem {
    pub full_path: String,
    pub name: String,
    pub author: String,
    pub is_enabled: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct ActionReport {
    pub total_actions: usize,
    pub success_count: usize,
    pub error_count: usize,
    pub messages: Vec<String>,
}

impl ActionReport {
    pub fn add(&mut self, target: &str, action: &str, success: bool, details: &str) {
        self.total_actions += 1;
        if success {
            self.success_count += 1;
        } else {
            self.error_count += 1;
        }
        self.messages.push(format!(
            "[{}] {} - {}: {}",
            if success { "OK" } else { "FAIL" },
            target,
            action,
            details
        ));
    }

    pub fn summary(&self) -> String {
        format!(
            "{} action(s): {} succeeded, {} failed",
            self.total_actions, self.success_count, self.error_count
        )
    }

    pub fn failures(&self) -> Vec<&str> {
        self.messages
            .iter()
            .filter(|m| m.starts_with("[FAIL]"))
            .map(|m| m.as_str())
            .collect()
    }
}
