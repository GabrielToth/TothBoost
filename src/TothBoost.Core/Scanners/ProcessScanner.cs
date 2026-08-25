using System.Diagnostics;
using System.Runtime.InteropServices;
using TothBoost.Core.Models;

namespace TothBoost.Core.Scanners
{
    /// <summary>
    /// Lista todos os processos em execução (incluindo os do sistema),
    /// protegendo os processos críticos contra encerramento, e anexa métricas
    /// ao vivo: CPU %, uso de RAM, leitura/gravação de disco via APIs nativas do Windows.
    /// </summary>
    public class ProcessScanner
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

        private struct ProcessSample
        {
            public TimeSpan TotalProcessorTime;
            public ulong ReadBytes;
            public ulong WriteBytes;
            public DateTime Timestamp;
        }

        private static readonly object Sync = new();
        private static readonly Dictionary<int, ProcessSample> PrevSamples = new();
        private static readonly int ProcessorCount = Environment.ProcessorCount;

        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "smss.exe", "csrss.exe", "wininit.exe", "services.exe",
            "lsass.exe", "winlogon.exe", "svchost.exe", "fontdrvhost.exe", "Memory Compression",
            "dwmapi.exe", "spoolsv.exe", "sihost.exe", "explorer.exe", "ctfmon.exe",
            "taskhostw.exe", "RuntimeBroker.exe", "SecurityHealthService.exe",
            "MsMpEng.exe", "NisSrv.exe", "audiodg.exe", "conhost.exe"
        };

        public List<ProcessInfo> ScanAll()
        {
            var result = new List<ProcessInfo>();
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return result; }

            var now = DateTime.UtcNow;
            var currentSamples = new Dictionary<int, ProcessSample>();

            foreach (var p in procs)
            {
                try
                {
                    var pid = p.Id;
                    if (pid == 0) continue; // Pula processo Idle

                    var name = p.ProcessName;
                    long memMb = 0;
                    try { memMb = p.WorkingSet64 / (1024 * 1024); } catch { }

                    string path = "";
                    try { path = p.MainModule?.FileName ?? ""; } catch { }

                    string sessionName = "";
                    try { sessionName = p.SessionId.ToString(); } catch { }

                    double cpuPercent = -1;
                    double readKbs = -1;
                    double writeKbs = -1;

                    try
                    {
                        var cpuTime = p.TotalProcessorTime;
                        ulong readBytes = 0;
                        ulong writeBytes = 0;

                        try
                        {
                            if (GetProcessIoCounters(p.Handle, out var ioCounters))
                            {
                                readBytes = ioCounters.ReadTransferCount;
                                writeBytes = ioCounters.WriteTransferCount;
                            }
                        }
                        catch { }

                        currentSamples[pid] = new ProcessSample
                        {
                            TotalProcessorTime = cpuTime,
                            ReadBytes = readBytes,
                            WriteBytes = writeBytes,
                            Timestamp = now
                        };

                        lock (Sync)
                        {
                            if (PrevSamples.TryGetValue(pid, out var prev))
                            {
                                var timeDeltaSeconds = (now - prev.Timestamp).TotalSeconds;
                                if (timeDeltaSeconds > 0.2)
                                {
                                    var cpuDeltaSeconds = (cpuTime - prev.TotalProcessorTime).TotalSeconds;
                                    cpuPercent = Math.Clamp((cpuDeltaSeconds / (timeDeltaSeconds * ProcessorCount)) * 100.0, 0.0, 100.0);

                                    if (readBytes >= prev.ReadBytes)
                                        readKbs = ((readBytes - prev.ReadBytes) / 1024.0) / timeDeltaSeconds;

                                    if (writeBytes >= prev.WriteBytes)
                                        writeKbs = ((writeBytes - prev.WriteBytes) / 1024.0) / timeDeltaSeconds;
                                }
                            }
                        }
                    }
                    catch { }

                    var info = new ProcessInfo
                    {
                        Pid = pid,
                        Name = string.IsNullOrEmpty(name) ? "Desconhecido" : (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe"),
                        WorkingSetMb = memMb,
                        Path = path,
                        SessionName = sessionName,
                        CpuPercent = cpuPercent,
                        IoReadKbs = readKbs,
                        IoWriteKbs = writeKbs,
                        GpuPercent = -1
                    };

                    if (ProtectedNames.Contains(name) || ProtectedNames.Contains(info.Name))
                    {
                        info.Protected = true;
                        info.ProtectReason = "Processo crítico do Windows — não pode ser encerrado.";
                    }

                    result.Add(info);
                }
                catch { }
                finally { p.Dispose(); }
            }

            lock (Sync)
            {
                PrevSamples.Clear();
                foreach (var kvp in currentSamples)
                    PrevSamples[kvp.Key] = kvp.Value;
            }

            return result;
        }
    }
}
