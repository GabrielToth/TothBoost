using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TothBoost.Core.Scanners;
using Xunit;

namespace TothBoost.Core.Tests
{
    /// <summary>
    /// Live checks for the process metrics shown in the grid. They must be real
    /// measurements, never placeholder zeros.
    /// </summary>
    public class ProcessScannerLiveTests
    {
        [Fact]
        public void ScanAll_ReportsRamCpuAndGpuForRealProcesses()
        {
            var scanner = new ProcessScanner();
            var first = scanner.ScanAll();
            Assert.NotEmpty(first);

            // CPU is a delta between two scans; RAM and GPU are instantaneous.
            Thread.Sleep(600);
            var second = scanner.ScanAll();
            Assert.NotEmpty(second);

            var self = second.Single(p => p.Pid == Environment.ProcessId);
            Assert.True(self.WorkingSetMb > 0, "own process RAM should be > 0 MB");

            Assert.True(second.Any(p => p.CpuPercent > 0),
                "at least one process should report CPU usage after two samples");
            Assert.All(second, p => Assert.InRange(p.CpuPercent, -1, 100));
            Assert.All(second, p => Assert.InRange(p.GpuPercent, -1, 100));
        }

        [Fact]
        public void ScanAll_SerializesConcurrentScans()
        {
            var scanner = new ProcessScanner();
            var results = new System.Collections.Generic.List<Exception>();

            var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                try
                {
                    var rows = scanner.ScanAll();
                    Assert.NotEmpty(rows);
                }
                catch (Exception ex) { lock (results) results.Add(ex); }
            })).ToList();

            foreach (var t in threads) t.Start();
            foreach (var t in threads) Assert.True(t.Join(TimeSpan.FromSeconds(60)));
            Assert.Empty(results);
        }
    }
}
