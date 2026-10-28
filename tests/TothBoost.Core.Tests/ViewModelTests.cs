using System.Windows.Media;
using TothBoost.App;
using TothBoost.Core.Models;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class ViewModelTests
    {
        [Fact]
        public void SvcRow_ExposesCorrectProperties()
        {
            var info = new ServiceInfo
            {
                Name = "TestSvc",
                DisplayName = "Test Service",
                Description = "A test service",
                StartKind = ServiceStartKind.Automatic,
                TypeKind = ServiceTypeKind.OwnProcess,
                RunningState = "Running",
                Risk = RiskLevel.Recommended
            };

            var row = new SvcRow(info);
            Assert.Equal("TestSvc", row.Name);
            Assert.Equal("Test Service", row.DisplayName);
            Assert.Equal("Automatic", row.StartKindLabel);
            Assert.Equal("Running", row.RunningState);
            Assert.True(row.IsOn);
            Assert.True(row.CanTouch);
            Assert.False(row.IsProtected);
        }

        [Fact]
        public void ProcRow_UpdatesPropertiesCorrectly()
        {
            var proc1 = new ProcessInfo
            {
                Pid = 1234,
                Name = "game.exe",
                WorkingSetMb = 512,
                CpuPercent = 15.5,
                GpuPercent = 40.0,
                IoReadKbs = 1024,
                IoWriteKbs = 2048,
                Protected = false
            };

            var row = new ProcRow(proc1);
            Assert.Equal(1234, row.Pid);
            Assert.Equal("game.exe", row.Name);
            Assert.Equal(512, row.MemMb);
            Assert.Contains("15", row.CpuText);
            Assert.Equal("40%", row.GpuText);
            Assert.False(row.IsProtected);

            var proc2 = new ProcessInfo
            {
                Pid = 1234,
                Name = "game.exe",
                WorkingSetMb = 1024,
                CpuPercent = 30.0,
                GpuPercent = 80.0,
                IoReadKbs = 500,
                IoWriteKbs = 100,
                Protected = false
            };

            row.Update(proc2);
            Assert.Equal(1024, row.MemMb);
            Assert.Contains("30", row.CpuText);
            Assert.Equal("80%", row.GpuText);
        }

        [Fact]
        public void ProcRow_ExposesNumericSortValues()
        {
            var row = new ProcRow(new ProcessInfo
            {
                Pid = 7,
                Name = "sort.exe",
                WorkingSetMb = 12.5,
                CpuPercent = 3.25,
                GpuPercent = 17.5,
                IoReadKbs = 512,
                IoWriteKbs = 2048
            });

            Assert.Equal(3.25, row.CpuValue);
            Assert.Equal(17.5, row.GpuValue);
            Assert.Equal(512, row.ReadKbs);
            Assert.Equal(2048, row.WriteKbs);
            Assert.Equal(12.5, row.MemMb);
            Assert.Equal(12.5.ToString("0.0"), row.MemText);
        }

        [Fact]
        public void ProcRow_SubMegabyteMemory_IsNotRoundedToZero()
        {
            var row = new ProcRow(new ProcessInfo
            {
                Pid = 9,
                Name = "tiny.exe",
                WorkingSetMb = 0.4
            });

            Assert.Equal(0.4, row.MemMb);
            Assert.NotEqual("0", row.MemText);
            Assert.Equal("—", row.CpuText); // CPU unavailable on the first sample
            Assert.Equal("—", row.GpuText); // GPU counters unavailable
        }
    }
}
