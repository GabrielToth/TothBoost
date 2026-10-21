using System.IO;
using System.Windows;
using TothBoost.Core;
using TothBoost.Core.Models;
using TothBoost.Core.Scanners;
using TothBoost.Core.Utilities;

namespace TothBoost.App
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, a) => LogCrash(a.ExceptionObject as Exception);
            DispatcherUnhandledException += (_, a) => { LogCrash(a.Exception); };

            if (e.Args.Any(a => a.Equals("--smoketest", StringComparison.OrdinalIgnoreCase)))
            {
                RunSmokeTest();
                Shutdown();
                return;
            }

            if (e.Args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                var code = RunSelfTest(e.Args);
                Shutdown(code);
                Environment.Exit(code);
                return;
            }

            _mutex = new Mutex(true, @"Global\TothBoost_SingleInstance", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("TothBoost is already running.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            if (!Elevation.IsElevated)
            {
                MessageBox.Show(
                    "TothBoost must be run as ADMINISTRATOR to manage Windows services, tasks, and startup items.\n\n" +
                    "Right-click the executable and choose 'Run as administrator'.",
                    "Administrator privileges required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private static void LogCrash(Exception? ex)
        {
            if (ex is null) return;
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "tothboost-crash.log"),
                    $"===== {DateTime.Now:u} =====" + Environment.NewLine + ex + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>
        /// Headless self-test (--self-test [logPath]): verifies the process really
        /// has an elevated administrator token and that the main window can be
        /// created and shown. Performs no system modifications. Exit code 0 = PASS.
        /// </summary>
        private static int RunSelfTest(string[] args)
        {
            var idx = Array.FindIndex(args, a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
            var logPath = idx >= 0 && idx + 1 < args.Length
                ? args[idx + 1]
                : Path.Combine(Path.GetTempPath(), "tothboost-dotnet-selftest.log");

            var lines = new List<string> { "TOTHBOOST .NET SELF-TEST " + DateTime.Now.ToString("u") };
            var ok = true;
            void Check(string name, bool condition, string detail = "")
            {
                if (!condition) ok = false;
                lines.Add($"{name}={(condition ? "OK" : "FAIL")}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
            }

            try
            {
                Check("ELEVATED", Elevation.IsElevated,
                    "identity=" + System.Security.Principal.WindowsIdentity.GetCurrent().Name);
                Check("VERSION", !string.IsNullOrEmpty(UpdateChecker.GetCurrentVersion()),
                    UpdateChecker.GetCurrentVersion());

                var win = new MainWindow(selfTest: true)
                {
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -32000,
                    Top = -32000,
                    Width = 900,
                    Height = 640
                };
                win.Show();
                win.UpdateLayout();
                var handle = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                Check("WINDOW_HANDLE", handle != IntPtr.Zero);
                Check("WINDOW_VISIBLE", win.IsVisible);
                Check("WINDOW_TITLE", !string.IsNullOrWhiteSpace(win.Title), win.Title);
                Check("DARK_THEME", win.FindResource("Bg0") is not null);
                win.Close();
            }
            catch (Exception ex)
            {
                ok = false;
                lines.Add("EXCEPTION=" + ex);
            }

            lines.Add("RESULT=" + (ok ? "PASS" : "FAIL"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Path.GetTempPath());
                File.WriteAllLines(logPath, lines);
            }
            catch { }
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Headless smoke test (--smoketest): verifies baseline creation, service
        /// disable/re-enable, and full restoration. Does not open a window.
        /// </summary>
        private void RunSmokeTest()
        {
            var console = new System.Text.StringBuilder();
            var logPath = Path.Combine(Path.GetTempPath(), "tothboost-smoketest.log");
            void Log(string s)
            {
                console.AppendLine(s);
                File.AppendAllText(logPath, s + Environment.NewLine);
            }
            try
            {
                File.WriteAllText(logPath, "SMOKE TEST " + DateTime.Now.ToString("u") + Environment.NewLine);
                var engine = new BoostEngine();
                engine.EnsureBaseline();
                Log("BASELINE: " + (engine.BaselineExists ? "OK" : "FAIL") + "  => " + TothBoost.Core.Snapshot.SnapshotService.Root);

                var services = engine.GetServices();
                Log($"SERVICES: {services.Count} total, {services.Count(s => s.IsProtected)} protected, {services.Count(s => s.Risk == RiskLevel.Recommended)} recommended");

                var candidate = services.FirstOrDefault(s =>
                    !s.IsProtected &&
                    (s.Risk == RiskLevel.Optional || s.Risk == RiskLevel.Recommended) &&
                    s.StartKind != ServiceStartKind.Disabled);
                if (candidate is null)
                {
                    Log("RESULT: FAIL - no candidate service found for test.");
                }
                else
                {
                    var before = (int)candidate.StartKind;
                    Log("CANDIDATE: " + candidate.Name + " (start=" + before + ")");

                    var rep1 = engine.DisableServices(new[] { candidate.Name }, services);
                    Log($"DISABLING: {rep1.SuccessCount} ok / {rep1.ErrorCount} failed");
                    var after = ServiceAccessor.CurrentStartKind(candidate.Name);
                    Log("VERIFY DISABLED: current start (int) = " + after + " (expected 4)");

                    var rep2 = engine.RestoreBaseline();
                    Log($"RESTORE: {rep2.SuccessCount} ok / {rep2.ErrorCount} failed");
                    var restored = ServiceAccessor.CurrentStartKind(candidate.Name);
                    Log("VERIFY RESTORED: start (int) = " + restored + " (original " + before + ")");

                    var ok = after == (int)ServiceStartKind.Disabled && restored == before;
                    Log("RESULT: " + (ok ? "PASS" : "FAIL"));
                }

                var tasks = engine.GetTasks();
                Log($"Tasks: {tasks.Count} | Startup: {engine.GetStartups().Count}");
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.WriteLine(console.ToString());
            }
            catch (Exception ex)
            {
                console.AppendLine("EXCEPTION: " + ex);
                File.AppendAllText(logPath, "EXCEPTION: " + ex + Environment.NewLine);
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.WriteLine(console.ToString());
            }
        }
    }

    internal static class ServiceAccessor
    {
        public static int CurrentStartKind(string serviceName)
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + serviceName);
            return k?.GetValue("Start") is int i ? i : -1;
        }
    }
}