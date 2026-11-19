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
    /// Logon or boot scheduled task. Disable uses `schtasks /Change /Disable`.
    ScheduledTask,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
pub enum StartupClassification {
    /// Critical Windows OS components required for stable boot, logon, or core services.
    /// Disabling causes system instability or boot failure.
    SystemCritical,
    /// System component that is safe and recommended to disable for gaming/performance
    /// (e.g. telemetry, crash reporting, background CEIP, security tray icon).
    SystemSuggested,
    /// System component that is optional and safe to disable (e.g. language sync, diagnostics).
    SystemSafe,
    /// Hardware/OEM driver utility (e.g. Realtek, NVIDIA, AMD, Intel audio/GPU utilities).
    DriverCaution,
    /// Third-party background apps recommended to disable for faster boot (launchers, updaters).
    UserSuggested,
    /// Standard user-installed application (safe to disable if desired).
    UserApp,
}

impl StartupClassification {
    pub fn label(&self) -> &'static str {
        match self {
            Self::SystemCritical => "System (Critical)",
            Self::SystemSuggested => "System (Suggested)",
            Self::SystemSafe => "System (Safe)",
            Self::DriverCaution => "Driver (Caution)",
            Self::UserSuggested => "User (Suggested)",
            Self::UserApp => "User App",
        }
    }

    pub fn is_suggested(&self) -> bool {
        matches!(self, Self::SystemSuggested | Self::UserSuggested)
    }

    #[allow(dead_code)]
    pub fn is_critical(&self) -> bool {
        matches!(self, Self::SystemCritical)
    }
}

pub fn classify_startup(item: &StartupItem) -> StartupClassification {
    let name_lower = item.name.to_ascii_lowercase();
    let cmd_lower = item.command.to_ascii_lowercase();
    let key_lower = item.key_path.to_ascii_lowercase();

    // 1. Critical Windows System Components
    // Tasks or Run entries that can break boot, logon, Text Services, or drive storage.
    if name_lower.contains("cachetask")
        || key_lower.contains("wininet\\cachetask")
        || name_lower.contains("msctfmonitor")
        || key_lower.contains("textservicesframework\\msctfmonitor")
        || key_lower.contains("autochk\\proxy")
        || key_lower.contains("spaceport")
        || key_lower.contains("softwareprotectionplatform")
        || key_lower.contains("data integrity scan")
        || name_lower.contains("verifiedpublishercertstorecheck")
        || cmd_lower.contains("userinit.exe")
        || cmd_lower.contains("winlogon.exe")
        || cmd_lower.contains("smss.exe")
        || cmd_lower.contains("csrss.exe")
        || cmd_lower.contains("lsass.exe")
    {
        return StartupClassification::SystemCritical;
    }

    // 2. System Suggested (Safe and recommended to disable for gaming/performance)
    // Non-essential system background tasks: telemetry, error reporting, tray icons, provisioning.
    if name_lower.contains("securityhealth")
        || cmd_lower.contains("securityhealthsystray")
        || name_lower.contains("queuereporting")
        || key_lower.contains("windows error reporting")
        || cmd_lower.contains("wermgr.exe")
        || name_lower.contains("device user")
        || cmd_lower.contains("devicecensus.exe")
        || name_lower.contains("clipesuconsumer")
        || cmd_lower.contains("clipesuconsumer.exe")
        || key_lower.contains("exploitguard")
        || key_lower.contains("autopilot")
        || (key_lower.starts_with('\\') && key_lower.contains("provisioning"))
        || cmd_lower.contains("provtool.exe")
        || (key_lower.starts_with('\\') && key_lower.contains("workplace join"))
        || cmd_lower.contains("dsregcmd.exe")
        || key_lower.contains("appxdeploymentclient")
        || key_lower.contains("active directory rights management")
        || name_lower.contains("ceip")
        || key_lower.contains("customer experience improvement program")
        || name_lower.contains("onedrive")
        || cmd_lower.contains("onedrive.exe")
        || key_lower.contains("onedrive")
        || name_lower.contains("cortana")
        || name_lower.contains("edgeupdate")
        || cmd_lower.contains("microsoftedgeupdate")
    {
        return StartupClassification::SystemSuggested;
    }

    // 3. Hardware / Driver (Caution)
    if name_lower.contains("realtek")
        || cmd_lower.contains("rtkauduservice")
        || cmd_lower.contains("ravcpl")
        || name_lower.contains("nvidia")
        || cmd_lower.contains("nvbackend")
        || cmd_lower.contains("nvcontainer")
        || cmd_lower.contains("shadowplay")
        || name_lower.contains("amd")
        || name_lower.contains("radeon")
        || cmd_lower.contains("amdrsserv")
        || name_lower.contains("intel")
        || cmd_lower.contains("igfx")
        || name_lower.contains("synaptics")
        || name_lower.contains("logitech")
        || cmd_lower.contains("lghub")
        || name_lower.contains("razer")
        || cmd_lower.contains("synapse")
        || name_lower.contains("corsair")
        || cmd_lower.contains("icue")
        || name_lower.contains("asus")
        || cmd_lower.contains("armoury")
        || name_lower.contains("alienware")
    {
        return StartupClassification::DriverCaution;
    }

    // 4. User Suggested (Heavy launchers, downloaders, chat apps that slow down boot)
    if name_lower.contains("steam")
        || cmd_lower.contains("steam.exe")
        || name_lower.contains("epic")
        || cmd_lower.contains("epicgames")
        || name_lower.contains("discord")
        || cmd_lower.contains("discord.exe")
        || name_lower.contains("spotify")
        || cmd_lower.contains("spotify.exe")
        || name_lower.contains("skype")
        || name_lower.contains("telegram")
        || name_lower.contains("whatsapp")
        || name_lower.contains("slack")
        || name_lower.contains("teams")
        || name_lower.contains("torrent")
        || cmd_lower.contains("torrent")
        || name_lower.contains("googleupdate")
        || cmd_lower.contains("googleupdate")
        || name_lower.contains("adobe")
        || cmd_lower.contains("acrotray")
        || name_lower.contains("ccleaner")
        || name_lower.contains("pm2")
        || cmd_lower.contains("pm2")
        || name_lower.contains("battle.net")
        || name_lower.contains("riot")
        || name_lower.contains("origin")
    {
        return StartupClassification::UserSuggested;
    }

    // 5. Other System tasks under \Microsoft\Windows (only for scheduled tasks starting with \)
    if (key_lower.starts_with("\\microsoft\\windows") || (item.kind == StartupKind::ScheduledTask && key_lower.contains("microsoft\\windows")))
        || cmd_lower.contains("system32\\rundll32.exe")
        || name_lower.contains("calibration loader")
        || name_lower.contains("synchronize language")
        || name_lower.contains("languagecomponents")
        || name_lower.contains("diskdiagnostic")
        || name_lower.contains("usertask")
        || name_lower.contains("work folders")
        || name_lower.contains("offline files")
        || name_lower.contains("systemsoundsservice")
    {
        return StartupClassification::SystemSafe;
    }

    // 6. General user app
    StartupClassification::UserApp
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

impl StartupItem {
    pub fn classification(&self) -> StartupClassification {
        classify_startup(self)
    }

    pub fn is_suggested(&self) -> bool {
        self.classification().is_suggested()
    }
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
