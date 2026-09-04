using System.Threading.Tasks;
using TothBoost.Core.Utilities;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class UpdateCheckerTests
    {
        [Fact]
        public void UpdateChecker_GetCurrentVersion_ReturnsValidVersionString()
        {
            string ver = UpdateChecker.GetCurrentVersion();
            Assert.NotNull(ver);
            Assert.NotEmpty(ver);
        }

        [Fact]
        public async Task UpdateChecker_CheckForUpdatesAsync_ExecutesWithoutException()
        {
            var res = await UpdateChecker.CheckForUpdatesAsync();
            Assert.NotNull(res);
            Assert.NotNull(res.CurrentVersion);
        }

        [Fact]
        public void ShortcutManager_CreateStartMenuShortcut_ExecutesWithoutCrashing()
        {
            ShortcutManager.CreateStartMenuShortcut();
        }
    }
}
