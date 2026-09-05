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
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProcessItem {
    pub pid: u32,
    pub name: String,
    pub memory_mb: u64,
    pub cpu_percent: f32,
    pub is_protected: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceItem {
    pub name: String,
    pub display_name: String,
    pub description: String,
    pub start_type: u32, // 2 = Auto, 3 = Manual, 4 = Disabled
    pub status: String,   // Running / Stopped
    pub risk: RiskLevel,
    pub is_protected: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StartupItem {
    pub name: String,
    pub location: String,
    pub location_key: String,
    pub command: String,
    pub is_enabled: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScheduledTaskItem {
    pub full_path: String,
    pub name: String,
    pub author: String,
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
}
