using System;
using System.IO;
using TothBoost.Core;
using TothBoost.Core.Known;
using TothBoost.Core.Models;
using TothBoost.Core.Utilities;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class NewUtilitiesTests
    {
        [Fact]
        public void JunkCleaner_Scan_ReturnsValidCategories()
        {
            var categories = JunkCleaner.Scan();
            Assert.NotEmpty(categories);
            Assert.Contains(categories, c => c.Id == "UserTemp");
        }

        [Fact]
        public void JunkCleaner_Clean_ExecutesWithoutException()
        {
            var dummyCatId = "UserTemp";
            var result = JunkCleaner.Clean(new[] { dummyCatId });
            Assert.NotNull(result);
            Assert.True(result.TotalFilesDeleted >= 0);
            Assert.True(result.TotalBytesFreed >= 0);
        }

        [Fact]
        public void MemoryOptimizer_TrimWorkingSet_ExecutesWithoutCrashing()
        {
            var result = MemoryOptimizer.TrimWorkingSet();
            Assert.NotNull(result);
            Assert.True(result.ProcessesTrimmed >= 0);
            Assert.True(result.BytesFreed >= 0);
        }

        [Fact]
        public void NetworkOptimizer_FlushDns_ReturnsResultObject()
        {
            var result = NetworkOptimizer.FlushDns();
            Assert.NotNull(result);
            Assert.NotNull(result.Message);
        }

        [Fact]
        public void TweakCatalog_ContainsNetworkThrottlingTweak()
        {
            var all = TweakCatalog.All();
            Assert.Contains(all, t => t.Id == nameof(TweakKind.NetworkThrottling));
        }

        [Fact]
        public void BoostEngine_ExposesNewUtilityMethods()
        {
            var engine = new BoostEngine();
            var scan = engine.ScanJunkFiles();
            Assert.NotEmpty(scan);

            var memRes = engine.TrimMemory();
            Assert.NotNull(memRes);

            var dnsRes = engine.FlushDns();
            Assert.NotNull(dnsRes);
        }
    }
}
