using TothBoost.Core.Snapshot;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class ProfileServiceTests
    {
        [Fact]
        public void ProfileService_CreateAndSelectProfile()
        {
            var testProfile = "TestProfile_" + Guid.NewGuid().ToString("N");
            ProfileService.Create(testProfile);

            var profiles = ProfileService.List();
            Assert.Contains(profiles, p => p.Name == testProfile);

            ProfileService.Select(testProfile);
            Assert.Equal(testProfile, ProfileService.CurrentName());
        }

        [Fact]
        public void ProfileService_SanitizeName_StripsInvalidChars()
        {
            var testProfile = "Profile/With\\Invalid:Chars*";
            var meta = ProfileService.Create(testProfile);
            Assert.NotNull(meta);
            Assert.False(meta.Name.Contains("/"));
        }

        [Fact]
        public void ProfileService_SaveAndLoadCapture()
        {
            var testProfile = "CaptureTest_" + Guid.NewGuid().ToString("N");
            var capture = new ProfileCapture
            {
                Name = testProfile,
                CapturedAt = DateTime.Now,
                Services = new List<ProfileServiceState>
                {
                    new ProfileServiceState { Name = "SysMain", StartKind = 4, Running = false }
                }
            };

            ProfileService.SaveCapture(testProfile, capture);
            var loaded = ProfileService.LoadCapture(testProfile);

            Assert.NotNull(loaded);
            Assert.Equal(testProfile, loaded.Name);
            Assert.Single(loaded.Services);
            Assert.Equal("SysMain", loaded.Services[0].Name);
        }
    }
}
