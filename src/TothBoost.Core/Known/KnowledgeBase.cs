using System.Reflection;
using System.Text.Json;
using TothBoost.Core.Models;

namespace TothBoost.Core.Known
{
    /// <summary>
    /// Service classification Knowledge Base embedded in executable (knowledge.json).
    /// Classifies each service as Protected / Warning / Recommended / Optional / Info,
    /// and automatically protects all kernel drivers.
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
                => (n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), new KbEntry(prefix, risk, category, reason));

            return new[]
            {
                Rule("BluetoothUserService", "optional", "Bluetooth", "Bluetooth User Service. Safe to disable if no Bluetooth devices are used."),
                Rule("OneSyncSvc", "recommended", "Sync", "Email, contact, and calendar sync (per-user suffix)."),
                Rule("PimIndexMaintenanceSvc", "recommended", "Contacts", "Contact indexing service (per-user suffix)."),
                Rule("vmic", "optional", "Virtualization", "Hyper-V guest integration service. Safe to disable if not using virtual machines."),
                Rule("vmiconsole", "info", "Virtualization", "Hyper-V integration console."),
                Rule("vmginn", "info", "Virtualization", "Hyper-V guest integration.")
            };
        }

        public static void Classify(ServiceInfo svc)
        {
            // Kernel drivers can never be modified.
            if (svc.IsDriverProtectedType)
            {
                svc.Risk = RiskLevel.Protected;
                svc.RiskReason = "Kernel Driver — disabling may cause BSOD and prevent Windows from booting.";
                svc.Category = "Driver";
                return;
            }

            if (Entries.Value.TryGetValue(svc.Name, out var e))
            {
                svc.Risk = ParseRisk(e.Risk);
                svc.RiskReason = svc.Risk == RiskLevel.Info ? "Cataloged service (Information)." : e.Reason;
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
            svc.RiskReason = "Uncataloged service. No safety classification — modify at your own risk.";
            svc.Category = "General";
        }

        private static RiskLevel ParseRisk(string s) => s.ToLowerInvariant() switch
        {
            "protected" or "critico" => RiskLevel.Protected,
            "warn" or "cautela" => RiskLevel.Warning,
            "recommended" or "recomendado" => RiskLevel.Recommended,
            "optional" or "opcional" => RiskLevel.Optional,
            _ => RiskLevel.Info
        };
    }
}
