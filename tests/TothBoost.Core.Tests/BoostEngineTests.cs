using TothBoost.Core;
using TothBoost.Core.Models;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class BoostEngineTests
    {
        [Fact]
        public void BoostEngine_GetServices_ReturnsList()
        {
            var engine = new BoostEngine();
            var services = engine.GetServices();
            Assert.NotNull(services);
            // Every service should have non-empty Name
            Assert.All(services, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        }

        [Fact]
        public void BoostEngine_GetProcesses_ReturnsList()
        {
            var engine = new BoostEngine();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var procs = engine.GetProcesses();
            sw.Stop();
            // Log duration
            System.Console.WriteLine($"GetProcesses took: {sw.ElapsedMilliseconds} ms");
            Assert.NotNull(procs);
            Assert.NotEmpty(procs);
        }

        [Fact]
        public void BoostEngine_GetStartups_ReturnsList()
        {
            var engine = new BoostEngine();
            var startups = engine.GetStartups();
            Assert.NotNull(startups);
        }

        [Fact]
        public void BoostEngine_GetTweaks_ReturnsAllTweaks()
        {
            var engine = new BoostEngine();
            var tweaks = engine.GetTweaks();
            Assert.NotNull(tweaks);
            Assert.NotEmpty(tweaks);
            Assert.Contains(tweaks, t => t.Id == "HighPerformance");
        }

        [Fact]
        public void BoostEngine_DisableServices_ProtectedService_Blocked()
        {
            var engine = new BoostEngine();
            var protectedSvc = new ServiceInfo
            {
                Name = "ProtectedTestSvc",
                Risk = RiskLevel.Protected
            };

            var report = engine.DisableServices(new[] { "ProtectedTestSvc" }, new[] { protectedSvc });
            Assert.NotNull(report);
            Assert.True(report.HasErrors);
            Assert.Contains(report.Items, i => i.Action == "blocked");
        }
    }
}
