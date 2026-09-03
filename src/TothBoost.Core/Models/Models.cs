namespace TothBoost.Core.Models
{
    public enum ServiceStartKind
    {
        Boot = 0,
        System = 1,
        Automatic = 2,
        Manual = 3,
        Disabled = 4,
        Unknown = 5
    }

    public enum RiskLevel
    {
        /// <summary>Do not touch: disabling can crash Windows or cause BSOD.</summary>
        Protected = 0,
        /// <summary>Can affect system or security features. Disable only with caution.</summary>
        Warning = 1,
        /// <summary>Safe to disable for gaming performance.</summary>
        Recommended = 2,
        /// <summary>Safe to disable if feature is not used (printers, Bluetooth, etc.).</summary>
        Optional = 3,
        /// <summary>Other. No significant impact.</summary>
        Info = 4
    }

    public static class RiskText
    {
        public static string Label(RiskLevel r) => r switch
        {
            RiskLevel.Protected => "CRITICAL — Protected",
            RiskLevel.Warning => "Caution",
            RiskLevel.Recommended => "Recommended for Gaming",
            RiskLevel.Optional => "Optional",
            _ => "Unclassified"
        };
    }

    public class ServiceInfo
    {
        public required string Name { get; init; }
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public string? ImagePath { get; init; }
        public ServiceTypeKind TypeKind { get; init; }
        public ServiceStartKind StartKind { get; init; }
        public ServiceStartKind? ConfiguredStart { get; set; }
        public bool IsDriver => TypeKind != ServiceTypeKind.OwnProcess && TypeKind != ServiceTypeKind.ShareProcess;
        public string? RunningState { get; init; }   // "Running", "Stopped", "" (drivers)
        public RiskLevel Risk { get; set; }
        public string RiskReason { get; set; } = "";
        public string Category { get; set; } = "";
        public string Group { get; init; } = "";
        public bool IsDriverProtectedType => IsDriver;

        public bool IsProtected => Risk == RiskLevel.Protected || IsDriver;

        public string StartKindLabel => StartKind switch
        {
            ServiceStartKind.Boot => "Boot Start",
            ServiceStartKind.System => "System Start",
            ServiceStartKind.Automatic => "Automatic",
            ServiceStartKind.Manual => "Manual",
            ServiceStartKind.Disabled => "Disabled",
            _ => "Unknown"
        };
    }

    public enum ServiceTypeKind { KernelDriver = 1, FileSystemDriver = 2, OwnProcess = 16, ShareProcess = 32, Unknown = -1 }

    public class ProcessInfo
    {
        public int Pid { get; init; }
        public required string Name { get; init; }
        public long WorkingSetMb { get; init; }
        public string? Path { get; init; }
        public string? Owner { get; init; }
        public string SessionName { get; init; } = "";
        public bool Protected { get; set; }
        public string? ProtectReason { get; set; }
        public bool IsService { get; init; }
        /// <summary>CPU percentage (0-100%). &lt;0 = unavailable.</summary>
        public double CpuPercent { get; set; } = -1;
        /// <summary>GPU utilization percentage. &lt;0 = unavailable.</summary>
        public double GpuPercent { get; set; } = -1;
        /// <summary>Disk read rate in KB/s. &lt;0 = unavailable.</summary>
        public double IoReadKbs { get; set; } = -1;
        /// <summary>Disk write rate in KB/s. &lt;0 = unavailable.</summary>
        public double IoWriteKbs { get; set; } = -1;
    }

    public class ScheduledTaskInfo
    {
        public required string FullPath { get; init; }      // \Microsoft\Windows\...\Name
        public required string Name { get; init; }
        public bool Enabled { get; init; }
        public string? Author { get; init; }
        public string? LogonDescription { get; init; }
    }

    public class StartupItem
    {
        public required string Name { get; init; }
        public required string Location { get; init; }      // Readable (e.g. HKCU\...\Run / Startup Folder)
        public string? LocationKey { get; init; }           // Registry key, if applicable
        public string? Command { get; init; }               // Command (registry) or path (file)
        public bool IsFolder { get; init; }
        public bool Existing { get; set; } = true;
        public bool DisabledByTothBoost { get; set; }
    }

    public class TweakInfo
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public string Category { get; init; } = "General";
        public bool Applies { get; set; }
        public bool IsApplied { get; set; }
        public string? ValueText { get; set; }
    }

    public class SnapshotItem
    {
        public required string Name { get; init; }
        public required string Category { get; init; }
        public string OriginalState { get; init; } = "";
        public string CurrentState { get; init; } = "";
        public bool CanRestore { get; init; } = true;
        public DateTime CreatedUtc { get; init; }
        public string? HostName { get; init; }
        public bool IsBaseline { get; init; }
        public long FileSize { get; init; }
    }

    public enum TweakKind
    {
        GameDvr,
        VisualEffects,
        Telemetry,
        Hibernate,
        UltimatePower,
        HighPerformance,
        BlackWallpaper,
        Transparency,
        Animations,
        Thumbnails,
        NetworkThrottling
    }
}
