using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TothBoost.Core;
using TothBoost.Core.Models;

namespace TothBoost.App
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private readonly BoostEngine _engine = new();
        private readonly ObservableCollection<SvcRow> _svcAll = new();
        private readonly ObservableCollection<ProcRow> _procAll = new();
        private readonly ObservableCollection<TaskRow> _taskAll = new();
        private readonly ObservableCollection<StartupRow> _startupAll = new();
        private readonly ObservableCollection<TweakRow> _tweakAll = new();
        private bool _busy;
        private readonly DispatcherTimer _procTimer = new() { Interval = TimeSpan.FromSeconds(2) };

        public MainWindow()
        {
            _procTimer.Tick += async (_, _) => await AutoRefreshProcesses();
            InitializeComponent();
            Loaded += OnLoaded;

            SvcFilter.ItemsSource = new[]
            {
                "Todos", "Recomendados p/ jogos", "Protegidos", "Cautela", "Opcionais", "Desabilitados", "Executando"
            };
            SvcFilter.SelectedIndex = 0;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var dark = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE (20 no Win11, 19 no Win10)
                _ = DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                _ = DwmSetWindowAttribute(hwnd, 19, ref dark, 4);
                // Bordas/caption/texto escuros (Win11): COLORREF 0x00BBGGRR
                var border = 0x00140F0B; // #0B0F14
                var caption = 0x0011161D; // #11161D
                var text = 0x00F3EDE6;   // #E6EDF3
                _ = DwmSetWindowAttribute(hwnd, 34, ref border, 4);  // DWMWA_BORDER_COLOR
                _ = DwmSetWindowAttribute(hwnd, 35, ref caption, 4); // DWMWA_CAPTION_COLOR
                _ = DwmSetWindowAttribute(hwnd, 36, ref text, 4);    // DWMWA_TEXT_COLOR
            }
            catch { }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            TxtBadgeAdmin.Text = "ADMINISTRADOR";

            SetBusy(true);
            try
            {
                if (!_engine.IsInitialized)
                {
                    // Primeiro uso — sequência obrigatória: ponto de restauração
                    // ANTES de qualquer varredura/perfil, para que o usuário só
                    // possa modificar depois da máquina estar protegida.
                    StatusText.Text = "Primeira execução: criando ponto de restauração obrigatório do Windows...";
                    TxtBadgeRestore.Text = "Ponto de restauração: criando...";
                    var (rpOk, rpMsg) = await Task.Run(() => _engine.InitialSetup());
                    TxtRestorePointInfo.Text = rpMsg;
                    if (rpOk)
                    {
                        TxtBadgeRestore.Text = "Ponto de restauração: OK";
                        BadgeRestore.Background = new SolidColorBrush(Color.FromRgb(0x14, 0x53, 0x2D));
                    }
                    else
                    {
                        TxtBadgeRestore.Text = "Ponto de restauração: NÃO CRIADO";
                        BadgeRestore.Background = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D));
                    }
                    if (_engine.BaselineExists)
                        StatusText.Text = "Perfil inicial gravado com o estado original completo. Você já pode modificar — tudo é reversível.";
                    else
                        StatusText.Text = "Aviso: " + rpMsg + " O perfil inicial foi gravado assim mesmo.";
                }
                else
                {
                    TxtBadgeRestore.Text = "Ponto de restauração: OK";
                    TxtRestorePointInfo.Text = "Este computador já está protegido: o ponto de restauração do primeiro uso e o perfil inicial completo (serviços, tarefas, inicialização) já foram criados.";
                }

                await Task.Run(_engine.EnsureBaseline);

                await RefreshServices();
                await RefreshTasks();
                await RefreshStartups();
                await RefreshTweaks();
                await RefreshProcesses();
                UpdateBaselineBadge();
                if (!_engine.IsInitialized)
                    StatusText.Text = "Pronto. Selecione itens e use os botões de habilitar/desabilitar. " +
                                      "Nada é excluído — tudo pode ser restaurado pela linha de base.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erro na inicialização: " + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdateBaselineBadge()
        {
            if (_engine.BaselineExists)
            {
                TxtBadgeBaseline.Text = "Linha de base: OK";
                BadgeBaseline.Background = Brushes.DarkGreen;
            }
            else
            {
                TxtBadgeBaseline.Text = "Linha de base: pendente";
                BadgeBaseline.Background = Brushes.DarkRed;
            }
        }

        // ---------- filtros ----------

        private void SvcSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySvcFilter();
        private void ProcSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyProcFilter();
        private void TaskSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyTaskFilter();
        private void StartupSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyStartupFilter();
        private void SvcFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySvcFilter();

        private void ApplySvcFilter()
        {
            var q = (SvcSearch.Text ?? "").Trim().ToLowerInvariant();
            var kind = SvcFilter.SelectedIndex;
            var view = _svcAll.Where(r =>
                (q.Length == 0 || r.Name.ToLowerInvariant().Contains(q) || r.DisplayName.ToLowerInvariant().Contains(q)) &&
                (kind switch
                {
                    0 => true,
                    1 => r.S.Risk == RiskLevel.Recommended,
                    2 => r.S.Risk == RiskLevel.Protected,
                    3 => r.S.Risk == RiskLevel.Warning,
                    4 => r.S.Risk == RiskLevel.Optional,
                    5 => r.S.StartKind == ServiceStartKind.Disabled,
                    6 => r.S.RunningState == "Executando",
                    _ => true
                })).ToList();
            GridServices.ItemsSource = view;
            SvcCount.Text = $"{view.Count} de {_svcAll.Count} serviços";
        }

        private void ApplyProcFilter()
        {
            var q = (ProcSearch.Text ?? "").Trim().ToLowerInvariant();
            var view = _procAll.Where(r => q.Length == 0 || r.Name.ToLowerInvariant().Contains(q) ||
                (r.Path != null && r.Path.ToLowerInvariant().Contains(q))).ToList();
            GridProcesses.ItemsSource = view;
            ProcCount.Text = $"{view.Count} de {_procAll.Count} processos";
        }

        private void ApplyTaskFilter()
        {
            var q = (TaskSearch.Text ?? "").Trim().ToLowerInvariant();
            var view = _taskAll.Where(r => q.Length == 0 || r.Name.ToLowerInvariant().Contains(q) ||
                r.FullPath.ToLowerInvariant().Contains(q)).ToList();
            GridTasks.ItemsSource = view;
            TaskCount.Text = $"{view.Count} tarefas";
        }

        private void ApplyStartupFilter()
        {
            var q = (StartupSearch.Text ?? "").Trim().ToLowerInvariant();
            var view = _startupAll.Where(r => q.Length == 0 || r.Name.ToLowerInvariant().Contains(q) ||
                r.Command.ToLowerInvariant().Contains(q)).ToList();
            GridStartup.ItemsSource = view;
            StartupCount.Text = $"{view.Count} itens";
        }

        // ---------- carregamento ----------

        private async Task RefreshServices()
        {
            var services = await Task.Run(_engine.GetServices);
            _svcAll.Clear();
            foreach (var s in services) _svcAll.Add(new SvcRow(s));
            ApplySvcFilter();
        }

        private async Task RefreshProcesses()
        {
            var procs = await Task.Run(_engine.GetProcesses);
            _procAll.Clear();
            foreach (var p in procs) _procAll.Add(new ProcRow(p));
            ApplyProcFilter();
        }

        private async Task RefreshTasks()
        {
            var tasks = await Task.Run(_engine.GetTasks);
            _taskAll.Clear();
            foreach (var t in tasks) _taskAll.Add(new TaskRow(t));
            ApplyTaskFilter();
        }

        private async Task RefreshStartups()
        {
            var s = await Task.Run(_engine.GetStartups);
            _startupAll.Clear();
            foreach (var i in s) _startupAll.Add(new StartupRow(i));
            ApplyStartupFilter();
        }

        private async Task RefreshTweaks()
        {
            var tw = await Task.Run(_engine.GetTweaks);
            _tweakAll.Clear();
            foreach (var t in tw) _tweakAll.Add(new TweakRow(t));
            GridTweaks.ItemsSource = _tweakAll;
        }

        private async Task AutoRefreshProcesses()
        {
            if (!(ChkAutoRefresh.IsChecked ?? false) || _busy || Tabs.SelectedIndex != 1) return;
            await RefreshProcesses();
        }

        private void AutoRefreshChanged(object sender, RoutedEventArgs e)
        {
            if (_procTimer is null) return;
            if (ChkAutoRefresh.IsChecked == true) _procTimer.Start();
            else _procTimer.Stop();
        }

        // ---------- operação com relatório ----------

        private async Task RunAction(Func<ActionReport> action, string title)
        {
            if (_busy) return;
            _busy = true;
            SetBusy(true);
            try
            {
                var report = await Task.Run(action);
                var w = new ReportWindow(report, title) { Owner = this };
                w.ShowDialog();
                await RefreshServices();
                await RefreshTasks();
                await RefreshStartups();
                await RefreshTweaks();
                await RefreshProcesses();
                UpdateBaselineBadge();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro: " + ex.Message, "TothBoost", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy) => Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        // ---------- serviços ----------

        private void BtnSvcDisableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() =>
            {
                var sel = _svcAll.Where(r => r.IsChecked && !r.S.IsProtected).Select(r => r.Name).ToList();
                return _engine.DisableServices(sel, _svcAll.Select(x => x.S));
            }, "Desabilitar serviços");

        private void BtnSvcEnableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() =>
            {
                var sel = _svcAll.Where(r => r.IsChecked && r.S.StartKind != ServiceStartKind.Automatic).Select(r => r.Name).ToList();
                return _engine.EnableServices(sel, ServiceStartKind.Automatic);
            }, "Habilitar serviços");

        private void BtnSvcStopSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() =>
            {
                var sel = _svcAll.Where(r => r.IsChecked && r.S.RunningState == "Executando").Select(r => r.Name).ToList();
                return _engine.StopServices(sel);
            }, "Parar serviços");

        private void BtnSvcStartSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() =>
            {
                var sel = _svcAll.Where(r => r.IsChecked && r.S.RunningState != "Executando").Select(r => r.Name).ToList();
                return _engine.StartServices(sel);
            }, "Iniciar serviços");

        private async void BtnSvcGaming_Click(object sender, RoutedEventArgs e)
        {
            var count = _svcAll.Count(r => r.S.Risk == RiskLevel.Recommended && !r.S.IsProtected);
            var confirm = MessageBox.Show(
                $"Aplicar o perfil de jogos?\n\n{count} serviços 'Recomendados' serão desabilitados e parados.\n\n" +
                "Nada é alterado permanentemente — é possível restaurar tudo com 1 clique na linha de base.",
                "Perfil de jogos", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;
            await RunAction(() => _engine.ApplyGamingPreset(_svcAll.Select(r => r.S)), "Perfil de jogos");
        }

        private async void SvcRowDisable_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not SvcRow r || r.S.IsProtected) return;
            await RunAction(() => _engine.DisableServices(new[] { r.Name }, _svcAll.Select(x => x.S)), "Desabilitar serviço: " + r.Name);
        }

        private async void SvcRowEnable_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not SvcRow r) return;
            await RunAction(() => _engine.EnableServices(new[] { r.Name }, ServiceStartKind.Automatic), "Habilitar serviço: " + r.Name);
        }

        private async void SvcRowStop_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not SvcRow r) return;
            await RunAction(() => _engine.StopServices(new[] { r.Name }), "Parar serviço: " + r.Name);
        }

        // ---------- processos ----------

        private async void BtnProcKillSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _procAll.Where(r => r.IsChecked && !r.IsProtected).ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show("Nenhum processo selecionado — os protegidos do sistema estão bloqueados.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var ok = MessageBox.Show($"Encerrar {sel.Count} processo(s)?\nProcessos críticos do Windows não serão afetados.", "Encerrar processos", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
            await RunAction(() =>
            {
                var report = new ActionReport();
                foreach (var r in sel)
                {
                    try
                    {
                        using var p = Process.GetProcessById(r.Pid);
                        p.Kill(false);
                        report.Add(r.Name, "encerrado", true);
                    }
                    catch (Exception ex) { report.AddError(r.Name, "encerrar", ex); }
                }
                return report;
            }, "Encerrar processos");
        }

        private async void ProcRowKill_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProcRow r || r.IsProtected) return;
            await RunAction(() =>
            {
                var report = new ActionReport();
                try
                {
                    using var p = Process.GetProcessById(r.Pid);
                    p.Kill(false);
                    report.Add(r.Name, "encerrado", true);
                }
                catch (Exception ex) { report.AddError(r.Name, "encerrar", ex); }
                return report;
            }, "Encerrar processo");
        }

        private void ProcRowRestart_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProcRow r) return;
            if (string.IsNullOrEmpty(r.Path))
            {
                StatusText.Text = r.Name + ": caminho indisponível para reiniciar.";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true });
                StatusText.Text = "Reiniciando: " + r.Name;
            }
            catch (Exception ex) { MessageBox.Show("Não foi possível reiniciar: " + ex.Message, "TothBoost", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void BtnProcRestartSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _procAll.Where(r => r.IsChecked && !string.IsNullOrEmpty(r.Path)).ToList();
            foreach (var r in sel)
            {
                try { Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true }); }
                catch { }
            }
            StatusText.Text = $"Solicitada reinicialização de {sel.Count} processo(s).";
        }

        // ---------- tarefas ----------

        private void BtnTaskDisableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.SetTasksEnabled(
                _taskAll.Where(r => r.IsChecked && r.Enabled).Select(r => r.T).ToList(), false), "Desabilitar tarefas");

        private void BtnTaskEnableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.SetTasksEnabled(
                _taskAll.Where(r => r.IsChecked && !r.Enabled).Select(r => r.T).ToList(), true), "Habilitar tarefas");

        private async void TaskRowDisable_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not TaskRow r) return;
            await RunAction(() => _engine.SetTasksEnabled(new[] { r.T }, false), "Desabilitar tarefa: " + r.Name);
        }

        private async void TaskRowEnable_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not TaskRow r) return;
            await RunAction(() => _engine.SetTasksEnabled(new[] { r.T }, true), "Habilitar tarefa: " + r.Name);
        }

        // ---------- inicialização ----------

        private void BtnStartupDisableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.DisableStartups(
                _startupAll.Where(r => r.IsChecked && !r.IsDisabledByTb).Select(r => r.I).ToList()), "Desabilitar inicialização");

        private void BtnStartupEnableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.EnableStartups(
                _startupAll.Where(r => r.IsChecked && r.IsDisabledByTb).Select(r => r.I).ToList()), "Habilitar inicialização");

        private async void StartupRowDisable_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not StartupRow r) return;
            await RunAction(() => _engine.DisableStartups(new[] { r.I }), "Desabilitar inicialização: " + r.Name);
        }

        private async void StartupRowEnable_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not StartupRow r) return;
            await RunAction(() => _engine.EnableStartups(new[] { r.I }), "Habilitar inicialização: " + r.Name);
        }

        // ---------- ajustes ----------

        private async void TweakApply_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not TweakRow r) return;
            await RunAction(() => _engine.ApplyTweak(r.T.Id), "Aplicar ajuste: " + r.T.Name);
        }

        private async void TweakRevert_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not TweakRow r) return;
            await RunAction(() => _engine.RevertTweak(r.T.Id), "Reverter ajuste: " + r.T.Name);
        }

        // ---------- snapshots / restore ----------

        private async void BtnSnapshotNow_Click(object sender, RoutedEventArgs e)
        {
            var name = "manual-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            await RunAction(() =>
            {
                _engine.CreateSnapshot(name);
                var rep = new ActionReport();
                rep.Add("Snapshot", name, true, "estado atual salvo");
                return rep;
            }, "Criar snapshot manual");
        }

        private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            BtnCreateRestorePoint.IsEnabled = false;
            SetBusy(true);
            StatusText.Text = "Criando ponto de restauração do Windows...";
            try
            {
                var (ok, msg) = await Task.Run(_engine.CreateRestorePointNow);
                TxtRestorePointInfo.Text = msg;
                StatusText.Text = ok ? "Ponto de restauração criado com sucesso." : "Ponto de restauração NÃO criado — veja o motivo abaixo.";
                TxtBadgeRestore.Text = ok ? "Ponto de restauração: OK" : "Ponto de restauração: NÃO CRIADO";
                BadgeRestore.Background = new SolidColorBrush(ok ? Color.FromRgb(0x14, 0x53, 0x2D) : Color.FromRgb(0x7F, 0x1D, 0x1D));
                MessageBox.Show(msg + (ok ? "" : "\n\nDica: ative a Proteção do Sistema em System Properties > Proteção do Sistema."),
                    "Ponto de restauração", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                TxtRestorePointInfo.Text = "Falha: " + ex.Message;
            }
            finally
            {
                BtnCreateRestorePoint.IsEnabled = true;
                SetBusy(false);
            }
        }

        private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
        {
            if (!_engine.BaselineExists)
            {
                MessageBox.Show("A linha de base ainda não existe. Nenhuma modificação foi feita — nada a restaurar.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var ok = MessageBox.Show(
                "Restaurar TUDO para o estado original (linha de base)?\n\n" +
                "• Serviços voltam ao tipo de inicialização original\n" +
                "• Tarefas agendadas voltam ao estado original\n" +
                "• Inicialização automática volta ao original\n" +
                "• Ajustes aplicados (hibernação, telemetria etc.) são revertidos\n\n" +
                "Esta ação é a mesma do botão vermelho no topo.",
                "Restaurar tudo", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
            await RunAction(() => _engine.Restore("baseline"), "Restauração completa (linha de base)");
        }

        // ---------- atualização ----------

        private void BtnSvcRefresh_Click(object sender, RoutedEventArgs e) => _ = BackgroundRefresh();
        private void BtnTaskRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshTasks();
        private void BtnStartupRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshStartups();
        private void BtnProcRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshProcesses();
        private void BtnRefreshTweaks_Click(object sender, RoutedEventArgs e) => _ = RefreshTweaks();

        private async Task BackgroundRefresh()
        {
            SetBusy(true);
            await RefreshServices();
            await RefreshProcesses();
            await RefreshTasks();
            await RefreshStartups();
            await RefreshTweaks();
            SetBusy(false);
            StatusText.Text = "Listas atualizadas.";
        }
    }
}