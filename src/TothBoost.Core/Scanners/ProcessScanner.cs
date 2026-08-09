using System.Diagnostics;
using TothBoost.Core.Models;

namespace TothBoost.Core.Scanners
{
    /// <summary>
    /// Lista todos os processos em execução (incluindo os do sistema),
    /// protegendo os processos críticos contra encerramento.
    /// </summary>
    public class ProcessScanner
    {
        private static readonly HashSet<string> ProtectedDebug = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "System Idle Process", "Registry", "Memory Compression",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "fontdrvhost", "dwm", "lsm", "win32k", "securekernel"
        };

        public List<ProcessInfo> ScanAll()
        {
            var result = new List<ProcessInfo>();
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return result; }

            foreach (var p in procs)
            {
                try
                {
                    string name = p.ProcessName.ToString() == "" ? "?" : p.ProcessName;
                    string? path = null;
                    try { if (p.MainModule is { } m) path = m.FileName; } catch { /* ACL */ }

                    var isService = path is not null && path.IndexOf("\\Windows\\system32\\", StringComparison.OrdinalIgnoreCase) >= 0;

                    var info = new ProcessInfo
                    {
                        Pid = p.Id,
                        Name = name,
                        WorkingSetMb = p.WorkingSet64 > 0 ? p.WorkingSet64 / (1024 * 1024) : 0,
                        Path = path,
                        SessionName = p.SessionId == 0 ? "Sistema" : "Console",
                        IsService = isService
                    };
                    if (ProtectedNames.Contains(name))
                    {
                        info.Protected = true;
                        info.ProtectReason = "Processo crítico do Windows — encerrá-lo pode causar travamento ou tela azul (BSOD).";
                    }
                    result.Add(info);
                }
                catch { /* processo sumiu durante iteração */ }
                finally { p.Dispose(); }
            }
            return result;
        }

        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "System Idle Process", "Registry", "Memory Compression",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "fontdrvhost", "dwm", "csrss"
        };
    }
}