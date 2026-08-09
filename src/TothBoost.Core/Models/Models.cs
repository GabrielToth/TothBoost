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
        /// <summary>Não tocar: desativar pode quebrar o Windows ou causar BSOD.</summary>
        Protected = 0,
        /// <summary>Pode afetar funcionalidades do sistema/segurança. Só desativar com conhecimento.</summary>
        Warning = 1,
        /// <summary>Seguro de desativar para desempenho em jogos.</summary>
        Recommended = 2,
        /// <summary>Seguro de desativar apenas se o recurso não for usado (impressora, Bluetooth etc.).</summary>
        Optional = 3,
        /// <summary>Outro. Sem impacto relevante.</summary>
        Info = 4
    }

    public static class RiskText
    {
        public static string Label(RiskLevel r) => r switch
        {
            RiskLevel.Protected => "CRÍTICO — não tocar",
            RiskLevel.Warning => "Cautela",
            RiskLevel.Recommended => "Recomendado p/ jogos",
            RiskLevel.Optional => "Opcional",
            _ => "Sem classificação"
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
        public string? RunningState { get; init; }   // "4 Running", "1 Stopped", "" (drivers)
        public RiskLevel Risk { get; set; }
        public string RiskReason { get; set; } = "";
        public string Category { get; set; } = "";
        public string Group { get; init; } = "";
        public bool IsProtected => Risk == RiskLevel.Protected;
        public bool IsProtectedDriver => IsDriverProtected;
        public bool IsDriverProtected => IsDriverProtectedType;
        public bool IsDriverProtectedType { get; init; }
        public string StartKindLabel => StartKind switch
        {
            ServiceStartKind.Boot => "Inicialização (boot)",
            ServiceStartKind.System => "Sistema",
            ServiceStartKind.Automatic => "Automático",
            ServiceStartKind.Manual => "Manual",
            ServiceStartKind.Disabled => "Desabilitado",
            _ => "?"
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
        /// <summary>Percentual de CPU (100% = 1 núcleo). &lt;0 = indisponível.</summary>
        public double CpuPercent { get; set; } = -1;
        /// <summary>Percentual de utilização da GPU. &lt;0 = indisponível.</summary>
        public double GpuPercent { get; set; } = -1;
        /// <summary>Leitura de disco em KB/s. &lt;0 = indisponível.</summary>
        public double IoReadKbs { get; set; } = -1;
        /// <summary>Gravação de disco em KB/s. &lt;0 = indisponível.</summary>
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
        public required string Location { get; init; }      // legível (Ex.: HKCU\...\Run / Pasta Inicialização)
        public string? LocationKey { get; init; }           // chave de registro, se aplicável
        public string? Command { get; init; }               // comando (registro) ou caminho (arquivo)
        public bool IsFolder { get; init; }
        public bool Existing { get; set; } = true;
        public bool DisabledByTothBoost { get; set; }
    }

    public class TweakInfo
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public string Category { get; init; } = "Geral";
        public bool Applies { get; set; }
        public bool IsApplied { get; set; }
        public string? ValueText { get; set; }
    }

    public class SnapshotItem
    {
        public required string Name { get; init; }
        public DateTime CreatedUtc { get; init; }
        public string? HostName { get; init; }
        public bool IsBaseline { get; init; }
        public long FileSize { get; init; }
    }

    public class ActionReport
    {
        public List<ActionLine> Lines { get; } = new();
        public int Succeeded => Lines.Count(l => l.Success);
        public int Failed => Lines.Count(l => !l.Success);
        public void Add(string item, string action, bool ok, string? detail = null) =>
            Lines.Add(new ActionLine(item, action, ok, detail));
        public void AddError(string item, string action, Exception ex) =>
            Lines.Add(new ActionLine(item, action, false, ex.Message.Split('\n')[0].Trim()));
    }

    public class ActionLine
    {
        public ActionLine(string item, string action, bool success, string? detail)
        {
            Item = item; Action = action; Success = success; Detail = detail ?? "";
        }
        public string Item { get; }
        public string Action { get; }
        public bool Success { get; }
        public string Detail { get; }
    }
}