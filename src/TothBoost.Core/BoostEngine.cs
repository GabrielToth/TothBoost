using TothBoost.Core.Known;
using TothBoost.Core.Models;
using TothBoost.Core.Scanners;
using TothBoost.Core.Snapshot;
using TothBoost.Core.Utilities;

namespace TothBoost.Core
{
    /// <summary>
    /// Core orchestration engine: manages baseline snapshot, service classification,
    /// action execution, and system restoration. Ensures no modification occurs
    /// before safety baseline exists.
    /// </summary>
    public sealed class BoostEngine
    {
        private readonly ServiceScanner _svcScanner = new();
        private readonly TaskScanner _taskScanner = new();

        public bool BaselineExists => SnapshotService.BaselineExists;

        /// <summary>True if first-run initialization (restore point + default profile) is complete.</summary>
        public bool IsInitialized => RestorePointService.IsInitialized;

        /// <summary>
        /// First-run setup routine:
        /// 1) Creates Windows System Restore Point;
        /// 2) Performs initial full scan;
        /// 3) Saves initial profile with ORIGINAL state of ALL services, tasks, startup, and tweaks;
        /// 4) Marks computer as initialized.
        /// </summary>
        public (bool RestorePointOk, string Message) InitialSetup()
        {
            var (rpOk, rpMsg) = RestorePointService.CreateInitialPoint();
            if (rpOk)
            {
                try { RestorePointService.MarkInitialized(); } catch { }
            }

            EnsureBaseline();
            ShortcutManager.CreateStartMenuShortcut();
            return (rpOk, rpMsg);
        }

        private static bool IsCriticalTask(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;
            var lower = fullPath.ToLowerInvariant();
            return lower.Contains("cachetask")
                || lower.Contains("msctfmonitor")
                || lower.Contains("autochk\\proxy")
                || lower.Contains("spaceport")
                || lower.Contains("softwareprotectionplatform")
                || lower.Contains("data integrity scan")
                || lower.Contains("verifiedpublishercertstorecheck")
                || lower.Contains("userinit")
                || lower.Contains("winlogon");
        }

        public void EnsureBaseline()
        {
            if (!SnapshotService.BaselineExists)
            {
                var services = GetServices();
                var tasks = GetTasks();
                var startups = GetStartups();
                SnapshotService.EnsureBaseline(services, tasks, startups);
                SaveCapture("Default");
            }
            EnsureExtremeProfile();
        }

        public void EnsureExtremeProfile()
        {
            if (ProfileService.CaptureExists("Extreme")) return;
            var services = GetServices();
            var tasks = GetTasks();
            var startups = GetStartups();
            var tweaks = GetTweaks();

            ProfileService.Create("Extreme");

            var capture = new ProfileCapture
            {
                Name = "Extreme",
                CapturedAt = DateTime.Now,
                Services = services
                    .Select(s => new ProfileServiceState
                    {
                        Name = s.Name,
                        StartKind = s.IsProtected ? (int)s.StartKind : (int)ServiceStartKind.Disabled,
                        Running = s.IsProtected && s.RunningState == "Running"
                    })
                    .ToList(),
                Tasks = tasks
                    .Select(t => new ProfileTaskState
                    {
                        FullPath = t.FullPath,
                        Enabled = IsCriticalTask(t.FullPath)
                    })
                    .ToList(),
                Startups = startups
                    .Select(i => new ProfileStartupState
                    {
                        Name = i.Name,
                        Location = i.Location,
                        Exists = i.Existing,
                        DisabledByTothBoost = true
                    })
                    .ToList(),
                Tweaks = tweaks
                    .Select(t => new ProfileTweakState
                    {
                        Id = t.Id,
                        Applied = true
                    })
                    .ToList()
            };
            ProfileService.SaveCapture("Extreme", capture);
            ProfileService.MarkSaved("Extreme");
        }

        public System.Threading.Tasks.Task<UpdateCheckResult> CheckForUpdatesAsync() => UpdateChecker.CheckForUpdatesAsync();

        public string CurrentProfileName => ProfileService.CurrentName() ?? "Default";

        public void SaveCapture(string? profileName = null)
        {
            var capture = new ProfileCapture
            {
                Name = profileName ?? CurrentProfileName,
                CapturedAt = DateTime.Now,
                Services = GetServices()
                    .Select(s => new ProfileServiceState { Name = s.Name, StartKind = (int)s.StartKind, Running = s.RunningState == "Running" })
                    .ToList(),
                Tasks = GetTasks()
                    .Select(t => new ProfileTaskState { FullPath = t.FullPath, Enabled = t.Enabled })
                    .ToList(),
                Startups = GetStartups()
                    .Select(i => new ProfileStartupState { Name = i.Name, Location = i.Location, Exists = i.Existing, DisabledByTothBoost = i.DisabledByTothBoost })
                    .ToList(),
                Tweaks = GetTweaks()
                    .Select(t => new ProfileTweakState { Id = t.Id, Applied = t.IsApplied })
                    .ToList()
            };
            var target = profileName ?? ProfileService.CurrentName() ?? "Default";
            ProfileService.SaveCapture(target, capture);
            ProfileService.MarkSaved(target);
        }

        public List<ProfileService.ProfileMeta> ListProfiles() => ProfileService.List();

        public void CreateProfile(string name) => ProfileService.Create(name);

        public void SelectProfile(string name) => ProfileService.Select(name);

        /// <summary>
        /// Applies all configurations saved in a profile (services, tasks, startup, tweaks).
        /// </summary>
        public ActionReport ApplyProfile(string profileName)
        {
            var report = new ActionReport();
            EnsureBaseline();
            var capture = ProfileService.LoadCapture(profileName);
            if (capture is null)
            {
                report.Add(profileName, "load profile", false, "Profile not found or contains no saved data.");
                return report;
            }

            // 1. Services
            var currentServices = GetServices();
            foreach (var sState in capture.Services)
            {
                var current = currentServices.FirstOrDefault(x => x.Name.Equals(sState.Name, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.IsProtected) continue;

                var targetStart = (ServiceStartKind)sState.StartKind;
                if (targetStart == ServiceStartKind.Disabled)
                {
                    if (current.StartKind != ServiceStartKind.Disabled || current.RunningState == "Running")
                    {
                        var res = DisableServices(new[] { current.Name }, currentServices);
                        report.Merge(res);
                    }
                }
                else
                {
                    if (current.StartKind != targetStart)
                    {
                        var res = EnableServices(new[] { current.Name }, targetStart, currentServices);
                        report.Merge(res);
                    }
                }
            }

            // 2. Scheduled Tasks
            var currentTasks = GetTasks();
            foreach (var tState in capture.Tasks)
            {
                if (IsCriticalTask(tState.FullPath) && !tState.Enabled) continue;
                var current = currentTasks.FirstOrDefault(x => x.FullPath.Equals(tState.FullPath, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.Enabled == tState.Enabled) continue;
                try
                {
                    TaskScanner.SetEnabled(tState.FullPath, tState.Enabled);
                    report.Add(tState.FullPath, tState.Enabled ? "enabled" : "disabled", true);
                }
                catch (Exception ex)
                {
                    report.AddError(tState.FullPath, "apply task profile", ex);
                }
            }

            // 3. Startup Items
            var currentStartups = GetStartups();
            foreach (var stState in capture.Startups)
            {
                var current = currentStartups.FirstOrDefault(x => x.Name.Equals(stState.Name, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.DisabledByTothBoost == stState.DisabledByTothBoost) continue;
                try
                {
                    StartupScanner.SetDisabled(current, stState.DisabledByTothBoost);
                    report.Add(current.Name, stState.DisabledByTothBoost ? "disabled" : "enabled", true);
                }
                catch (Exception ex)
                {
                    report.AddError(current.Name, "apply startup profile", ex);
                }
            }

            // 4. Tweaks
            foreach (var twState in capture.Tweaks)
            {
                if (!Enum.TryParse<TweakKind>(twState.Id, out var kind)) continue;
                var isApplied = TweakManager.IsApplied(kind);
                if (isApplied != twState.Applied)
                {
                    var res = twState.Applied ? ApplyTweak(twState.Id) : RevertTweak(twState.Id);
                    report.Merge(res);
                }
            }

            ProfileService.Select(profileName);
            return report;
        }

        public List<RestorePointService.RestorePointItem> ListRestorePoints() => RestorePointService.ListPoints();

        public (bool Ok, string Message) CreateRestorePoint(string description) => RestorePointService.CreatePoint(description);

        public (bool Ok, string Message) RestoreSystemToPoint(long sequence) => RestorePointService.RestoreToPoint(sequence);

        public (bool Ok, string Message) DeleteRestorePoint(long sequence) => RestorePointService.DeletePoint(sequence);

        public List<StartupItem> GetStartups() => new StartupScanner().ScanAll();

        public List<JunkCategoryInfo> ScanJunkFiles() => JunkCleaner.Scan();

        public CleanResult CleanJunkFiles(IEnumerable<string>? categoryIds = null) => JunkCleaner.Clean(categoryIds);

        public MemoryTrimResult TrimMemory() => MemoryOptimizer.TrimWorkingSet();

        public NetworkActionResult FlushDns() => NetworkOptimizer.FlushDns();

        public List<TweakInfo> GetTweaks()
        {
            var all = TweakCatalog.All();
            foreach (var t in all)
            {
                if (Enum.TryParse<TweakKind>(t.Id, out var kind))
                {
                    t.Applies = true;
                    t.IsApplied = TweakManager.IsApplied(kind);
                }
            }
            return all.ToList();
        }

        // ---------- Services ----------

        /// <summary>Disables services and stops them.</summary>
        public ActionReport DisableServices(IEnumerable<string> names, IEnumerable<ServiceInfo> services)
        {
            var report = new ActionReport();
            var list = names.ToList();
            if (list.Count == 0) { report.Add("Selection", "no changes", true, "No items selected."); return report; }
            EnsureBaseline();

            foreach (var n in list)
            {
                var svc = services.FirstOrDefault(s => s.Name == n);
                try
                {
                    if (svc is { IsProtected: true })
                    {
                        report.Add(n, "blocked", false, "Protected service — disabling may damage Windows.");
                        continue;
                    }
                    if (svc is { StartKind: ServiceStartKind.Disabled } && svc.RunningState == "Stopped")
                    {
                        report.Add(n, "already disabled", true);
                        continue;
                    }
                    ServiceActions.ChangeStartKind(n, ServiceStartKind.Disabled);
                    report.Add(n, "disabled", true);
                    if (svc?.RunningState == "Running")
                    {
                        try
                        {
                            ServiceActions.Stop(n);
                            report.Add(n, "stopped", true);
                        }
                        catch (Exception ex) { report.AddError(n, "stop service", ex); }
                    }
                }
                catch (Exception ex) { report.AddError(n, "disable service", ex); }
            }
            return report;
        }

        /// <summary>Enables services (sets start type) and optionally starts them.</summary>
        public ActionReport EnableServices(IEnumerable<string> names, ServiceStartKind targetStart, IEnumerable<ServiceInfo> services)
        {
            var report = new ActionReport();
            var list = names.ToList();
            if (list.Count == 0) { report.Add("Selection", "no changes", true, "No items selected."); return report; }

            foreach (var n in list)
            {
                var svc = services.FirstOrDefault(s => s.Name == n);
                try
                {
                    if (svc is { IsProtected: true })
                    {
                        report.Add(n, "blocked", false, "Protected service.");
                        continue;
                    }
                    ServiceActions.ChangeStartKind(n, targetStart);
                    report.Add(n, $"enabled ({targetStart})", true);
                    try
                    {
                        ServiceActions.Start(n);
                        report.Add(n, "started", true);
                    }
                    catch (Exception ex) { report.AddError(n, "start service", ex); }
                }
                catch (Exception ex) { report.AddError(n, "enable service", ex); }
            }
            return report;
        }

        /// <summary>Applies Game Profile: disables all services classified as "Recommended".</summary>
        public ActionReport ApplyGameProfile()
        {
            EnsureBaseline();
            var services = GetServices();
            var rec = services.Where(s => s.Risk == RiskLevel.Recommended && !s.IsProtected).Select(s => s.Name);
            return DisableServices(rec, services);
        }

        /// <summary>Restores initial system baseline (100% original state).</summary>
        public ActionReport RestoreBaseline()
        {
            EnsureBaseline();
            var currentServices = GetServices();
            var currentTasks = GetTasks();
            var currentStartups = GetStartups();
            return SnapshotService.Restore("baseline", currentServices, currentTasks, currentStartups);
        }

        public ActionReport ApplyTweak(string tweakId)
        {
            EnsureBaseline();
            if (!Enum.TryParse<TweakKind>(tweakId, out var kind))
            {
                var r = new ActionReport();
                r.Add(tweakId, "tweak", false, "Unknown tweak.");
                return r;
            }
            return TweakManager.Apply(kind);
        }

        public ActionReport RevertTweak(string tweakId)
        {
            EnsureBaseline();
            if (!Enum.TryParse<TweakKind>(tweakId, out var kind))
            {
                var r = new ActionReport();
                r.Add(tweakId, "tweak", false, "Unknown tweak.");
                return r;
            }
            return TweakManager.Revert(kind);
        }

        public List<ServiceInfo> GetServices() => _svcScanner.ScanAll();
        public List<ProcessInfo> GetProcesses() => new ProcessScanner().ScanAll();
        public List<ScheduledTaskInfo> GetTasks() => _taskScanner.ScanAll();
    }
}
