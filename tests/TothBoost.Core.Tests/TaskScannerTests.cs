using TothBoost.Core.Scanners;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class TaskScannerTests
    {
        [Fact]
        public void ParseTasksXml_ValidXml_ReturnsTaskInfos()
        {
            var scanner = new TaskScanner();
            var xml = @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Tasks xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
    <Task>
        <TaskName>\Microsoft\Windows\TestTask</TaskName>
        <State>Ready</State>
        <Command>C:\Windows\System32\cmd.exe</Command>
    </Task>
</Tasks>";

            var tasks = scanner.ParseTasksXml(xml);
            Assert.NotNull(tasks);
            Assert.Single(tasks);
            Assert.Equal(@"\Microsoft\Windows\TestTask", tasks[0].Name);
            Assert.True(tasks[0].Enabled);
        }

        [Fact]
        public void ParseTasksXml_DisabledState_ReturnsDisabledTask()
        {
            var scanner = new TaskScanner();
            var xml = @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Tasks xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
    <Task>
        <TaskName>\Microsoft\Windows\DisabledTask</TaskName>
        <State>Disabled</State>
    </Task>
</Tasks>";

            var tasks = scanner.ParseTasksXml(xml);
            Assert.NotNull(tasks);
            Assert.Single(tasks);
            Assert.False(tasks[0].Enabled);
        }

        [Fact]
        public void ParseTasksXml_MalformedXml_ReturnsEmptyListWithoutCrashing()
        {
            var scanner = new TaskScanner();
            var xml = "Invalid XML content";

            var tasks = scanner.ParseTasksXml(xml);
            Assert.NotNull(tasks);
            Assert.Empty(tasks);
        }
    }
}
