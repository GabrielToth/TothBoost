using System.Text.Json;
using Microsoft.Win32;
using TothBoost.Core.Known;
using TothBoost.Core.Models;
using TothBoost.Core.Scanners;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Snapshot
{
    // ---------- Modelos do documento de snapshot ----------

    public class ServiceSnapshot
    {
        public string Name { get; set; } = "";
        public int StartKind { get; set; }
        public bool WasRunning { get; set; }
    }

    public class TaskSnapshot
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public class StartupSnapshot
    {
        public string Hive { get; set; } = "";        // "LM" | "CU"
        public string KeyPath { get; set; } = "";
        public string ValueName { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class StartupFileSnapshot
    {
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    public class SnapshotDocument
    {
        public string Version { get; set; } = "1.0";
        public string Name { get; set; } = "baseline";
        public DateTime CreatedUtc { get; set; }
        public string HostName { get; set; } = "";
        public string WindowsVersion { get; set; } = "";
        public List<ServiceSnapshot> Services { get; set; } = new();
        public List<TaskSnapshot> Tasks { get; set; } = new();
        public List<StartupSnapshot> Startups { get; set; } = new();
        public List<StartupFileSnapshot> StartupFiles { get; set; } = new();
        public List<string> AppliedTweaks { get; set; } = new();
    }

    /// <summary>
    /// Guarda o estado original do sistema ANTES de qualquer modificação e
    /// permite restaurar tudo (serviços, tarefas agendadas, inicialização e
    /// ajustes) para o estado 100% original. NADA é excluído permanentemente.
    /// </summary>
    public static class SnapshotService
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TothBoost", "Snapshots");

        public static bool BaselineExists => File.Exists(Path.Combine(Root, "baseline.json"));

        public static List<SnapshotItem> List()
        {
            var items = new List<SnapshotItem>();
            if (!Directory.Exists(Root)) return items;
            foreach (var f in Directory.EnumerateFiles(Root, "*.json"))
            {
                try
                {
                    var fi = new FileInfo(f);
                    var doc = JsonSerializer.Deserialize<SnapshotDocument>(File.ReadAllText(f));
                    items.Add(new SnapshotItem
                    {
                        Name = doc?.Name ?? Path.GetFileNameWithoutExtension(f),
                        CreatedUtc = doc?.CreatedUtc ?? fi.LastWriteTimeUtc,
                        HostName = doc?.HostName,
                        IsBaseline = string.Equals(doc?.Name, "baseline", StringComparison.OrdinalIgnoreCase),
                        FileSize = fi.Length
                    });
                }
                catch { }
            }
            return items.OrderByDescending(i => i.CreatedUtc).ToList();
        }

        /// <summary>
        /// Cria a linha de base (estado original) — só é criada uma vez e NUNCA
        /// sobrescreve o que já existe.
        /// </summary>
        public static bool EnsureBaseline(IEnumerable<ServiceInfo> services,
            IEnumerable<ScheduledTaskInfo> tasks, IEnumerable<StartupItem> startups)
        {
            if (BaselineExists) return false;
            Create("baseline", services, tasks, startups);
            return true;
        }

        public static void Create(string name, IEnumerable<ServiceInfo> services,
            IEnumerable<ScheduledTaskInfo> tasks, IEnumerable<StartupItem> startups)
        {
            Directory.CreateDirectory(Root);
            var doc = new SnapshotDocument
            {
                Name = name,
                CreatedUtc = DateTime.UtcNow,
                HostName = Environment.MachineName,
                WindowsVersion = $"{Environment.OSVersion.VersionString} ({Environment.Is64BitOperatingSystem})",
                Services = services.Select(s => new ServiceSnapshot
                {
                    Name = s.Name,
                    StartKind = (int)s.StartKind,
                    WasRunning = s.RunningState == "Executando"
                }).ToList(),
                Tasks = tasks.Select(t => new TaskSnapshot { Name = t.Name, FullPath = t.FullPath, Enabled = t.Enabled }).ToList(),
                Startups = startups.Where(x => !x.IsFolder && !string.IsNullOrEmpty(x.LocationKey))
                    .Select(s => new StartupSnapshot
                    {
                        Hive = IsLm(s.LocationKey!) ? "LM" : "CU",
                        KeyPath = s.LocationKey!,
                        ValueName = s.Name,
                        Value = s.Command ?? ""
                    }).ToList(),
                StartupFiles = startups.Where(x => x.IsFolder && !string.IsNullOrEmpty(x.Command))
                    .Select(s => new StartupFileSnapshot { Name = s.Name, FilePath = s.Command! }).ToList()
            };

            File.WriteAllText(Path.Combine(Root, Safe(name) + ".json"),
                JsonSerializer.Serialize(doc, JsonOpts));
        }

        public static void RecordAppliedTweak(string tweakId)
        {
            var path = Path.Combine(Root, "baseline.json");
            if (!File.Exists(path)) return;
            try
            {
                var doc = JsonSerializer.Deserialize<SnapshotDocument>(File.ReadAllText(path));
                if (doc is null) return;
                if (!doc.AppliedTweaks.Contains(tweakId))
                {
                    doc.AppliedTweaks.Add(tweakId);
                    File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOpts));
                }
            }
            catch { }
        }

        /// <summary>Revert todos os ajustes registrados no snapshot da linha de base.</summary>
        public static void RevertAllRecordedTweaks()
        {
            var path = Path.Combine(Root, "baseline.json");
            if (!File.Exists(path)) return;
            try
            {
                var doc = JsonSerializer.Deserialize<SnapshotDocument>(File.ReadAllText(path));
                if (doc is null) return;
                foreach (var t in doc.AppliedTweaks)
                {
                    if (Enum.TryParse<TweakKind>(t, out var kind) && TweakManager.IsApplied(kind))
                        TweakManager.Revert(kind);
                }
                doc.AppliedTweaks.Clear();
                File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOpts));
            }
            catch { }
        }

        // ---------- Restauração ----------

        public static ActionReport Restore(string name)
        {
            var report = new ActionReport();
            var path = Path.Combine(Root, Safe(name) + ".json");
            if (!File.Exists(path))
            {
                report.Add(name, "restauração", false, "Snapshot não encontrado em " + path);
                return report;
            }

            SnapshotDocument doc;
            try { doc = JsonSerializer.Deserialize<SnapshotDocument>(File.ReadAllText(path))!; }
            catch (Exception ex) { report.Add(name, "ler snapshot", false, ex.Message); return report; }

            foreach (var s in doc.Services) RestoreService(report, s);
            foreach (var t in doc.Tasks) RestoreTask(report, t);
            foreach (var st in doc.Startups) RestoreStartup(report, st);
            foreach (var sf in doc.StartupFiles) RestoreStartupFile(report, sf);
            foreach (var tw in doc.AppliedTweaks)
            {
                try
                {
                    if (Enum.TryParse<TweakKind>(tw, out var kind) && TweakManager.IsApplied(kind))
                    {
                        TweakManager.Revert(kind);
                        report.Add(tw, "reverter ajuste", true);
                    }
                }
                catch (Exception ex) { report.AddError(tw, "reverter ajuste", ex); }
            }

            report.Add("COMANDO", "restauração concluída", true,
                $"{report.Succeeded} itens OK, {report.Failed} falhas");
            return report;
        }

        private static void RestoreService(ActionReport report, ServiceSnapshot s)
        {
            try
            {
                var current = CurrentStartKind(s.Name);
                if (current != (ServiceStartKind)s.StartKind)
                {
                    ServiceActions.ChangeStartKind(s.Name, (ServiceStartKind)s.StartKind);
                    report.Add(s.Name, $"inicialização → {Label((ServiceStartKind)s.StartKind)}", true);
                }
                if (s.WasRunning)
                {
                    ServiceActions.Start(s.Name);
                    report.Add(s.Name, "iniciar", true);
                }
            }
            catch (Exception ex) { report.AddError(s.Name, "restaurar serviço", ex); }
        }

        private static void RestoreTask(ActionReport report, TaskSnapshot t)
        {
            try
            {
                if (ReadTaskEnabled(t.FullPath) != t.Enabled)
                {
                    TaskActions.SetEnabled(t.FullPath, t.Enabled);
                    report.Add(t.Name, t.Enabled ? "habilitar" : "desabilitar", true);
                }
            }
            catch (Exception ex) { report.AddError(t.Name, "restaurar tarefa", ex); }
        }

        private static void RestoreStartup(ActionReport report, StartupSnapshot st)
        {
            try
            {
                var where = st.Hive == "LM" ? Registry.LocalMachine : Registry.CurrentUser;
                if (StartupScanner.IsQuarantined(where, st.KeyPath, st.ValueName))
                {
                    // Devolver valor da quarentena ao local original.
                    var qKey = where.OpenSubKey(StartupScanner.QuarantineKey(st.KeyPath));
                    if (qKey is not null)
                    {
                        var v = qKey.GetValue(st.ValueName)?.ToString() ?? st.Value;
                        using var orig = where.CreateSubKey(st.KeyPath, true);
                        orig.SetValue(st.ValueName, v, RegistryValueKind.String);
                        qKey.DeleteValue(st.ValueName, false);
                        report.Add(st.ValueName, "restaurar inicialização", true);
                    }
                }
                else
                {
                    using var orig = where.OpenSubKey(st.KeyPath, true);
                    if (orig?.GetValue(st.ValueName) is null && !string.IsNullOrEmpty(st.Value))
                    {
                        orig.SetValue(st.ValueName, st.Value, RegistryValueKind.String);
                        report.Add(st.ValueName, "regravar inicialização", true);
                    }
                }
            }
            catch (Exception ex) { report.AddError(st.ValueName, "restaurar inicialização", ex); }
        }

        private static void RestoreStartupFile(ActionReport report, StartupFileSnapshot sf)
        {
            try
            {
                var src = Path.Combine(StartupScanner.QuarantineFiles, sf.Name);
                if (!File.Exists(src)) return;
                var dir = Path.GetDirectoryName(sf.FilePath);
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                File.Move(src, sf.FilePath, true);
                report.Add(sf.Name, "restaurar arquivo de inicialização", true);
            }
            catch (Exception ex) { report.AddError(sf.Name, "restaurar arquivo", ex); }
        }

        // ---------- helpers ----------

        private static string Safe(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static bool IsLm(string key) =>
            key.StartsWith("SOFTWARE", StringComparison.OrdinalIgnoreCase);

        public static ServiceStartKind? CurrentStartKind(string name)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(RegistryKeys.SvcRoot + "\\" + name, false);
                if (k is null) return null;
                var v = k.GetValue("Start");
                return v is int i && Enum.IsDefined(typeof(ServiceStartKind), i)
                    ? (ServiceStartKind)i
                    : (ServiceStartKind?)null;
            }
            catch { return null; }
        }

        public static bool? ReadTaskEnabled(string fullPath)
        {
            var (exit, outStr) = Proc.Run("schtasks.exe", $"/Query /TN \"{fullPath}\" /XML");
            if (exit != 0 || !outStr.Contains("<Enabled>")) return null;
            var m = System.Text.RegularExpressions.Regex.Match(outStr, "<Enabled>([^<]*)</Enabled>");
            if (!m.Success) return null;
            return m.Groups[1].Value == "true";
        }

        private static string Label(ServiceStartKind k) => k switch
        {
            ServiceStartKind.Boot => "inicialização (boot)",
            ServiceStartKind.System => "sistema",
            ServiceStartKind.Automatic => "automático",
            ServiceStartKind.Manual => "manual",
            ServiceStartKind.Disabled => "desabilitado",
            _ => "?"
        };
    }
}