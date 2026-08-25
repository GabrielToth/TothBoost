using System.Text.Json;
using Microsoft.Win32;
using TothBoost.Core.Known;
using TothBoost.Core.Models;
using TothBoost.Core.Scanners;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Snapshot
{
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
    /// Preserves original system baseline state BEFORE any modification,
    /// allowing full restoration of services, scheduled tasks, startup items, and tweaks.
    /// </summary>
    public static class SnapshotService
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TothBoost", "Snapshots");

        public static bool BaselineExists => File.Exists(Path.Combine(Root, "baseline.json"));

        /// <summary>
        /// Creates original baseline state once — never overwrites existing baseline.
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
            var path = Path.Combine(Root, name + ".json");
            if (File.Exists(path) && string.Equals(name, "baseline", StringComparison.OrdinalIgnoreCase)) return;

            var doc = new SnapshotDocument
            {
                Name = name,
                CreatedUtc = DateTime.UtcNow,
                HostName = Environment.MachineName,
                WindowsVersion = Environment.OSVersion.VersionString,
                Services = services.Select(s => new ServiceSnapshot
                {
                    Name = s.Name,
                    StartKind = (int)s.StartKind,
                    WasRunning = s.RunningState == "Running"
                }).ToList(),
                Tasks = tasks.Select(t => new TaskSnapshot
                {
                    Name = t.Name,
                    FullPath = t.FullPath,
                    Enabled = t.Enabled
                }).ToList(),
                Startups = startups.Where(s => !s.IsFolder && !string.IsNullOrEmpty(s.LocationKey)).Select(s => new StartupSnapshot
                {
                    Hive = s.LocationKey!.StartsWith("SOFTWARE", StringComparison.OrdinalIgnoreCase) ? "LM" : "CU",
                    KeyPath = s.LocationKey,
                    ValueName = s.Name,
                    Value = s.Command ?? ""
                }).ToList(),
                StartupFiles = startups.Where(s => s.IsFolder && !string.IsNullOrEmpty(s.Command)).Select(s => new StartupFileSnapshot
                {
                    Name = s.Name,
                    FilePath = s.Command!
                }).ToList(),
                AppliedTweaks = TweakCatalog.All().Where(t =>
                {
                    if (Enum.TryParse<TweakKind>(t.Id, out var k)) return TweakManager.IsApplied(k);
                    return false;
                }).Select(t => t.Id).ToList()
            };

            File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOpts));
        }

        public static ActionReport Restore(string snapshotName,
            IEnumerable<ServiceInfo> currentServices,
            IEnumerable<ScheduledTaskInfo> currentTasks,
            IEnumerable<StartupItem> currentStartups)
        {
            var report = new ActionReport();
            var path = Path.Combine(Root, snapshotName + ".json");
            if (!File.Exists(path))
            {
                report.Add("Snapshot", "restore", false, $"Snapshot '{snapshotName}' not found.");
                return report;
            }

            SnapshotDocument? doc;
            try { doc = JsonSerializer.Deserialize<SnapshotDocument>(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                report.AddError("Snapshot", "read file", ex);
                return report;
            }
            if (doc is null) return report;

            // 1) Services
            foreach (var s in doc.Services)
            {
                var cur = currentServices.FirstOrDefault(x => x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase));
                if (cur is null || cur.IsProtected) continue;

                var origStart = (ServiceStartKind)s.StartKind;
                try
                {
                    if (cur.StartKind != origStart)
                    {
                        ServiceActions.ChangeStartKind(s.Name, origStart);
                        report.Add(s.Name, $"startup set to {origStart}", true);
                    }
                    if (s.WasRunning && cur.RunningState != "Running")
                    {
                        ServiceActions.Start(s.Name);
                        report.Add(s.Name, "started", true);
                    }
                    else if (!s.WasRunning && cur.RunningState == "Running")
                    {
                        ServiceActions.Stop(s.Name);
                        report.Add(s.Name, "stopped", true);
                    }
                }
                catch (Exception ex) { report.AddError(s.Name, "restore service", ex); }
            }

            // 2) Tasks
            foreach (var t in doc.Tasks)
            {
                var cur = currentTasks.FirstOrDefault(x => x.FullPath.Equals(t.FullPath, StringComparison.OrdinalIgnoreCase));
                if (cur is null) continue;
                if (cur.Enabled != t.Enabled)
                {
                    try
                    {
                        TaskScanner.SetEnabled(t.FullPath, t.Enabled);
                        report.Add(t.Name, t.Enabled ? "enabled" : "disabled", true);
                    }
                    catch (Exception ex) { report.AddError(t.Name, "restore task", ex); }
                }
            }

            // 3) Startup Items
            foreach (var st in currentStartups)
            {
                var orig = doc.Startups.FirstOrDefault(x => x.ValueName.Equals(st.Name, StringComparison.OrdinalIgnoreCase)) ??
                           (object?)doc.StartupFiles.FirstOrDefault(x => x.Name.Equals(st.Name, StringComparison.OrdinalIgnoreCase));

                bool shouldDisable = orig == null;
                if (st.DisabledByTothBoost != shouldDisable)
                {
                    try
                    {
                        StartupScanner.SetDisabled(st, shouldDisable);
                        report.Add(st.Name, shouldDisable ? "disabled" : "enabled", true);
                    }
                    catch (Exception ex) { report.AddError(st.Name, "restore startup", ex); }
                }
            }

            // 4) Tweaks
            foreach (var tw in TweakCatalog.All())
            {
                if (!Enum.TryParse<TweakKind>(tw.Id, out var kind)) continue;
                bool wasApplied = doc.AppliedTweaks.Contains(tw.Id);
                bool nowApplied = TweakManager.IsApplied(kind);
                if (wasApplied != nowApplied)
                {
                    var r = wasApplied ? TweakManager.Apply(kind) : TweakManager.Revert(kind);
                    report.Merge(r);
                }
            }

            return report;
        }
    }
}
