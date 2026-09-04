using System;
using TothBoost.Core.Utilities;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class ActionReportTests
    {
        [Fact]
        public void ActionReport_AggregatesCountsAndErrorsCorrectly()
        {
            var report = new ActionReport();
            Assert.False(report.HasErrors);
            Assert.Equal(0, report.SuccessCount);
            Assert.Equal(0, report.ErrorCount);

            report.Add("Svc1", "disabled", true, "OK");
            report.Add("Svc2", "disabled", false, "Access Denied");
            report.AddError("Svc3", "stop", new InvalidOperationException("Service failed"));

            Assert.True(report.HasErrors);
            Assert.Equal(1, report.SuccessCount);
            Assert.Equal(2, report.ErrorCount);
            Assert.Equal(3, report.Items.Count);
        }

        [Fact]
        public void ActionReport_Merge_CombinesReports()
        {
            var r1 = new ActionReport();
            r1.Add("Item1", "enable", true);

            var r2 = new ActionReport();
            r2.Add("Item2", "disable", true);

            r1.Merge(r2);

            Assert.Equal(2, r1.Items.Count);
            Assert.Equal(2, r1.SuccessCount);
        }

        [Fact]
        public void Proc_Run_ExecutesSimpleCommand()
        {
            var (exitCode, output) = Proc.Run("cmd.exe", "/c echo TothBoostTest");
            Assert.Equal(0, exitCode);
            Assert.Contains("TothBoostTest", output);
        }

        [Fact]
        public void Elevation_Check_DoesNotThrow()
        {
            // Just verify the property evaluates cleanly without exception
            bool isElevated = Elevation.IsElevated;
            Assert.True(isElevated || !isElevated);
        }
    }
}
