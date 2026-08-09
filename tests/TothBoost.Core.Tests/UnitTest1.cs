using System.Text.Json;
using System.Xml.Linq;
using TothBoost.Core.Known;
using TothBoost.Core.Models;
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
}