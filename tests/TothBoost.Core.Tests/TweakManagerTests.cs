using TothBoost.Core.Known;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class TweakManagerTests
    {
        [Fact]
        public void TweakCatalog_All_ReturnsValidCatalog()
        {
            var tweaks = TweakCatalog.All();
            Assert.NotNull(tweaks);
            Assert.NotEmpty(tweaks);
            Assert.All(tweaks, t =>
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Id));
                Assert.False(string.IsNullOrWhiteSpace(t.Name));
                Assert.False(string.IsNullOrWhiteSpace(t.Description));
            });
        }

        [Fact]
        public void TweakCatalog_ContainsKnownTweaks()
        {
            var tweaks = TweakCatalog.All();
            Assert.Contains(tweaks, t => t.Id == "GameMode");
            Assert.Contains(tweaks, t => t.Id == "HighPerformance");
            Assert.Contains(tweaks, t => t.Id == "Transparency");
            Assert.Contains(tweaks, t => t.Id == "Animations");
        }
    }
}
