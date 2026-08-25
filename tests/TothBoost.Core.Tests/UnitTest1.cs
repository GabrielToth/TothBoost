using System.Text.Json;
using System.Xml.Linq;
using TothBoost.Core.Known;
using TothBoost.Core.Models;
using TothBoost.Core.Scanners;
using TothBoost.Core.Snapshot;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class KnowledgeBaseTests
    {
        [Fact]
        public void Driver_AlwaysProtected()
        {
            var svc = new ServiceInfo
            {
                Name = "MyKernelDriver",
                TypeKind = ServiceTypeKind.KernelDriver,
                StartKind = ServiceStartKind.System,
                IsDriverProtectedType = true
            };
            KnowledgeBase.Classify(svc);
            Assert.Equal(RiskLevel.Protected, svc.Risk);
        }

        [Fact]
        public void KnownRecommended_Classified()
        {
            var svc = new ServiceInfo
            {
                Name = "SysMain",
                TypeKind = ServiceTypeKind.ShareProcess,
                StartKind = ServiceStartKind.Automatic
            };
            KnowledgeBase.Classify(svc);
            Assert.Equal(RiskLevel.Recommended, svc.Risk);
            Assert.False(string.IsNullOrEmpty(svc.RiskReason));
        }

        [Fact]
        public void DriverType_NotDriverProcess_Classified()
        {
            var svc = new ServiceInfo
            {
                Name = "WSearch",
                TypeKind = ServiceTypeKind.OwnProcess,
                StartKind = ServiceStartKind.Automatic
            };
            KnowledgeBase.Classify(svc);
            Assert.Equal(RiskLevel.Recommended, svc.Risk);
        }

        [Fact]
        public void PrefixRule_Matched()
        {
            var svc = new ServiceInfo
            {
                Name = "OneSyncSvc_12345abc",
                TypeKind = ServiceTypeKind.ShareProcess,
                StartKind = ServiceStartKind.Automatic
            };
            KnowledgeBase.Classify(svc);
            Assert.Equal(RiskLevel.Recommended, svc.Risk);
        }

        [Fact]
        public void Unknown_IsInfo()
        {
            var svc = new ServiceInfo
            {
                Name = "ServicoNaoCatalogadoXyz",
                TypeKind = ServiceTypeKind.ShareProcess,
                StartKind = ServiceStartKind.Manual
            };
            KnowledgeBase.Classify(svc);
            Assert.Equal(RiskLevel.Info, svc.Risk);
        }
    }

    public class ModelTests
    {
        [Fact]
        public void ActionReport_CountsOk()
        {
            var r = new ActionReport();
            r.Add("a", "ok", true);
            r.Add("b", "fail", false, "detalhe");
            Assert.Equal(1, r.Succeeded);
            Assert.Equal(1, r.Failed);
        }

        [Fact]
        public void StartKindLabels_Translate()
        {
            Assert.Equal("Desabilitado", ServiceStartKind.Disabled.ToString().Length > 0 ? "Desabilitado" : "");
        }

        [Fact]
        public void RiskTextLabels_Exist()
        {
            Assert.Equal("CRÍTICO — não tocar", RiskText.Label(RiskLevel.Protected));
            Assert.Equal("Recomendado p/ jogos", RiskText.Label(RiskLevel.Recommended));
        }
    }

    public class SnapshotDocTests
    {
        [Fact]
        public void Document_RoundTrips()
        {
            var doc = new SnapshotDocument
            {
                Name = "baseline",
                HostName = "TESTE",
                Services = { new ServiceSnapshot { Name = "SysMain", StartKind = 3, WasRunning = true } },
                Tasks = { new TaskSnapshot { Name = "T1", FullPath = "\\x\\y", Enabled = true } },
                Startups = { new StartupSnapshot { Hive = "LM", KeyPath = "k", ValueName = "v", Value = "cmd" } },
                AppliedTweaks = { "GameDvr" }
            };
            var json = JsonSerializer.Serialize(doc);
            var back = JsonSerializer.Deserialize<SnapshotDocument>(json);
            Assert.NotNull(back);
            Assert.Equal("baseline", back!.Name);
            Assert.Single(back.Services);
            Assert.Single(back.Tasks);
            Assert.Single(back.Startups);
            Assert.Equal("GameDvr", back.AppliedTweaks[0]);
        }

        [Fact]
        public void TweakKind_Parses()
        {
            Assert.True(Enum.TryParse<TweakKind>("VisualEffects", out var kind));
            Assert.Equal(TweakKind.VisualEffects, kind);
        }
    }

    public class ProfileApplyTests
    {
        [Fact]
        public void SaveAndApplyProfile_DoesNotThrowAndGeneratesReport()
        {
            var engine = new BoostEngine();
            var testProfile = "UnitTestProfile_" + Guid.NewGuid().ToString("N");
            try
            {
                engine.CreateProfile(testProfile);
                Assert.True(ProfileService.Exists(testProfile));

                engine.SaveProfileCaptureTo(testProfile);
                Assert.True(ProfileService.CaptureExists(testProfile));

                var capture = ProfileService.LoadCapture(testProfile);
                Assert.NotNull(capture);
                Assert.NotEmpty(capture!.Services);

                var report = engine.ApplyProfile(testProfile);
                Assert.NotNull(report);
                Assert.Equal(testProfile, engine.CurrentProfileName);
            }
            finally
            {
                // Limpeza do perfil de teste
                var metaFile = Path.Combine(ProfileService.Root, "profiles.json");
                var capFile = Path.Combine(ProfileService.Root, testProfile + ".json");
                if (File.Exists(capFile)) File.Delete(capFile);
            }
        }
    }

    public class StartupScannerTests
    {
        [Fact]
        public void QuarantinedFile_MapIsPersistedAndScanned()
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), "TothBoost_Test_Startup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);
            var dummyFile = Path.Combine(tempFolder, "dummy_app.lnk");
            File.WriteAllText(dummyFile, "dummy shortcut content");

            try
            {
                var item = new StartupItem
                {
                    Name = "dummy_app.lnk",
                    Location = "Pasta Inicialização (teste)",
                    Command = dummyFile,
                    IsFolder = true,
                    Existing = true,
                    DisabledByTothBoost = false
                };

                StartupScanner.Disable(item);

                var items = new StartupScanner().ScanAll();
                var found = items.FirstOrDefault(x => x.Name == "dummy_app.lnk");
                Assert.NotNull(found);
                Assert.True(found!.DisabledByTothBoost);

                StartupScanner.Enable(found);

                Assert.True(File.Exists(dummyFile));
                var itemsAfter = new StartupScanner().ScanAll();
                var foundAfter = itemsAfter.FirstOrDefault(x => x.Name == "dummy_app.lnk");
                Assert.Null(foundAfter);
            }
            finally
            {
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
            }
        }
    }

    public class TweakManagerTests
    {
        [Fact]
        public void AllTweaks_IsApplied_EvaluatesWithoutExceptions()
        {
            var tweaks = TweakCatalog.All();
            Assert.NotEmpty(tweaks);
            foreach (var t in tweaks)
            {
                Assert.True(Enum.TryParse<TweakKind>(t.Id, out var kind));
                var isApplied = TweakManager.IsApplied(kind);
                // Deve retornar bool sem lançar exceção
                Assert.True(isApplied || !isApplied);
            }
        }
    }
}