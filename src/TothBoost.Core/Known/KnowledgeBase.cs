using System.Reflection;
using System.Text.Json;
using TothBoost.Core.Models;

namespace TothBoost.Core.Known
{
    /// <summary>
    /// Base de conhecimento de classificações de serviços, embutida no executável
    /// (knowledge.json). Classifica cada serviço como Protegido / Cautela /
    /// Recomendado / Opcional / Informação, além de proteger automaticamente
    /// todos os drivers do kernel.
    /// </summary>
    public static class KnowledgeBase
    {
        private sealed record KbEntry(string Name, string Risk, string Category, string Reason);

        private static readonly Lazy<Dictionary<string, KbEntry>> Entries = new(LoadEntries);
        private static readonly (Func<string, bool> Match, KbEntry Entry)[] Prefixes = BuildPrefixRules();

        public const string SourceFile = "TothBoost.Core.Known.knowledge.json";

        private static Dictionary<string, KbEntry> LoadEntries()
        {
            var map = new Dictionary<string, KbEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SourceFile);
                if (stream is null) return map;
                using var doc = JsonDocument.Parse(stream);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var name = el.GetProperty("name").GetString() ?? "";
                    var risk = el.GetProperty("risk").GetString() ?? "info";
                    var cat = el.GetProperty("category").GetString() ?? "";
                    var reason = el.GetProperty("reason").GetString() ?? "";
                    map[name] = new KbEntry(name, risk, cat, reason);
                }
            }
            catch { }
            return map;
        }

        private static (Func<string, bool>, KbEntry)[] BuildPrefixRules()
        {
            static (Func<string, bool>, KbEntry) Rule(string prefix, string risk, string category, string reason)
            {
                var e = new KbEntry(prefix, risk, category, reason);
                return (n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), e);
            }
            return new[]
            {
                Rule("BluetoothUserService", "optional", "Bluetooth", "Serviço Bluetooth de usuário. Desative sem dispositivos BT."),
                Rule("OneSyncSvc", "recommended", "Sincronização", "Sincronização de e-mail/contatos/calendário (sufixo de usuário)."),
                Rule("PimIndexMaintenanceSvc", "recommended", "Contatos", "Indexação de contatos (sufixo de usuário)."),
                Rule("vmic", "optional", "Virtualização", "Serviços de integração de convidado Hyper-V. Desative se não usa VMs."),
                Rule("vmiconsole", "info", "Virtualização", "Console de integração Hyper-V."),
                Rule("vmginn", "info", "Virtualização", "Integração Hyper-V guest.")
            };
        }

        public static void Classify(ServiceInfo svc)
        {
            // Drivers do kernel nunca podem ser tocados.
            if (svc.IsDriverProtectedType)
            {
                svc.Risk = RiskLevel.Protected;
                svc.RiskReason = "Driver do kernel — desativar pode causar tela azul (BSOD) e impedir o Windows de iniciar.";
                svc.Category = "Driver";
                return;
            }

            if (Entries.Value.TryGetValue(svc.Name, out var e))
            {
                svc.Risk = ParseRisk(e.Risk);
                svc.RiskReason = svc.Risk == RiskLevel.Info ? "Serviço catalogado como informação." : e.Reason;
                svc.Category = e.Category;
                return;
            }

            foreach (var (match, rule) in Prefixes)
            {
                if (match(svc.Name))
                {
                    svc.Risk = ParseRisk(rule.Risk);
                    svc.RiskReason = rule.Reason;
                    svc.Category = rule.Category;
                    return;
                }
            }

            svc.Risk = RiskLevel.Info;
            svc.RiskReason = "Serviço não catalogado. Sem classificação de segurança — altere por sua conta e risco.";
            svc.Category = "Desconhecido";
        }

        public static RiskLevel ParseRisk(string risk) => risk switch
        {
            "protected" => RiskLevel.Protected,
            "warn" => RiskLevel.Warning,
            "recommended" => RiskLevel.Recommended,
            "optional" => RiskLevel.Optional,
            "info" => RiskLevel.Info,
            _ => RiskLevel.Info
        };

        /// <summary>Serviços recomendados para o perfil de jogos.</summary>
        public static IEnumerable<string> GamingPresetNames =>
            Entries.Value.Values.Where(e => e.Risk == "recommended").Select(e => e.Name).Distinct();
    }

    public class ServiceEntry
    {
        public required string Name { get; init; }
        public required RiskLevel Risk { get; set; }
        public string Reason { get; set; } = "";
        public string Category { get; set; } = "";
    }
}