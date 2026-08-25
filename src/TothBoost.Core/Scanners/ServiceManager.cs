using Microsoft.Win32;
using System.ServiceProcess;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Scanners
{
    /// <summary>
    /// Scans ALL services and drivers registered in Windows Registry
    /// (HKLM\SYSTEM\CurrentControlSet\Services), cross-referencing live state
    /// via ServiceController.
    /// </summary>
    public class ServiceScanner
    {
        public List<ServiceInfo> ScanAll()
        {
            var list = new List<ServiceInfo>();
            RegistryKey? root = null;
            try { root = Registry.LocalMachine.OpenSubKey(RegistryKeys.SvcRoot); }
            catch { return list; }
            if (root is null) return list;

            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(name, false);
                    if (k is null) continue;

                    var type = (ServiceTypeKind)GetInt(k, "Type", -1);
                    var start = (ServiceStartKind)GetInt(k, "Start", 3);
                    var image = GetString(k, "ImagePath");
                    var displayName = GetString(k, "DisplayName");
                    var description = GetString(k, "Description");
                    var group = GetString(k, "Group");

                    var driverProtected = type is ServiceTypeKind.KernelDriver or ServiceTypeKind.FileSystemDriver;

                    var item = new ServiceInfo
                    {
                        Name = name,
                        DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName,
                        Description = description ?? "",
                        ImagePath = image,
                        TypeKind = type,
                        StartKind = start,
                        ConfiguredStart = start,
                        RunningState = driverProtected ? null : GetRunningState(name),
                        Group = group ?? ""
                    };

                    Known.KnowledgeBase.Classify(item);
                    list.Add(item);
                }
                catch { }
            }
            return list;
        }

        private static string GetRunningState(string name)
        {
            try
            {
                using var sc = new ServiceController(name);
                return sc.Status switch
                {
                    ServiceControllerStatus.Running => "Running",
                    ServiceControllerStatus.Stopped => "Stopped",
                    ServiceControllerStatus.StartPending => "Starting",
                    ServiceControllerStatus.StopPending => "Stopping",
                    ServiceControllerStatus.Paused => "Paused",
                    _ => sc.Status.ToString()
                };
            }
            catch
            {
                return "Unknown";
            }
        }

        private static int GetInt(RegistryKey k, string name, int def) => k.GetValue(name) is int i ? i : def;
        private static string? GetString(RegistryKey k, string name) => k.GetValue(name)?.ToString();
    }

    public static class ServiceActions
    {
        public static void ChangeStartKind(string name, ServiceStartKind kind)
        {
            var configStr = kind switch
            {
                ServiceStartKind.Automatic => "auto",
                ServiceStartKind.Manual => "demand",
                ServiceStartKind.Disabled => "disabled",
                _ => throw new ArgumentException("Unsupported startup type.", nameof(kind))
            };
            var (exit, outStr) = Proc.Sc("config", name, "start=", configStr);
            if (exit != 0) throw new InvalidOperationException($"{name}: {outStr.Trim()}");
        }

        public static void Stop(string name)
        {
            var (exit, outStr) = Proc.Sc("stop", name);
            if (exit != 0 && !outStr.Contains("1066") && !outStr.Contains("1062"))
                throw new InvalidOperationException($"{name}: {outStr.Trim()}");
        }

        public static void Start(string name)
        {
            var (exit, outStr) = Proc.Sc("start", name);
            if (exit != 0 && !outStr.Contains("1056")) // 1056 = already running
                throw new InvalidOperationException($"{name}: {outStr.Trim()}");
        }
    }
}
