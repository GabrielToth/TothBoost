using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using TothBoost.App;
using Xunit;

namespace TothBoost.Core.Tests
{
    /// <summary>
    /// UI-level tests that instantiate the WPF MainWindow on an STA thread to
    /// verify every style resource referenced by DataGrids/buttons resolves and
    /// that the window constructs without XAML parse exceptions (guards against
    /// the missing-static-resource crash seen when switching tabs).
    /// </summary>
    public class UiNavigationTests : IClassFixture<UiAppFixture>
    {
        private readonly UiAppFixture _fx;
        public UiNavigationTests(UiAppFixture fx) => _fx = fx;

        [Fact]
        public void DarkThemeDefinesAllReferencedStyleKeys()
        {
            var missing = _fx.MissingKeys(new[]
            {
                "Bg0", "Bg1", "Bg2", "Bg3", "Border", "BorderSoft",
                "Text", "TextDim", "TextSub", "Accent", "Green", "Red", "Amber",
                "Btn", "BtnPrimary", "BtnGreen", "BtnRed", "BtnAccent",
                "ToggleSwitch", "Card", "InverseBool"
            });
            Assert.Empty(missing);
        }

        [Fact]
        public void MainWindow_Constructs_WithoutResourceCrash()
        {
            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var win = new MainWindow { ShowInTaskbar = false, Width = 800, Height = 600 };
                    win.ApplyTemplate();
                    win.Close();
                }
                catch (Exception caught) { ex = caught; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(ex);
        }
    }

    /// <summary>Shares a single WPF Application so StaticResources resolve and only one App exists per AppDomain.</summary>
    public sealed class UiAppFixture : IDisposable
    {
        private readonly object _lock = new();
        private TothBoost.App.App? _app;
        private ResourceDictionary? _theme;

        private void EnsureApp()
        {
            lock (_lock)
            {
                if (_app != null) return;
                _app = new TothBoost.App.App();
                _app.InitializeComponent(); // merges App.xaml -> Themes/Dark.xaml
                foreach (var d in _app.Resources.MergedDictionaries)
                {
                    _theme = d;
                    break;
                }
            }
        }

        public IReadOnlyList<string> MissingKeys(IEnumerable<string> keys)
        {
            EnsureApp();
            var missing = new List<string>();
            foreach (var k in keys)
            {
                if (!(_app!.TryFindResource(k) is not null))
                {
                    missing.Add(k);
                }
            }
            return missing;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try { _app?.Shutdown(); } catch { }
            }
        }
    }
}