using System.IO;
using System.Windows;
using TothBoost.Core;
using TothBoost.Core.Models;

namespace TothBoost.App
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Any(a => a.Equals("--smoketest", StringComparison.OrdinalIgnoreCase)))
            {
                RunSmokeTest();
                Shutdown();
                return;
            }

            _mutex = new Mutex(true, @"Global\TothBoost_SingleInstance", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("O TothBoost já está em execução.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            if (!TothBoost.Core.Utilities.Elevation.IsElevated)
            {
                MessageBox.Show(
                    "O TothBoost precisa ser executado como ADMINISTRADOR para poder " +
                    "gerenciar serviços, tarefas e inicialização do sistema.\n\n" +
                    "Clique com o botão direito no executável e escolha 'Executar como administrador'.",
                    "Permissão de administrador necessária",
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

        /// <summary>
        /// Modo de verificação headless (--smoketest): confirma que a linha de base é
        /// criada, que um serviço pode ser desabilitado e que a restauração devolve
        /// o estado exato. NÃO abre janela.
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
                File.WriteAllText(logPath, $"SMOKE TEST {DateTime.Now:u}" + Environment.NewLine);
                var engine = new BoostEngine();
                engine.EnsureBaseline();
                Log($"BASELINE: {(engine.BaselineExists ? "OK" : "FALHA")}  => {TothBoost.Core.Snapshot.SnapshotService.Root}");

                var services = engine.GetServices();
                Log($"SERVICES: {services.Count} totais, {services.Count(s => s.IsProtected)} protegidos, {services.Count(s => s.Risk == RiskLevel.Recommended)} recomendados");

                var candidate = services.FirstOrDefault(s =>
                    !s.IsProtected &&
                    (s.Risk == RiskLevel.Optional || s.Risk == RiskLevel.Recommended) &&
                    s.StartKind != ServiceStartKind.Disabled);
                if (candidate is null)
                {
                    Log("RESULTADO: FALHA — nenhum serviço candidato encontrado para o teste.");
                }
                else
                {
                    var before = (int)candidate.StartKind;
                    Log($"CANDIDATO: {candidate.Name} (start={before})");

                    var rep1 = engine.DisableServices(new[] { candidate.Name }, services);
                    Log($"DISABLING: {rep1.Succeeded} ok / {rep1.Failed} falhas");
                    var after = TothBoost.Core.Snapshot.SnapshotService.CurrentStartKind(candidate.Name);
                    Log($"VERIFY DISABLED: current start (int) = {after} (esperado 4)");

                    var rep2 = engine.Restore("baseline");
                    Log($"RESTORE: {rep2.Succeeded} ok / {rep2.Failed} falhas");
                    var restored = TothBoost.Core.Snapshot.SnapshotService.CurrentStartKind(candidate.Name);
                    Log($"VERIFY RESTORED: start (int) = {restored} (original {before})");

                    var ok = after == ServiceStartKind.Disabled && restored == (ServiceStartKind)before;
                    Log($"RESULTADO: {(ok ? "PASS" : "FAIL")}");
                }

                var tasks = engine.GetTasks();
                Log($"Tasks: {tasks.Count} | Startup: {engine.GetStartups().Count}");
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.WriteLine(console.ToString());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SMOKE FAIL: " + ex);
            }
        }
    }
}