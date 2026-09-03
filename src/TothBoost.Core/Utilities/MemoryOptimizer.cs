using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TothBoost.Core.Utilities
{
    public class MemoryTrimResult
    {
        public int ProcessesTrimmed { get; set; }
        public long BytesFreed { get; set; }
        public double MbFreed => Math.Round(BytesFreed / (1024.0 * 1024.0), 2);
    }

    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static MemoryTrimResult TrimWorkingSet()
        {
            var result = new MemoryTrimResult();
            var processes = Process.GetProcesses();

            foreach (var proc in processes)
            {
                try
                {
                    if (proc.HasExited) continue;

                    long beforeMem = proc.WorkingSet64;
                    if (beforeMem < 10 * 1024 * 1024) continue; // Skip processes using < 10 MB

                    if (EmptyWorkingSet(proc.Handle))
                    {
                        proc.Refresh();
                        long afterMem = proc.WorkingSet64;
                        long freed = beforeMem - afterMem;
                        if (freed > 0)
                        {
                            result.BytesFreed += freed;
                            result.ProcessesTrimmed++;
                        }
                    }
                }
                catch
                {
                    // Access denied for protected system processes or process terminated during loop
                }
                finally
                {
                    proc.Dispose();
                }
            }

            return result;
        }
    }
}
