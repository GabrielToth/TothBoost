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
using TothBoost.Core.Snapshot;

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

                await Task.Run(_engine.EnsureDefaultProfile);
                TxtCurrentProfile.Text = _engine.CurrentProfileName;
                RefreshProfilesUi();

                await RefreshServices();
                await RefreshTasks();
                await RefreshStartups();
                await RefreshTweaks();
                await RefreshProcesses();
                await RefreshRestorePointsUi();
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
            GridServices.ItemsSource = view.Where(r => r.CanTouch).ToList();
            GridSvcSystem.ItemsSource = view.Where(r => !r.CanTouch).ToList();
            SvcCount.Text = $"{view.Count} de {_svcAll.Count} serviços · {view.Count(r => !r.CanTouch)} protegidos";
        }

        private void ApplyProcFilter()
        {
            var q = (ProcSearch.Text ?? "").Trim().ToLowerInvariant();
            var view = _procAll.Where(r => q.Length == 0 || r.Name.ToLowerInvariant().Contains(q) ||
                (r.Path != null && r.Path.ToLowerInvariant().Contains(q))).ToList();
            GridProcesses.ItemsSource = view.Where(r => !r.IsProtected).ToList();
            GridProcSystem.ItemsSource = view.Where(r => r.IsProtected).ToList();
            ProcCount.Text = $"{view.Count(r => !r.IsProtected)} seus processos · {view.Count(r => r.IsProtected)} do sistema";
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

        private void DataGrid_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is not UIElement element) return;
            var scrollViewer = FindParentScrollViewer(element);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not ScrollViewer)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as ScrollViewer;
        }

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
            var newByPid = procs.ToDictionary(p => p.Pid);
            var existingByPid = _procAll.ToDictionary(r => r.Pid);

            // Remove processos que encerraram
            for (int i = _procAll.Count - 1; i >= 0; i--)
            {
                if (!newByPid.ContainsKey(_procAll[i].Pid))
                {
                    _procAll.RemoveAt(i);
                }
            }

            // Atualiza métricas in-place ou adiciona novos processos
            foreach (var p in procs)
            {
                if (existingByPid.TryGetValue(p.Pid, out var existingRow))
                {
                    existingRow.Update(p);
                }
                else
                {
                    _procAll.Add(new ProcRow(p));
                }
            }

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
            if (!(ChkAutoRefresh.IsChecked ?? false) || _busy || Tabs.SelectedIndex != 0) return;
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

        private async void SvcRowToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Primitives.ToggleButton)?.DataContext is not SvcRow r || r.S.IsProtected) return;
            var on = ((System.Windows.Controls.Primitives.ToggleButton)sender).IsChecked == true;
            if (on)
                await RunAction(() => _engine.EnableServices(new[] { r.Name }, ServiceStartKind.Automatic), "Habilitar serviço: " + r.Name);
            else
                await RunAction(() => _engine.DisableServices(new[] { r.Name }, _svcAll.Select(x => x.S)), "Desabilitar serviço: " + r.Name);
        }

        private async void SvcRowStop_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not SvcRow r) return;
            await RunAction(() => _engine.StopServices(new[] { r.Name }), "Parar serviço: " + r.Name);
        }

        // ---------- processos ----------

        private void BtnProcSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var marked = 0;
            if (GridProcesses.ItemsSource is System.Collections.IEnumerable rows)
            {
                foreach (var r in rows.OfType<ProcRow>())
                {
                    if (r.IsProtected || r.IsChecked) continue;
                    r.IsChecked = true;
                    marked++;
                }
            }
            StatusText.Text = marked > 0
                ? $"{marked} processo(s) marcado(s). Os protegidos do sistema ficaram de fora."
                : "Nenhum processo novo para marcar (os protegidos ficam de fora).";
        }

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

        private async void ProcRowRestart_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProcRow r) return;
            if (string.IsNullOrEmpty(r.Path))
            {
                StatusText.Text = r.Name + ": caminho indisponível para reiniciar.";
                return;
            }
            await RunAction(() =>
            {
                var report = new ActionReport();
                try
                {
                    try
                    {
                        using var p = Process.GetProcessById(r.Pid);
                        p.Kill(false);
                        p.WaitForExit(2000);
                    }
                    catch { }
                    Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true });
                    report.Add(r.Name, "reiniciado", true);
                }
                catch (Exception ex) { report.AddError(r.Name, "reiniciar", ex); }
                return report;
            }, "Reiniciar processo");
        }

        private async void BtnProcRestartSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _procAll.Where(r => r.IsChecked && !string.IsNullOrEmpty(r.Path) && !r.IsProtected).ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show("Nenhum processo selecionável com caminho válido para reiniciar.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await RunAction(() =>
            {
                var report = new ActionReport();
                foreach (var r in sel)
                {
                    try
                    {
                        try
                        {
                            using var p = Process.GetProcessById(r.Pid);
                            p.Kill(false);
                            p.WaitForExit(2000);
                        }
                        catch { }
                        Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true });
                        report.Add(r.Name, "reiniciado", true);
                    }
                    catch (Exception ex) { report.AddError(r.Name, "reiniciar", ex); }
                }
                return report;
            }, "Reiniciar processos");
        }

        // ---------- tarefas ----------

        private void BtnTaskDisableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.SetTasksEnabled(
                _taskAll.Where(r => r.IsChecked && r.Enabled).Select(r => r.T).ToList(), false), "Desabilitar tarefas");

        private void BtnTaskEnableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.SetTasksEnabled(
                _taskAll.Where(r => r.IsChecked && !r.Enabled).Select(r => r.T).ToList(), true), "Habilitar tarefas");

        private async void TaskRowToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Primitives.ToggleButton)?.DataContext is not TaskRow r) return;
            var on = ((System.Windows.Controls.Primitives.ToggleButton)sender).IsChecked == true;
            await RunAction(() => _engine.SetTasksEnabled(new[] { r.T }, on), on ? "Habilitar tarefa: " + r.Name : "Desabilitar tarefa: " + r.Name);
        }

        // ---------- inicialização ----------

        private void BtnStartupDisableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.DisableStartups(
                _startupAll.Where(r => r.IsChecked && !r.IsDisabledByTb).Select(r => r.I).ToList()), "Desabilitar inicialização");

        private void BtnStartupEnableSel_Click(object sender, RoutedEventArgs e) =>
            _ = RunAction(() => _engine.EnableStartups(
                _startupAll.Where(r => r.IsChecked && r.IsDisabledByTb).Select(r => r.I).ToList()), "Habilitar inicialização");

        private async void StartupRowToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Primitives.ToggleButton)?.DataContext is not StartupRow r) return;
            var on = ((System.Windows.Controls.Primitives.ToggleButton)sender).IsChecked == true;
            if (on)
                await RunAction(() => _engine.EnableStartups(new[] { r.I }), "Habilitar inicialização: " + r.Name);
            else
                await RunAction(() => _engine.DisableStartups(new[] { r.I }), "Desabilitar inicialização: " + r.Name);
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

        // ---------- perfis ----------

        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e) => SaveWithRecommendation(null);

        private void BtnProfileSaveHere_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProfileCard c) return;
            SaveWithRecommendation(c.Name);
        }

        /// <summary>
        /// Salva o estado atual no perfil indicado. Se for o Default, recomenda
        /// salvar em outro perfil (o Default é o save total / referência original).
        /// </summary>
        private async void SaveWithRecommendation(string? targetName)
        {
            if (_busy) return;
            var target = targetName ?? _engine.CurrentProfileName;
            if (string.Equals(target, "Default", StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageBox.Show(
                    "O perfil Default é o save total — o estado original de referência desta máquina.\n\n" +
                    "Recomendamos salvar em um perfil diferente: você pode criar um novo ou " +
                    "sobrescrever o perfil que escolher, ou continuar salvando em outros novos.\n\n" +
                    "Deseja salvar em outro perfil?",
                    "Salvar perfil", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.Yes)
                {
                    var name = PromptProfileName();
                    if (string.IsNullOrWhiteSpace(name)) return;
                    if (ProfileService.Exists(name))
                    {
                        var over = MessageBox.Show($"O perfil '{name}' já existe. Sobrescrever o estado dele?",
                            "Sobrescrever perfil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (over != MessageBoxResult.Yes) return;
                    }
                    _engine.CreateProfile(name);
                    target = name;
                }
                else
                {
                    var ow = MessageBox.Show("Sobrescrever o perfil Default mesmo assim?",
                        "Sobrescrever Default", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (ow != MessageBoxResult.Yes) return;
                }
            }
            await SaveTo(target);
        }

        private async Task SaveTo(string name)
        {
            _busy = true;
            SetBusy(true);
            try
            {
                await Task.Run(() => _engine.SaveProfileCaptureTo(name));
                StatusText.Text = "Estado atual salvo no perfil '" + name + "'.";
                RefreshProfilesUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao salvar o perfil: " + ex.Message, "TothBoost", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
                SetBusy(false);
            }
        }

        private async void BtnProfileUse_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProfileCard c) return;
            var confirm = MessageBox.Show(
                $"Aplicar o perfil '{c.Name}' no sistema?\n\n" +
                "Esta ação alterará a inicialização de serviços, tarefas, inicialização e ajustes " +
                "conforme gravado neste perfil.",
                "Aplicar Perfil", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            await RunAction(() => _engine.ApplyProfile(c.Name), "Aplicar Perfil: " + c.Name);
            RefreshProfilesUi();
        }

        private void BtnProfileCreate_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptProfileName();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (ProfileService.Exists(name))
            {
                MessageBox.Show($"Já existe um perfil chamado '{name}'.",
                    "Criar perfil", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _engine.CreateProfile(name);
            RefreshProfilesUi();
            StatusText.Text = "Perfil '" + name + "' criado e selecionado. Use 'Salvar estado atual' para gravá-lo.";
        }

        private bool _cardsView = true;

        private void BtnViewToggle_Click(object sender, RoutedEventArgs e)
        {
            _cardsView = !_cardsView;
            ProfileCards.Visibility = _cardsView ? Visibility.Visible : Visibility.Collapsed;
            GridProfiles.Visibility = _cardsView ? Visibility.Collapsed : Visibility.Visible;
            BtnViewToggle.Content = _cardsView ? "Ver como lista" : "Ver como cards";
        }

        private void RefreshProfilesUi()
        {
            var metas = _engine.ListProfiles();
            var cur = _engine.CurrentProfileName;
            var list = metas.Select(m => new ProfileCard(m,
                string.Equals(m.Name, cur, StringComparison.OrdinalIgnoreCase))).ToList();
            ProfileCards.ItemsSource = list;
            GridProfiles.ItemsSource = list;
            TxtCurrentProfile.Text = cur;
        }

        private string? PromptProfileName()
        {
            var win = new Window
            {
                Title = "Novo perfil — TothBoost",
                Width = 420, Height = 178,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14)),
                Foreground = Brushes.White,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Nome do novo perfil:", Foreground = Brushes.White });
            var box = new TextBox { Margin = new Thickness(0, 10, 0, 14), Text = "meu-perfil" };
            panel.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Criar", IsDefault = true, Width = 88, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, Width = 88 };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            win.Content = panel;
            string? result = null;
            ok.Click += (_, _) => { result = box.Text.Trim(); win.DialogResult = true; };
            box.KeyDown += (_, a) =>
            {
                if (a.Key == System.Windows.Input.Key.Enter) { result = box.Text.Trim(); win.DialogResult = true; }
            };
            win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
            return win.ShowDialog() == true ? result : null;
        }

        // ---------- pontos de restauração ----------

        private async Task RefreshRestorePointsUi()
        {
            try
            {
                var pts = await Task.Run(_engine.ListRestorePoints);
                var rows = pts.Select(p => new RestorePointRow(p)).ToList();
                GridRestorePoints.ItemsSource = rows;
                if (rows.Count == 0)
                    TxtRestorePointInfo.Text = "Nenhum ponto de restauração encontrado nesta máquina — use o botão abaixo para criar um.";
            }
            catch (Exception ex)
            {
                TxtRestorePointInfo.Text = "Falha ao listar pontos: " + ex.Message;
            }
        }

        private async void BtnRestorePointDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || (sender as Button)?.DataContext is not RestorePointRow r) return;
            var ok = MessageBox.Show(
                $"Excluir o ponto de restauração '{r.Description}' criado em {r.WhenText}?\n\n" +
                "POR CONTA E RISCO: esta ação remove um ponto do Windows de forma definitiva e não pode ser desfeita.",
                "Excluir ponto de restauração", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;
            _busy = true;
            SetBusy(true);
            try
            {
                var (delOk, msg) = await Task.Run(() => _engine.DeleteRestorePoint(r.Seq));
                StatusText.Text = msg;
                MessageBox.Show(msg, "Excluir ponto de restauração", MessageBoxButton.OK,
                    delOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
                await RefreshRestorePointsUi();
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

        private void BtnRefreshRestorePoints_Click(object sender, RoutedEventArgs e) => _ = RefreshRestorePointsUi();

        private async void BtnCreateRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            BtnCreateRestorePoint.IsEnabled = false;
            SetBusy(true);
            StatusText.Text = "Criando ponto de restauração do Windows...";
            try
            {
                var (ok, msg) = await Task.Run(_engine.CreateRestorePointNow);
                await RefreshRestorePointsUi();
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