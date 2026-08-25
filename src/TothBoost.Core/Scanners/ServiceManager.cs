using Microsoft.Win32;
using System.ServiceProcess;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Scanners
{
    /// <summary>
    /// Lista TODOS os serviços e drivers registrados no Windows a partir do registro
    /// (HKLM\SYSTEM\CurrentControlSet\Services), cruzando com o estado atual
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
                        Group = group ?? "",
                        IsDriverProtectedType = driverProtected
                    };
                    list.Add(item);
                }
                catch { /* chave sem permissão de leitura — ignorar */ }
            }
            root.Dispose();
            return list;
        }

        private static string? GetRunningState(string name)
        {
            try
            {
                using var sc = new ServiceController(name);
                sc.Refresh();
                return sc.Status switch
                {
                    ServiceControllerStatus.Running => "Executando",
                    ServiceControllerStatus.Stopped => "Parado",
                    ServiceControllerStatus.StartPending => "Iniciando...",
                    ServiceControllerStatus.StopPending => "Parando...",
                    ServiceControllerStatus.Paused => "Pausado",
                    ServiceControllerStatus.ContinuePending => "Retomando...",
                    _ => "Desconhecido"
                };
            }
            catch { return "—"; }
        }

        private static int GetInt(RegistryKey k, string name, int dflt)
        {
            try { return (int)(k.GetValue(name, dflt) ?? dflt); }
            catch { return dflt; }
        }

        private static string? GetString(RegistryKey k, string name)
        {
            try { return k.GetValue(name)?.ToString(); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Executa operações sobre serviços via sc.exe (config de inicialização) e
    /// ServiceController (start/stop).
    /// </summary>
    public static class ServiceActions
    {
        public static void ChangeStartKind(string name, ServiceStartKind kind)
        {
            var scArg = kind switch
            {
                ServiceStartKind.Boot => "boot",
                ServiceStartKind.System => "system",
                ServiceStartKind.Automatic => "auto",
                ServiceStartKind.Manual => "demand",
                _ => "disabled"
            };
            var (exit, output) = Proc.Run("sc.exe", $"config \"{name}\" start= {scArg}");
            if (exit != 0)
                throw new InvalidOperationException($"sc config falhou (código {exit}): {output}");
            // Confirma no registro (fonte da verdade do Windows).
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(RegistryKeys.SvcRoot + "\\" + name, true);
                k?.SetValue("Start", (int)kind, RegistryValueKind.DWord);
            }
            catch { /* sc já aplicou */ }
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
            if (exit != 0 && !outStr.Contains("1056"))
                throw new InvalidOperationException($"{name}: {outStr.Trim()}");
        }
    }
}