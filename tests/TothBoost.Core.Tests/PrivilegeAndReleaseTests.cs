using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TothBoost.Core.Utilities;

namespace TothBoost.Core.Tests
{
    /// <summary>
    /// Guards the requirements that TothBoost always starts elevated and that the
    /// repository keeps a working CI/CD release pipeline. These are intentionally
    /// static checks so they run on any machine, elevated or not.
    /// </summary>
    public class PrivilegeAndReleaseTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TothBoost.slnx")))
            {
                dir = dir.Parent;
            }
            if (dir is null) throw new DirectoryNotFoundException("Repository root (TothBoost.slnx) not found.");
            return dir.FullName;
        }

        [Fact]
        public void DotnetAppManifest_RequiresAdministrator()
        {
            var manifestPath = Path.Combine(RepoRoot(), "src", "TothBoost.App", "app.manifest");
            Assert.True(File.Exists(manifestPath), manifestPath);

            var doc = XDocument.Load(manifestPath);
            XNamespace asm = "urn:schemas-microsoft-com:asm.v3";
            var level = doc.Descendants(asm + "requestedExecutionLevel")
                .Select(e => (string?)e.Attribute("level"))
                .FirstOrDefault();

            Assert.Equal("requireAdministrator", level);
        }

        [Fact]
        public void NativeApp_EnforcesAdministratorOnStartup()
        {
            var mainPath = Path.Combine(RepoRoot(), "src", "tothboost-rs", "src", "main.rs");
            var main = File.ReadAllText(mainPath);
            Assert.Contains("ElevationManager::enforce_administrator()", main);

            var elevationPath = Path.Combine(RepoRoot(), "src", "tothboost-rs", "src", "elevation.rs");
            var elevation = File.ReadAllText(elevationPath);
            Assert.Contains("w!(\"runas\")", elevation);
            Assert.Contains("TokenIsElevated", elevation);
        }

        [Fact]
        public void ReleaseWorkflow_BuildsTestsAndPublishesRelease()
        {
            var workflowPath = Path.Combine(RepoRoot(), ".github", "workflows", "ci-cd.yml");
            Assert.True(File.Exists(workflowPath), workflowPath);
            var yml = File.ReadAllText(workflowPath);

            Assert.Contains("dotnet test", yml);
            Assert.Contains("cargo test", yml);
            Assert.Contains("dotnet publish", yml);
            Assert.Contains("cargo build --release", yml);
            Assert.Contains("gh release create", yml);
            Assert.Contains("contents: write", yml);
            Assert.Contains("windows-latest", yml);
            Assert.Contains("build-windows", yml);
            Assert.Contains("actions/upload-artifact", yml);
        }

        [Fact]
        public void CurrentVersion_IsParseable()
        {
            var version = UpdateChecker.GetCurrentVersion();
            Assert.True(Version.TryParse(version, out _), $"Unparseable version: {version}");
        }
    }
}
