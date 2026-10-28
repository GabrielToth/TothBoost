using System.Collections;
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
        private static readonly object ScanLock = new();
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
            // A manual refresh can overlap the 2-second timer. Serialize scans so
            // CPU deltas are always measured against the previous complete scan.
            lock (ScanLock)
            {
                return ScanAllCore();
            }
        }

        /// <summary>
        /// Reads per-process GPU utilization from the Windows "GPU Engine"
        /// performance counters (Windows 10 1709+). Returns false when the
        /// counters are unavailable so the UI can show "—" instead of a fake 0%.
        /// </summary>
        private static (bool Available, Dictionary<int, double> ByPid) ReadGpuPercent()
        {
            var byPid = new Dictionary<int, double>();
            try
            {
                var category = new PerformanceCounterCategory("GPU Engine");
                var data = category.ReadCategory();
                if (data["Utilization Percentage"] is not InstanceDataCollection utilization)
                    return (false, byPid);

                foreach (string instance in utilization.Keys)
                {
                    var pid = ParseGpuInstancePid(instance);
                    if (pid <= 0) continue;

                    var raw = CounterSample.Calculate(utilization[instance].Sample);
                    if (double.IsNaN(raw) || raw < 0) continue;
                    var value = Math.Clamp(raw, 0.0, 100.0);
                    if (byPid.TryGetValue(pid, out var current))
                        byPid[pid] = Math.Min(100.0, current + value);
                    else
                        byPid[pid] = value;
                }

                return (true, byPid);
            }
            catch
            {
                return (false, byPid);
            }
        }

        private static int ParseGpuInstancePid(string instance)
        {
            const string prefix = "pid_";
            if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return 0;

            var end = instance.IndexOf('_', prefix.Length);
            if (end < 0) return 0;

            return int.TryParse(instance.AsSpan(prefix.Length, end - prefix.Length), out var pid)
                ? pid
                : 0;
        }

        private static List<ProcessInfo> ScanAllCore()
        {
            var (gpuAvailable, gpuByPid) = ReadGpuPercent();
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
                    double memMb = 0;
                    try
                    {
                        // Keep one decimal place: sub-1 MB processes would display
                        // as "0" with integer division, which looked like a bug.
                        memMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1);
                    }
                    catch
                    {
                        try { memMb = Math.Round(p.PrivateMemorySize64 / (1024.0 * 1024.0), 1); } catch { }
                    }

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
                        GpuPercent = gpuAvailable
                            ? (gpuByPid.TryGetValue(pid, out var gpu) ? Math.Round(gpu, 1) : 0.0)
                            : -1
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
