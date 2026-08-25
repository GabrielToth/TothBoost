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
        public long MemMb => P.WorkingSetMb;
        public string Session => P.SessionName;
        public string Path => P.Path ?? "—";
        public bool IsProtected => P.Protected;
        public string ProtectReason => P.Protected ? "Critical Windows process — cannot be terminated." : "";
        public string ProtectionLabel => P.Protected ? "PROTECTED" : "Allowed";
        public Brush ProtectionColor => P.Protected ? Green : Dim;

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

            if (oldMem != newP.WorkingSetMb) OnPropertyChanged(nameof(MemMb));
            if (Math.Abs(oldCpu - newP.CpuPercent) > 0.01) OnPropertyChanged(nameof(CpuText));
            if (Math.Abs(oldGpu - newP.GpuPercent) > 0.01) OnPropertyChanged(nameof(GpuText));
            if (Math.Abs(oldRead - newP.IoReadKbs) > 0.01) OnPropertyChanged(nameof(ReadText));
            if (Math.Abs(oldWrite - newP.IoWriteKbs) > 0.01) OnPropertyChanged(nameof(WriteText));
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
        public string StatusLabel => Item.DisabledByTothBoost ? "Disabled (Quarantine)" : "Active";
        public bool IsOn => !Item.DisabledByTothBoost;
        public string ToggleTip => IsOn ? "Disable (move to quarantine)" : "Enable (restore from quarantine)";
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
