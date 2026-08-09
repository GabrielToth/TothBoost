using System.ComponentModel;
using System.Runtime.CompilerServices;
using TothBoost.Core.Models;

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
}