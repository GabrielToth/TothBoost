using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TothBoost.Core.Models;
using TothBoost.Core.Snapshot;

namespace TothBoost.App
{
    /// <summary>Linha observável para grids (checkboxes de seleção múltipla).</summary>
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
        /// <summary>Ligado = habilitado (inicialização automática); desligado = desabilitado.</summary>
        public bool IsOn => S.StartKind != ServiceStartKind.Disabled;
        public string ToggleTip => CanTouch ? (IsOn ? "Desabilitar este serviço" : "Habilitar este serviço") : "Protegido pelo sistema";
    }

    public class ProcRow : RowBase
    {
        public ProcessInfo P { get; }
        public ProcRow(ProcessInfo p) => P = p;
        public int Pid => P.Pid;
        public string Name => P.Name;
        public long MemMb => P.WorkingSetMb;
        public string Session => P.SessionName;
        public string Path => P.Path ?? "—";
        public bool IsProtected => P.Protected;
        public string ProtectReason => P.Protected ? "Processo crítico do Windows — não pode ser encerrado." : "";
        public string ProtectionLabel => P.Protected ? "PROTEGIDO" : "Livre";
        public Brush ProtectionColor => P.Protected ? Green : Dim;

        private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x4A, 0xE0, 0x80));
        private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9C, 0xAB, 0xBE));

        public string CpuText => P.CpuPercent < 0 ? "—" : P.CpuPercent.ToString("0.0") + "%";
        public string GpuText => P.GpuPercent < 0 ? "—" : P.GpuPercent.ToString("0") + "%";
        public string ReadText => FormatIo(P.IoReadKbs);
        public string WriteText => FormatIo(P.IoWriteKbs);

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
        public string State => T.Enabled ? "Habilitada" : "Desabilitada";
        public bool Enabled => T.Enabled;
        public string Author => T.Author ?? "—";
        public bool IsOn => T.Enabled;
    }

    public class StartupRow : RowBase
    {
        public StartupItem I { get; }
        public StartupRow(StartupItem i) => I = i;
        public string Name => I.Name;
        public string Location => I.Location;
        public string Command => I.Command ?? "";
        public string State => I.DisabledByTothBoost ? "Desabilitada (TothBoost)" : "Habilitada";
        public bool IsDisabledByTb => I.DisabledByTothBoost;
        public bool IsOn => !I.DisabledByTothBoost;
    }

    public class TweakRow : RowBase
    {
        public TweakInfo T { get; }
        public TweakRow(TweakInfo t) => T = t;
        public string Name => T.Name;
        public string Description => T.Description;
        public string Category => T.Category;
        public string State => T.IsApplied ? "Aplicado" : "Não aplicado";
        public bool IsApplied => T.IsApplied;
    }

    public class ProfileCard
    {
        public ProfileService.ProfileMeta M { get; }
        public ProfileCard(ProfileService.ProfileMeta m, bool isCurrent) { M = m; IsCurrent = isCurrent; }
        public string Name => M.Name ?? "?";
        public bool IsDefault => M.IsDefault;
        public string KindLabel => IsDefault ? "PADRÃO — save total (estado original)" : "Perfil criado pelo usuário";
        public string HostName => M.HostName ?? "";
        public string CreatedText => "Criado em " + M.CreatedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string LastSavedText => M.LastSavedUtc is DateTime d
            ? "Salvo em " + d.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            : "Nunca salvo";
        public bool IsCurrent { get; }
        public string Initials => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    }

    public class RestorePointRow
    {
        public RestorePointInfo P { get; }
        public RestorePointRow(RestorePointInfo p) => P = p;
        public long Seq => P.Sequence;
        public string Description => string.IsNullOrEmpty(P.Description) ? "Ponto de restauração" : P.Description;
        public string WhenText => P.When.ToString("dd/MM/yyyy HH:mm:ss");
    }
}