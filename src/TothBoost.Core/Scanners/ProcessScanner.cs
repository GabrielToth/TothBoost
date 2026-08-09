using System.Diagnostics;
using System.Runtime.InteropServices;
using TothBoost.Core.Models;

namespace TothBoost.Core.Scanners
{
    /// <summary>
    /// Lista todos os processos em execução (incluindo os do sistema),
    /// protegendo os processos críticos contra encerramento, e anexa métricas
    /// ao vivo: CPU %, uso de RAM, leitura/gravação de disco e uso de GPU.
    /// </summary>
    public class ProcessScanner
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        private const int SystemProcessInformation = 5;

        // Estados anteriores para cálculo de delta (CPU e I/O)
        private static readonly object Sync = new();
        private static readonly Dictionary<int, Snapshot> Prev = new();
        private static DateTime PrevAt = DateTime.MinValue;

        private struct Snapshot
        {
            public ulong KernelTicks;
            public ulong UserTicks;
            public ulong ReadBytes;
            public ulong WriteBytes;
        }

        /// <summary>Leituras de GPU por PID (categoria "GPU Engine" do Windows).</summary>
        private sealed class GpuSampler
        {
            private readonly Dictionary<string, PerformanceCounter> _counters = new();

            public Dictionary<int, double> Sample()
            {
                var result = new Dictionary<int, double>();
                try
                {
                    if (!PerformanceCounterCategory.Exists("GPU Engine")) return result;

                    var gpuNames = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
                    var byPid = new Dictionary<int, List<string>>();
                    foreach (var name in gpuNames)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(name, @"pid_(\d+)_");
                        if (!m.Success) continue;
                        var pid = int.Parse(m.Groups[1].Value);
                        if (!byPid.TryGetValue(pid, out var l)) byPid[pid] = l = new List<string>();
                        l.Add(name);
                    }

                    foreach (var (pid, instances) in byPid)
                    {
                        double max = 0;
                        foreach (var inst in instances)
                        {
                            if (!_counters.TryGetValue(inst, out var pc))
                            {
                                pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                                _counters[inst] = pc;
                            }
                            try { max = Math.Max(max, pc.NextValue()); } catch { }
                        }
                        if (max > 0) result[pid] = max;
                    }

                    // limpa instâncias que deixaram de existir (processos mortos)
                    var live = new HashSet<string>(gpuNames);
                    var dead = _counters.Keys.Where(k => !live.Contains(k)).ToList();
                    foreach (var k in dead) { _counters[k].Dispose(); _counters.Remove(k); }
                }
                catch { }
                return result;
            }
        }

        private static readonly Lazy<GpuSampler> Gpu = new(() => new GpuSampler());

        public List<ProcessInfo> ScanAll()
        {
            var result = new List<ProcessInfo>();
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return result; }

            var metrics = ReadNativeMetrics();          // CPU/RAM/IO por PID (delta)
            var gpu = Gpu.Value.Sample();               // % GPU por PID
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in procs)
            {
                try
                {
                    string name = p.ProcessName.ToString() == "" ? "?" : p.ProcessName;
                    string? path = null;
                    try { if (p.MainModule is { } m) path = m.FileName; } catch { /* ACL */ }

                    var isService = path is not null && path.IndexOf("\\Windows\\system32\\", StringComparison.OrdinalIgnoreCase) >= 0;
                    long ramMb = p.WorkingSet64 > 0 ? p.WorkingSet64 / (1024 * 1024) : 0;

                    var info = new ProcessInfo
                    {
                        Pid = p.Id,
                        Name = name,
                        WorkingSetMb = ramMb,
                        Path = path,
                        SessionName = p.SessionId == 0 ? "Sistema" : "Console",
                        IsService = isService
                    };

                    if (metrics.TryGetValue(p.Id, out var mx))
                    {
                        info.CpuPercent = mx.CpuPercent;
                        info.IoReadKbs = mx.ReadKbs;
                        info.IoWriteKbs = mx.WriteKbs;
                    }
                    if (gpu.TryGetValue(p.Id, out var gp)) info.GpuPercent = gp;

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

        /// <summary>
        /// Métricas delta via NtQuerySystemInformation (uma única chamada nativa):
        /// CPU % (kernel+usuário), leitura/gravação de disco em KB/s.
        /// </summary>
        private static Dictionary<int, (long RamMb, double CpuPercent, double ReadKbs, double WriteKbs)> ReadNativeMetrics()
        {
            var dict = new Dictionary<int, (long, double, double, double)>();
            try
            {
                int len = 1 << 20;
                var buf = Marshal.AllocHGlobal(len);
                try
                {
                    var status = NtQuerySystemInformation(SystemProcessInformation, buf, len, out _);
                    if (status != 0) return dict;

                    // offsets x64 do SYSTEM_PROCESS_INFORMATION
                    const int oWorkingSet = 0x60;
                    const int oKernelTime = 0xB8;
                    const int oUserTime = 0xC0;
                    const int oReadTransfer = 0xA0;
                    const int oWriteTransfer = 0xA8;

                    var now = DateTime.UtcNow;
                    var elapsed = (PrevAt == DateTime.MinValue) ? 0.0 : (now - PrevAt).TotalSeconds;
                    lock (Sync)
                    {
                        var p = buf;
                        while (true)
                        {
                            var next = Marshal.ReadInt32(p);
                            var pidOffset = Marshal.ReadIntPtr(p, 0x18).ToInt64();
                            if (pidOffset > 0)
                            {
                                var kern = (ulong)Marshal.ReadInt64(p, oKernelTime);
                                var user = (ulong)Marshal.ReadInt64(p, oUserTime);
                                var read = (ulong)Marshal.ReadInt64(p, oReadTransfer);
                                var write = (ulong)Marshal.ReadInt64(p, oWriteTransfer);
                                var ws = Marshal.ReadInt64(p, oWorkingSet);

                                double cpu = 0, rk = 0, wk = 0;
                                if (Prev.TryGetValue((int)pidOffset, out var prev) && elapsed > 0.3)
                                {
                                    var dCpu = (kern + user) - (prev.KernelTicks + prev.UserTicks);
                                    var cores = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 1;
                                    cpu = dCpu / (elapsed * 10_000_000.0) * 100.0 / cores;
                                    cpu = Math.Min(100.0, Math.Max(0.0, cpu));
                                    rk = ((read - prev.ReadBytes) / 1024.0) / elapsed;
                                    wk = ((write - prev.WriteBytes) / 1024.0) / elapsed;
                                }
                                Prev[(int)pidOffset] = new Snapshot { KernelTicks = kern, UserTicks = user, ReadBytes = read, WriteBytes = write };
                                dict.Add((int)pidOffset, (ws / (1024 * 1024), cpu, rk, wk));
                            }
                            if (next == 0) break;
                            p += next;
                        }
                        PrevAt = now;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            return dict;
        }

        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "System Idle Process", "Registry", "Memory Compression",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass",
            "svchost", "fontdrvhost", "dwm", "csrss"
        };
    }
}