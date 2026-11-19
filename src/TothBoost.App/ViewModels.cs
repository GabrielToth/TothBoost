using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TothBoost.Core.Models;
using TothBoost.Core.Snapshot;

namespace TothBoost.App
{
    /// <summary>Observable row base class for data grids with checkbox selection.</summary>
    public abstract class RowBase : INotifyPropertyChanged
    {
        private bool _checked;
        public bool IsChecked
        {
            get => _checked;
            set { _checked = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class SvcRow : RowBase
    {
        public ServiceInfo S { get; }
        public SvcRow(ServiceInfo s) => S = s;
        public string Name => S.Name;
        public string DisplayName => S.DisplayName;
        public string StartKindLabel => S.StartKindLabel;
        public string? RunningState => S.RunningState;
        public string RiskLabel => RiskText.Label(S.Risk);
        public string Category => S.Category;
        public string Description => string.IsNullOrEmpty(S.Description) ? S.RiskReason : S.Description;
        public string RiskReason => S.RiskReason;
        public string ImagePath => S.ImagePath ?? "";
        public bool IsProtected => S.IsProtected;
        public bool CanTouch => !S.IsProtected;
        public bool IsDriver => S.IsDriver;
        /// <summary>On = enabled (automatic startup); Off = disabled.</summary>
        public bool IsOn => S.StartKind != ServiceStartKind.Disabled;
        public string ToggleTip => CanTouch ? (IsOn ? "Disable this service" : "Enable this service") : "Protected by system";
    }

    public class ProcRow : RowBase
    {
        public ProcessInfo P { get; private set; }
        public ProcRow(ProcessInfo p) => P = p;
        public int Pid => P.Pid;
        public string Name => P.Name;
        public double MemMb => P.WorkingSetMb;
        public string MemText => P.WorkingSetMb.ToString("0.0");
        public string Session => P.SessionName;
        public string Path => P.Path ?? "—";
        public bool IsProtected => P.Protected;
        public string ProtectReason => P.Protected ? "Critical Windows process — cannot be terminated." : "";
        public string ProtectionLabel => P.Protected ? "PROTECTED" : "Allowed";
        public Brush ProtectionColor => P.Protected ? Green : Dim;

        // Numeric sorting properties: the display columns are formatted strings,
        // so the DataGrid must sort on the underlying values instead.
        public double CpuValue => P.CpuPercent;
        public double GpuValue => P.GpuPercent;
        public double ReadKbs => P.IoReadKbs;
        public double WriteKbs => P.IoWriteKbs;

        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x4A, 0xE0, 0x80));
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9C, 0xAB, 0xBE));

        public string CpuText => P.CpuPercent < 0 ? "—" : P.CpuPercent.ToString("0.0") + "%";
        public string GpuText => P.GpuPercent < 0 ? "—" : P.GpuPercent.ToString("0") + "%";
        public string ReadText => FormatIo(P.IoReadKbs);
        public string WriteText => FormatIo(P.IoWriteKbs);

        public void Update(ProcessInfo newP)
        {
            var oldMem = P.WorkingSetMb;
            var oldCpu = P.CpuPercent;
            var oldGpu = P.GpuPercent;
            var oldRead = P.IoReadKbs;
            var oldWrite = P.IoWriteKbs;

            P = newP;

            if (oldMem != newP.WorkingSetMb)
            {
                OnPropertyChanged(nameof(MemMb));
                OnPropertyChanged(nameof(MemText));
            }
            if (Math.Abs(oldCpu - newP.CpuPercent) > 0.01)
            {
                OnPropertyChanged(nameof(CpuText));
                OnPropertyChanged(nameof(CpuValue));
            }
            if (Math.Abs(oldGpu - newP.GpuPercent) > 0.01)
            {
                OnPropertyChanged(nameof(GpuText));
                OnPropertyChanged(nameof(GpuValue));
            }
            if (Math.Abs(oldRead - newP.IoReadKbs) > 0.01)
            {
                OnPropertyChanged(nameof(ReadText));
                OnPropertyChanged(nameof(ReadKbs));
            }
            if (Math.Abs(oldWrite - newP.IoWriteKbs) > 0.01)
            {
                OnPropertyChanged(nameof(WriteText));
                OnPropertyChanged(nameof(WriteKbs));
            }
        }

        private static string FormatIo(double kbs)
        {
            if (kbs < 0) return "—";
            return kbs switch
            {
                >= 1024 * 1024 => (kbs / (1024 * 1024)).ToString("0.0") + " GB/s",
                >= 1024 => (kbs / 1024).ToString("0.0") + " MB/s",
                _ => kbs.ToString("0") + " KB/s"
            };
        }
    }

    public class TaskRow : RowBase
    {
        public ScheduledTaskInfo T { get; }
        public TaskRow(ScheduledTaskInfo t) => T = t;
        public string Name => T.Name;
        public string FullPath => T.FullPath;
        public string StateLabel => T.Enabled ? "Enabled" : "Disabled";
        public string Author => string.IsNullOrEmpty(T.Author) ? "—" : T.Author;
        public bool IsOn => T.Enabled;
        public string ToggleTip => IsOn ? "Disable this task" : "Enable this task";
    }

    public class StartupRow : RowBase
    {
        public StartupItem Item { get; }
        public StartupRow(StartupItem item) => Item = item;
        public string Name => Item.Name;
        public string Location => Item.Location;
        public string Command => Item.Command ?? "—";
        public string StatusLabel => Item.DisabledByTothBoost ? "Disabled" : "Active";
        public bool IsOn => !Item.DisabledByTothBoost;
        public string ToggleTip => IsOn ? "Disable" : "Enable";
        public string Classification => GetClassification(Item);
        public bool IsSuggested => Classification.Contains("Suggested");

        private static string GetClassification(StartupItem item)
        {
            var name = (item.Name ?? "").ToLowerInvariant();
            var cmd = (item.Command ?? "").ToLowerInvariant();
            var loc = (item.Location ?? "").ToLowerInvariant();

            // Critical Windows system components
            if (name.Contains("cachetask") || loc.Contains("wininet\\cachetask") ||
                name.Contains("msctfmonitor") || loc.Contains("textservicesframework\\msctfmonitor") ||
                loc.Contains("autochk\\proxy") || loc.Contains("spaceport") ||
                loc.Contains("softwareprotectionplatform") || loc.Contains("data integrity scan") ||
                name.Contains("verifiedpublishercertstorecheck") ||
                cmd.Contains("userinit.exe") || cmd.Contains("winlogon.exe") ||
                cmd.Contains("smss.exe") || cmd.Contains("csrss.exe") || cmd.Contains("lsass.exe"))
            {
                return "System (Critical)";
            }

            // System suggested to disable for gaming/performance
            if (name.Contains("securityhealth") || cmd.Contains("securityhealthsystray") ||
                name.Contains("queuereporting") || loc.Contains("windows error reporting") ||
                cmd.Contains("wermgr.exe") || name.Contains("device user") ||
                cmd.Contains("devicecensus.exe") || name.Contains("clipesuconsumer") ||
                cmd.Contains("clipesuconsumer.exe") || loc.Contains("exploitguard") ||
                loc.Contains("autopilot") || (loc.StartsWith("\\") && loc.Contains("provisioning")) ||
                cmd.Contains("provtool.exe") || (loc.StartsWith("\\") && loc.Contains("workplace join")) ||
                cmd.Contains("dsregcmd.exe") || loc.Contains("appxdeploymentclient") ||
                loc.Contains("active directory rights management") || name.Contains("ceip") ||
                loc.Contains("customer experience improvement program") ||
                name.Contains("onedrive") || cmd.Contains("onedrive.exe") || loc.Contains("onedrive") ||
                name.Contains("cortana") || name.Contains("edgeupdate") || cmd.Contains("microsoftedgeupdate"))
            {
                return "System (Suggested)";
            }

            // Hardware / Driver utilities
            if (name.Contains("realtek") || cmd.Contains("rtkauduservice") || cmd.Contains("ravcpl") ||
                name.Contains("nvidia") || cmd.Contains("nvbackend") || cmd.Contains("nvcontainer") ||
                cmd.Contains("shadowplay") || name.Contains("amd") || name.Contains("radeon") ||
                cmd.Contains("amdrsserv") || name.Contains("intel") || cmd.Contains("igfx") ||
                name.Contains("synaptics") || name.Contains("logitech") || cmd.Contains("lghub") ||
                name.Contains("razer") || cmd.Contains("synapse") || name.Contains("corsair") ||
                cmd.Contains("icue") || name.Contains("asus") || cmd.Contains("armoury") ||
                name.Contains("alienware"))
            {
                return "Driver (Caution)";
            }

            // User apps suggested to disable for faster boot
            if (name.Contains("steam") || cmd.Contains("steam.exe") ||
                name.Contains("epic") || cmd.Contains("epicgames") ||
                name.Contains("discord") || cmd.Contains("discord.exe") ||
                name.Contains("spotify") || cmd.Contains("spotify.exe") ||
                name.Contains("skype") || name.Contains("telegram") ||
                name.Contains("whatsapp") || name.Contains("slack") ||
                name.Contains("teams") || name.Contains("torrent") ||
                cmd.Contains("torrent") || name.Contains("googleupdate") ||
                cmd.Contains("googleupdate") || name.Contains("adobe") ||
                cmd.Contains("acrotray") || name.Contains("ccleaner") ||
                name.Contains("pm2") || cmd.Contains("pm2") ||
                name.Contains("battle.net") || name.Contains("riot") ||
                name.Contains("origin"))
            {
                return "User (Suggested)";
            }

            // Other system tasks
            if (loc.StartsWith("\\microsoft\\windows") || loc.Contains("microsoft\\windows") ||
                cmd.Contains("system32\\rundll32.exe") || name.Contains("calibration loader") ||
                name.Contains("synchronize language") || name.Contains("languagecomponents") ||
                name.Contains("diskdiagnostic") || name.Contains("usertask") ||
                name.Contains("work folders") || name.Contains("offline files") ||
                name.Contains("systemsoundsservice"))
            {
                return "System (Safe)";
            }

            return "User App";
        }
    }

    public class TweakRow : RowBase
    {
        public TweakInfo T { get; }
        public TweakRow(TweakInfo t) => T = t;
        public string Name => T.Name;
        public string Description => T.Description;
        public string Category => T.Category;
        public string State => T.IsApplied ? "Applied" : "Default";
    }

    public class ProfileCard
    {
        public ProfileService.ProfileMeta Meta { get; }
        public bool IsSelected { get; }

        public ProfileCard(ProfileService.ProfileMeta meta, bool isSelected)
        {
            Meta = meta;
            IsSelected = isSelected;
        }

        public string Name => Meta.Name ?? "Default";
        public bool IsDefault => Meta.IsDefault;
        public string CreatedLabel => "Created: " + Meta.CreatedUtc.ToLocalTime().ToString("g");
        public string SavedLabel => Meta.LastSavedUtc.HasValue
            ? "Last saved: " + Meta.LastSavedUtc.Value.ToLocalTime().ToString("g")
            : "No custom save recorded";
        public string Subtitle => IsDefault
            ? "Full original baseline reference"
            : (IsSelected ? "Active profile" : "Saved user profile");
    }
}
