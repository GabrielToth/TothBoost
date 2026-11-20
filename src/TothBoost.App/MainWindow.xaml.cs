using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TothBoost.Core;
using TothBoost.Core.Models;
using TothBoost.Core.Snapshot;
using TothBoost.Core.Scanners;
using TothBoost.Core.Utilities;

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
        private ICollectionView _procView = null!;
        private ICollectionView _svcView = null!;
        private ICollectionView _taskView = null!;
        private ICollectionView _startupView = null!;
        private ICollectionView _tweakView = null!;
        private bool _busy;
        private readonly bool _selfTest;
        private readonly DispatcherTimer _procTimer = new() { Interval = TimeSpan.FromSeconds(2) };

        public MainWindow() : this(false) { }

        public MainWindow(bool selfTest)
        {
            _selfTest = selfTest;
            _procTimer.Tick += async (_, _) => await AutoRefreshProcesses();
            InitializeComponent();

            // Tag targets for the "Columns" chooser buttons (assigned in code so
            // they are available before the window is loaded).
            BtnProcColumns.Tag = GridProcesses;
            BtnSvcColumns.Tag = GridServices;
            BtnStartupColumns.Tag = GridStartups;
            BtnTaskColumns.Tag = GridTasks;
            BtnTweakColumns.Tag = GridTweaks;

            // One persistent view per grid. Reassigning ItemsSource on every
            // refresh used to wipe the user's chosen sort order, so the grids
            // now keep a single ICollectionView whose Filter is refreshed
            // instead. Sorting set by clicking a header lives on that view.
            _procView = CreateView(_procAll, GridProcesses, ProcMatches);
            _svcView = CreateView(_svcAll, GridServices, SvcMatches);
            _taskView = CreateView(_taskAll, GridTasks, TaskMatches);
            _startupView = CreateView(_startupAll, GridStartups, StartupMatches);
            _tweakView = CreateView(_tweakAll, GridTweaks, _ => true);

            ApplySavedColumnLayout();

            Loaded += OnLoaded;

            SvcFilter.ItemsSource = new[]
            {
                "All", "Recommended for Gaming", "Protected", "Caution", "Optional", "Disabled", "Running"
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
                // DWMWA_USE_IMMERSIVE_DARK_MODE (20 on Win11, 19 on Win10)
                _ = DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                _ = DwmSetWindowAttribute(hwnd, 19, ref dark, 4);
            }
            catch { }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_selfTest) return;

            if (!_engine.IsInitialized)
            {
                StatusText.Text = "First run: Creating Windows Restore Point and baseline profile...";
                var (rpOk, rpMsg) = await Task.Run(_engine.InitialSetup);
                if (!rpOk)
                {
                    MessageBox.Show(
                        rpMsg + "\n\n" +
                        "Note: If system restore is disabled on drive C:, TothBoost enabled baseline capture.",
                        "First Run Baseline Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                _engine.EnsureBaseline();
            }

            BadgeBaseline.Visibility = Visibility.Visible;
            TxtBadgeAdmin.Text = "ADMINISTRATOR";

            StartupFilter.ItemsSource = new[] { "All", "Enabled", "Disabled" };
            StartupFilter.SelectedIndex = 0;

            TaskFilter.ItemsSource = new[] { "All", "Enabled", "Disabled" };
            TaskFilter.SelectedIndex = 0;

            ShortcutManager.CreateStartMenuShortcut();
            _ = AutoCheckUpdateSilentAsync();

            await RefreshAll();
            RefreshProfilesUi();
            StatusText.Text = "TothBoost ready. Default baseline loaded.";

            if (ChkAutoRefresh.IsChecked == true) _procTimer.Start();
        }

        private async Task RefreshAll()
        {
            await RefreshServices();
            await RefreshProcesses();
            await RefreshTasks();
            await RefreshStartups();
            await RefreshTweaks();
            RefreshRestorePointsUi();
        }

        private static ICollectionView CreateView<T>(ObservableCollection<T> source, DataGrid grid, Predicate<object> filter)
        {
            var view = CollectionViewSource.GetDefaultView(source);
            view.Filter = filter;
            grid.ItemsSource = view;
            return view;
        }

        private bool ProcMatches(object item)
        {
            if (item is not ProcRow r) return false;
            // System view shows only protected processes, user view the rest.
            if (r.IsProtected != _showSystemProcs) return false;

            var q = ProcSearch.Text?.Trim().ToLowerInvariant() ?? "";
            if (q.Length == 0) return true;
            return r.Name.ToLowerInvariant().Contains(q) || r.Pid.ToString().Contains(q);
        }

        private bool SvcMatches(object item)
        {
            if (item is not SvcRow s) return false;
            var f = SvcFilter.SelectedIndex;
            var matches = f switch
            {
                1 => s.S.Risk == RiskLevel.Recommended,
                2 => s.S.IsProtected,
                3 => s.S.Risk == RiskLevel.Warning,
                4 => s.S.Risk == RiskLevel.Optional,
                5 => !s.IsOn,
                6 => s.RunningState == "Running",
                _ => true
            };
            if (!matches) return false;

            var q = SvcSearch.Text?.Trim().ToLowerInvariant() ?? "";
            if (q.Length == 0) return true;
            return s.Name.ToLowerInvariant().Contains(q)
                || s.DisplayName.ToLowerInvariant().Contains(q)
                || s.Description.ToLowerInvariant().Contains(q);
        }

        private bool TaskMatches(object item)
        {
            if (item is not TaskRow t) return false;
            var f = TaskFilter.SelectedIndex;
            if (f == 1 && !t.IsOn) return false;
            if (f == 2 && t.IsOn) return false;

            var q = TaskSearch.Text?.Trim().ToLowerInvariant() ?? "";
            if (q.Length == 0) return true;
            return t.Name.ToLowerInvariant().Contains(q) || t.FullPath.ToLowerInvariant().Contains(q);
        }

        private bool StartupMatches(object item)
        {
            if (item is not StartupRow s) return false;
            var f = StartupFilter.SelectedIndex;
            if (f == 1 && !s.IsOn) return false;
            if (f == 2 && s.IsOn) return false;

            var q = StartupSearch.Text?.Trim().ToLowerInvariant() ?? "";
            if (q.Length == 0) return true;
            return s.Name.ToLowerInvariant().Contains(q)
                || s.Location.ToLowerInvariant().Contains(q)
                || s.Command.ToLowerInvariant().Contains(q);
        }

        // ---------- Column visibility ----------

        private static string ColumnKey(DataGridColumn column, int index)
        {
            var header = column.Header?.ToString();
            return string.IsNullOrWhiteSpace(header) ? "#" + index : header;
        }

        private static string LayoutPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TothBoost", "ui-layout.json");

        private IEnumerable<DataGrid> AllGrids()
        {
            yield return GridProcesses;
            yield return GridServices;
            yield return GridStartups;
            yield return GridTasks;
            yield return GridTweaks;
        }

        private void ApplySavedColumnLayout()
        {
            try
            {
                if (!File.Exists(LayoutPath)) return;
                var json = File.ReadAllText(LayoutPath);
                var hidden = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (hidden == null) return;

                foreach (var grid in AllGrids())
                {
                    if (!hidden.TryGetValue(grid.Name, out var keys) || keys == null) continue;
                    for (int i = 0; i < grid.Columns.Count; i++)
                    {
                        var column = grid.Columns[i];
                        column.Visibility = keys.Contains(ColumnKey(column, i))
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    }
                }
            }
            catch { /* layout is a convenience: never block startup on it */ }
        }

        private void SaveColumnLayout()
        {
            try
            {
                var layout = new Dictionary<string, List<string>>();
                foreach (var grid in AllGrids())
                {
                    var hidden = new List<string>();
                    for (int i = 0; i < grid.Columns.Count; i++)
                    {
                        if (grid.Columns[i].Visibility != Visibility.Visible)
                            hidden.Add(ColumnKey(grid.Columns[i], i));
                    }
                    layout[grid.Name] = hidden;
                }

                var dir = Path.GetDirectoryName(LayoutPath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(LayoutPath, JsonSerializer.Serialize(layout,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void ColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not DataGrid grid) return;

            var menu = new ContextMenu();
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                var column = grid.Columns[i];
                var key = ColumnKey(column, i);
                var item = new MenuItem
                {
                    Header = string.IsNullOrEmpty(column.Header?.ToString()) ? "Select (checkbox)" : column.Header!.ToString(),
                    IsCheckable = true,
                    IsChecked = column.Visibility == Visibility.Visible,
                    StaysOpenOnClick = true
                };
                item.Click += (_, _) =>
                {
                    column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    SaveColumnLayout();
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var showAll = new MenuItem { Header = "Show all columns" };
            showAll.Click += (_, _) =>
            {
                foreach (var column in grid.Columns) column.Visibility = Visibility.Visible;
                SaveColumnLayout();
            };
            menu.Items.Add(showAll);

            menu.PlacementTarget = fe;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

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

            // Remove processes that exited
            for (int i = _procAll.Count - 1; i >= 0; i--)
            {
                if (!newByPid.ContainsKey(_procAll[i].Pid))
                {
                    _procAll.RemoveAt(i);
                }
            }

            // Update metrics in-place or add new processes
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
            _tweakView.Refresh();
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

        // ---------- Action Reports ----------

        private async Task RunAction(Func<ActionReport> action, string title)
        {
            if (_busy) return;
            _busy = true;
            StatusText.Text = "Executing: " + title + "...";
            try
            {
                var report = await Task.Run(action);
                await RefreshAll();
                StatusText.Text = $"{title} completed ({report.SuccessCount} succeeded, {report.ErrorCount} failed).";
                new ReportWindow(report, title) { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
                MessageBox.Show("Could not execute action:\n" + ex.Message, "TothBoost Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
            }
        }

        // ---------- Processes ----------

        private bool _showSystemProcs;

        private void ProcSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyProcFilter();

        private void BtnProcViewToggle_Click(object sender, RoutedEventArgs e)
        {
            _showSystemProcs = !_showSystemProcs;
            ApplyProcFilter();
        }

        private void ApplyProcFilter()
        {
            _procView.Refresh();

            var list = _procView.Cast<ProcRow>().ToList();

            if (_showSystemProcs)
            {
                BtnProcViewToggle.Content = "Switch to User Processes";
                ProcViewHeader.Text = "SYSTEM PROCESSES — PROTECTED (view-only)";
                ProcViewHeader.Foreground = System.Windows.Media.Brushes.Salmon;
                ProcCount.Text = $"{list.Count} system processes";
            }
            else
            {
                BtnProcViewToggle.Content = "Switch to System Processes";
                ProcViewHeader.Text = "USER PROCESSES — Safe to terminate or restart";
                ProcViewHeader.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
                ProcCount.Text = $"{list.Count} user processes · {_procAll.Count(r => r.IsProtected)} system";
            }
        }

        private async void BtnProcRefresh_Click(object sender, RoutedEventArgs e) => await RefreshProcesses();

        private void BtnProcSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var list = (GridProcesses.ItemsSource as IEnumerable<ProcRow>)?.ToList()
                       ?? _procAll.Where(r => !r.IsProtected).ToList();
            var selectable = list.Where(r => !r.IsProtected).ToList();
            var targetState = !selectable.All(r => r.IsChecked);
            int marked = 0;
            foreach (var r in selectable)
            {
                r.IsChecked = targetState;
                if (targetState) marked++;
            }
            StatusText.Text = marked > 0
                ? $"{marked} process(es) checked. Protected processes excluded."
                : "No processes to check.";
        }

        private async void BtnProcKillSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _procAll.Where(r => r.IsChecked && !r.IsProtected).ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show("No killable processes selected.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var ok = MessageBox.Show($"Terminate {sel.Count} process(es)?\nCritical Windows processes will not be affected.", "Terminate Processes", MessageBoxButton.OKCancel, MessageBoxImage.Question);
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
                        report.Add(r.Name, "terminated", true);
                    }
                    catch (Exception ex) { report.AddError(r.Name, "terminate", ex); }
                }
                return report;
            }, "Terminate Processes");
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
                    report.Add(r.Name, "terminated", true);
                }
                catch (Exception ex) { report.AddError(r.Name, "terminate", ex); }
                return report;
            }, "Terminate Process");
        }

        private async void ProcRowRestart_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProcRow r) return;
            if (string.IsNullOrEmpty(r.Path))
            {
                StatusText.Text = r.Name + ": path unavailable to restart.";
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
                    report.Add(r.Name, "restarted", true);
                }
                catch (Exception ex) { report.AddError(r.Name, "restart", ex); }
                return report;
            }, "Restart Process");
        }

        private async void BtnProcRestartSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _procAll.Where(r => r.IsChecked && !string.IsNullOrEmpty(r.Path) && !r.IsProtected).ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show("No selectable processes with valid executable path to restart.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
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
                            p.WaitForExit(1000);
                        }
                        catch { }
                        Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true });
                        report.Add(r.Name, "restarted", true);
                    }
                    catch (Exception ex) { report.AddError(r.Name, "restart", ex); }
                }
                return report;
            }, "Restart Processes");
        }

        // ---------- Startup ----------

        private void StartupSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyStartupFilter();
        private void StartupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyStartupFilter();

        private void ApplyStartupFilter()
        {
            _startupView.Refresh();
            StartupCount.Text = $"{_startupView.Cast<StartupRow>().Count()} item(s)";
        }

        private void BtnStartupSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var visible = (GridStartups.ItemsSource as IEnumerable<StartupRow>)?.ToList();
            if (visible == null || visible.Count == 0) return;
            var targetState = !visible.All(r => r.IsChecked);
            foreach (var r in visible) r.IsChecked = targetState;
        }

        private async void BtnStartupEnableSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _startupAll.Where(r => r.IsChecked && !r.IsOn).ToList();
            if (sel.Count == 0) { MessageBox.Show("No disabled startup items selected.", "TothBoost"); return; }
            await RunAction(() =>
            {
                var report = new ActionReport();
                foreach (var item in sel)
                {
                    try
                    {
                        StartupScanner.SetDisabled(item.Item, false);
                        report.Add(item.Name, "enabled", true);
                    }
                    catch (Exception ex) { report.AddError(item.Name, "enable startup", ex); }
                }
                return report;
            }, "Enable Startup Items");
        }

        private async void BtnStartupDisableSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _startupAll.Where(r => r.IsChecked && r.IsOn).ToList();
            if (sel.Count == 0) { MessageBox.Show("No active startup items selected.", "TothBoost"); return; }
            await RunAction(() =>
            {
                var report = new ActionReport();
                foreach (var item in sel)
                {
                    try
                    {
                        StartupScanner.SetDisabled(item.Item, true);
                        report.Add(item.Name, "disabled", true);
                    }
                    catch (Exception ex) { report.AddError(item.Name, "disable startup", ex); }
                }
                return report;
            }, "Disable Startup Items");
        }

        private async void BtnStartupDisableSuggested_Click(object sender, RoutedEventArgs e)
        {
            var suggested = _startupAll.Where(r => r.IsOn && r.IsSuggested).ToList();
            if (suggested.Count == 0)
            {
                MessageBox.Show("All suggested startup items are already disabled.", "TothBoost", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var res = MessageBox.Show(
                $"Disable {suggested.Count} suggested startup item(s)?\n\n" +
                "These items (telemetry, background updaters, non-critical services) are safe to disable to improve boot time and responsiveness.",
                "Disable Suggested Startup Items",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            await RunAction(() =>
            {
                var report = new ActionReport();
                foreach (var item in suggested)
                {
                    try
                    {
                        StartupScanner.SetDisabled(item.Item, true);
                        report.Add(item.Name, "disabled", true);
                    }
                    catch (Exception ex) { report.AddError(item.Name, "disable startup", ex); }
                }
                return report;
            }, "Disable Suggested Startup Items");
        }

        private async void StartupRowToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not StartupRow r) return;
            var disable = r.IsOn;
            await RunAction(() =>
            {
                var report = new ActionReport();
                try
                {
                    StartupScanner.SetDisabled(r.Item, disable);
                    report.Add(r.Name, disable ? "disabled" : "enabled", true);
                }
                catch (Exception ex) { report.AddError(r.Name, "toggle startup", ex); }
                return report;
            }, disable ? "Disable Startup" : "Enable Startup");
        }

        // ---------- Services ----------

        private void SvcSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySvcFilter();
        private void SvcFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySvcFilter();

        private void BtnSvcFilterGaming_Click(object sender, RoutedEventArgs e) => SvcFilter.SelectedIndex = 1;
        private void BtnSvcFilterWarn_Click(object sender, RoutedEventArgs e) => SvcFilter.SelectedIndex = 3;
        private void BtnSvcFilterOpt_Click(object sender, RoutedEventArgs e) => SvcFilter.SelectedIndex = 4;
        private void BtnSvcFilterAll_Click(object sender, RoutedEventArgs e) => SvcFilter.SelectedIndex = 0;

        private void BtnSvcSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var list = (GridServices.ItemsSource as IEnumerable<SvcRow>)?.ToList() ?? _svcAll.ToList();
            var selectable = list.Where(r => r.CanTouch).ToList();
            var targetState = !selectable.All(r => r.IsChecked);
            foreach (var r in selectable) r.IsChecked = targetState;
            StatusText.Text = targetState
                ? $"{selectable.Count} service(s) checked. Protected services excluded."
                : "No services selected.";
        }

        private void ApplySvcFilter()
        {
            _svcView.Refresh();
            SvcCount.Text = $"{_svcView.Cast<SvcRow>().Count()} service(s)";
        }

        private async void BtnSvcApplyGame_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Apply Gaming Profile?\n\n" +
                "All non-essential services classified as 'Recommended' will be disabled.\n" +
                "Your baseline is fully preserved and can be restored with 1 click.",
                "Gaming Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            await RunAction(_engine.ApplyGameProfile, "Apply Gaming Profile");
        }

        private async void BtnSvcRestoreBaseline_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Restore baseline state?\n\n" +
                "All services, tasks, startup items, and tweaks will be restored to their original baseline.",
                "Restore Baseline", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            await RunAction(_engine.RestoreBaseline, "Restore Baseline");
        }

        private async void BtnSvcEnableSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _svcAll.Where(s => s.IsChecked && s.CanTouch).Select(s => s.Name).ToList();
            if (sel.Count == 0) { MessageBox.Show("No editable services selected.", "TothBoost"); return; }
            await RunAction(() => _engine.EnableServices(sel, ServiceStartKind.Automatic, _engine.GetServices()), "Enable Services");
        }

        private async void BtnSvcDisableSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _svcAll.Where(s => s.IsChecked && s.CanTouch).Select(s => s.Name).ToList();
            if (sel.Count == 0) { MessageBox.Show("No editable services selected.", "TothBoost"); return; }
            await RunAction(() => _engine.DisableServices(sel, _engine.GetServices()), "Disable Services");
        }

        private async void SvcRowToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not SvcRow r || !r.CanTouch) return;
            var disable = r.IsOn;
            var services = _engine.GetServices();
            await RunAction(() => disable ? _engine.DisableServices(new[] { r.Name }, services) : _engine.EnableServices(new[] { r.Name }, ServiceStartKind.Automatic, services),
                disable ? "Disable Service: " + r.Name : "Enable Service: " + r.Name);
        }

        // ---------- Scheduled Tasks ----------

        private void TaskSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyTaskFilter();
        private void TaskFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyTaskFilter();

        private void ApplyTaskFilter()
        {
            _taskView.Refresh();
            TaskCount.Text = $"{_taskView.Cast<TaskRow>().Count()} task(s)";
        }

        private void BtnTaskSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var visible = (GridTasks.ItemsSource as IEnumerable<TaskRow>)?.ToList();
            if (visible == null || visible.Count == 0) return;
            var targetState = !visible.All(r => r.IsChecked);
            foreach (var r in visible) r.IsChecked = targetState;
        }

        private async void BtnTaskEnableSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _taskAll.Where(t => t.IsChecked && !t.IsOn).ToList();
            if (sel.Count == 0) { MessageBox.Show("No disabled tasks selected.", "TothBoost"); return; }
            await RunAction(() =>
            {
                var report = new ActionReport();
                foreach (var t in sel)
                {
                    try
                    {
                        TaskScanner.SetEnabled(t.FullPath, true);
                        report.Add(t.Name, "enabled", true);
                    }
                    catch (Exception ex) { report.AddError(t.Name, "enable task", ex); }
                }
                return report;
            }, "Enable Scheduled Tasks");
        }

        private async void BtnTaskDisableSel_Click(object sender, RoutedEventArgs e)
        {
            var sel = _taskAll.Where(t => t.IsChecked && t.IsOn).ToList();
            if (sel.Count == 0) { MessageBox.Show("No enabled tasks selected.", "TothBoost"); return; }
            await RunAction(() =>
            {
                var report = new ActionReport();
                foreach (var t in sel)
                {
                    try
                    {
                        TaskScanner.SetEnabled(t.FullPath, false);
                        report.Add(t.Name, "disabled", true);
                    }
                    catch (Exception ex) { report.AddError(t.Name, "disable task", ex); }
                }
                return report;
            }, "Disable Scheduled Tasks");
        }

        private async void TaskRowToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TaskRow r) return;
            var enable = !r.IsOn;
            await RunAction(() =>
            {
                var report = new ActionReport();
                try
                {
                    TaskScanner.SetEnabled(r.FullPath, enable);
                    report.Add(r.Name, enable ? "enabled" : "disabled", true);
                }
                catch (Exception ex) { report.AddError(r.Name, "toggle task", ex); }
                return report;
            }, enable ? "Enable Task" : "Disable Task");
        }

        // ---------- Updates ----------

        private async Task AutoCheckUpdateSilentAsync()
        {
            try
            {
                var update = await _engine.CheckForUpdatesAsync();
                if (update.HasUpdate)
                {
                    BtnCheckUpdate.Content = $"🎉 Update v{update.LatestVersion} Available!";
                    BtnCheckUpdate.Style = (Style)FindResource("BtnGreen");

                    var choice = MessageBox.Show(
                        $"A new version of TothBoost (v{update.LatestVersion}) is available!\n\n" +
                        $"Current version: v{update.CurrentVersion}\n\n" +
                        $"Would you like to open the download page?",
                        "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (choice == MessageBoxResult.Yes && !string.IsNullOrEmpty(update.ReleaseUrl))
                    {
                        Process.Start(new ProcessStartInfo(update.ReleaseUrl) { UseShellExecute = true });
                    }
                }
            }
            catch { }
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.Content = "Checking...";
            var update = await _engine.CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(update.ErrorMessage))
            {
                MessageBox.Show(update.ErrorMessage, "Update Check", MessageBoxButton.OK, MessageBoxImage.Warning);
                BtnCheckUpdate.Content = "🔄 Check Updates";
                return;
            }

            if (update.HasUpdate)
            {
                BtnCheckUpdate.Content = $"🎉 Update v{update.LatestVersion} Available!";
                BtnCheckUpdate.Style = (Style)FindResource("BtnGreen");

                var res = MessageBox.Show(
                    $"New version v{update.LatestVersion} is available!\n\n" +
                    $"Release notes:\n{update.ReleaseNotes}\n\n" +
                    $"Open download page now?",
                    "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes && !string.IsNullOrEmpty(update.ReleaseUrl))
                {
                    Process.Start(new ProcessStartInfo(update.ReleaseUrl) { UseShellExecute = true });
                }
            }
            else
            {
                BtnCheckUpdate.Content = "🔄 Check Updates";
                MessageBox.Show($"You are running the latest version of TothBoost (v{update.CurrentVersion}).", "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ---------- Tweaks ----------

        private async void BtnCleanJunk_Click(object sender, RoutedEventArgs e)
        {
            await RunAction(() =>
            {
                var scan = _engine.ScanJunkFiles();
                long totalBytes = scan.Sum(c => c.SizeBytes);
                double totalMb = Math.Round(totalBytes / (1024.0 * 1024.0), 2);
                
                var cleanResult = _engine.CleanJunkFiles();
                var report = new ActionReport();
                report.Add("Junk & Temp Cleaning", "cleaned", true, $"Freed {cleanResult.TotalMbFreed} MB ({cleanResult.TotalFilesDeleted} files deleted)");
                return report;
            }, "Clean Junk & Temp Files");
        }

        private async void BtnTrimRam_Click(object sender, RoutedEventArgs e)
        {
            await RunAction(() =>
            {
                var res = _engine.TrimMemory();
                var report = new ActionReport();
                report.Add("RAM Working Set Trim", "trimmed", true, $"Freed {res.MbFreed} MB across {res.ProcessesTrimmed} processes");
                return report;
            }, "Free Up RAM (Trim Working Set)");
        }

        private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            await RunAction(() =>
            {
                var res = _engine.FlushDns();
                var report = new ActionReport();
                report.Add("Flush DNS Cache", "flushed", res.Success, res.Message);
                return report;
            }, "Flush DNS Cache");
        }

        private async void TweakApply_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not TweakRow r) return;
            await RunAction(() => _engine.ApplyTweak(r.T.Id), "Apply Tweak: " + r.T.Name);
        }

        private async void TweakRevert_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not TweakRow r) return;
            await RunAction(() => _engine.RevertTweak(r.T.Id), "Revert Tweak: " + r.T.Name);
        }

        // ---------- Profiles ----------

        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e) => SaveWithRecommendation(null);

        private void BtnProfileSaveHere_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProfileCard c) return;
            SaveWithRecommendation(c.Name);
        }

        private async void SaveWithRecommendation(string? targetName)
        {
            if (_busy) return;
            var target = targetName ?? _engine.CurrentProfileName;
            if (string.Equals(target, "Default", StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageBox.Show(
                    "The Default profile is your full original reference baseline.\n\n" +
                    "We recommend saving to a custom profile name to keep Default pristine.\n\n" +
                    "Would you like to save to a different profile?",
                    "Save Profile", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.Yes)
                {
                    BtnProfileCreate_Click(this, new RoutedEventArgs());
                    return;
                }
            }

            await RunAction(() =>
            {
                _engine.SaveCapture(target);
                var report = new ActionReport();
                report.Add("Profile: " + target, "saved", true, "Current state captured into profile.");
                return report;
            }, "Save Profile: " + target);

            RefreshProfilesUi();
        }

        private async void BtnProfileSelect_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProfileCard c) return;
            _engine.SelectProfile(c.Name);
            RefreshProfilesUi();
            StatusText.Text = "Active profile set to: " + c.Name;
        }

        private async void BtnApplyExtremeProfile_Click(object sender, RoutedEventArgs e)
        {
            _engine.EnsureExtremeProfile();
            var r = MessageBox.Show(
                "Apply the suggested EXTREME profile?\n\n" +
                "This profile is extremely aggressive:\n" +
                "• Disables 100% of non-critical Windows services (leaves only essential OS core).\n" +
                "• Disables all background apps and startup tasks.\n" +
                "• Enables all performance tweaks and Ultimate power plan.\n\n" +
                "You can edit this profile at any time by changing settings and overwriting it.",
                "Apply Extreme Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r != MessageBoxResult.Yes) return;
            await RunAction(() => _engine.ApplyProfile("Extreme"), "Apply Extreme Profile");
            RefreshProfilesUi();
        }

        private async void BtnProfileApply_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProfileCard c) return;
            var r = MessageBox.Show(
                $"Apply profile '{c.Name}'?\n\n" +
                "All saved service, task, startup, and tweak configurations will be applied to Windows.",
                "Apply Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (r != MessageBoxResult.Yes) return;
            await RunAction(() => _engine.ApplyProfile(c.Name), "Apply Profile: " + c.Name);
            RefreshProfilesUi();
        }

        private void BtnProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not ProfileCard c || c.IsDefault) return;
            var r = MessageBox.Show($"Delete profile '{c.Name}'?", "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            if (ProfileService.Delete(c.Name))
            {
                RefreshProfilesUi();
                StatusText.Text = "Profile '" + c.Name + "' deleted.";
            }
        }

        private void BtnProfileCreate_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptProfileName();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (ProfileService.Exists(name))
            {
                MessageBox.Show($"Profile '{name}' already exists.", "Create Profile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _engine.CreateProfile(name);
            RefreshProfilesUi();
            StatusText.Text = "Profile '" + name + "' created and selected. Click 'Save Current State' to capture.";
        }

        private bool _cardsView = true;

        private void BtnViewToggle_Click(object sender, RoutedEventArgs e)
        {
            _cardsView = !_cardsView;
            ProfileCards.Visibility = _cardsView ? Visibility.Visible : Visibility.Collapsed;
            GridProfiles.Visibility = _cardsView ? Visibility.Collapsed : Visibility.Visible;
            BtnViewToggle.Content = _cardsView ? "View as List" : "View as Cards";
        }

        private void RefreshProfilesUi()
        {
            var metas = _engine.ListProfiles();
            var cur = _engine.CurrentProfileName;
            var list = metas.Select(m => new ProfileCard(m, string.Equals(m.Name, cur, StringComparison.OrdinalIgnoreCase))).ToList();
            ProfileCards.ItemsSource = list;
            GridProfiles.ItemsSource = list;
            TxtCurrentProfile.Text = cur;
        }

        private string? PromptProfileName()
        {
            var win = new Window
            {
                Title = "New Profile — TothBoost",
                Width = 420, Height = 178,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Profile Name:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            var txt = new TextBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6) };
            sp.Children.Add(txt);

            var pnlBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnOk = new Button { Content = "Create", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var btnCancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

            string? result = null;
            btnOk.Click += (_, _) =>
            {
                var val = txt.Text.Trim();
                if (!string.IsNullOrEmpty(val)) { result = val; win.DialogResult = true; win.Close(); }
            };
            btnCancel.Click += (_, _) => { win.DialogResult = false; win.Close(); };

            pnlBtns.Children.Add(btnOk);
            pnlBtns.Children.Add(btnCancel);
            sp.Children.Add(pnlBtns);

            win.Content = sp;
            txt.Focus();
            return win.ShowDialog() == true ? result : null;
        }

        // ---------- System Restore Points ----------

        private void RefreshRestorePointsUi()
        {
            var pts = _engine.ListRestorePoints();
            GridRestorePoints.ItemsSource = pts;
        }

        private void BtnRpRefresh_Click(object sender, RoutedEventArgs e) => RefreshRestorePointsUi();

        private async void BtnRpCreate_Click(object sender, RoutedEventArgs e)
        {
            var desc = PromptRestorePointDesc();
            if (desc == null) return;

            StatusText.Text = "Creating Windows System Restore Point...";
            var (ok, msg) = await Task.Run(() => _engine.CreateRestorePoint(desc));
            RefreshRestorePointsUi();
            StatusText.Text = msg;
            MessageBox.Show(msg, "System Restore Point", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private string? PromptRestorePointDesc()
        {
            var win = new Window
            {
                Title = "New System Restore Point — TothBoost",
                Width = 460, Height = 178,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x14)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Description (e.g. Before GPU Driver Update):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            var txt = new TextBox { Width = 400, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6) };
            sp.Children.Add(txt);

            var pnlBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var btnOk = new Button { Content = "Create", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var btnCancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };

            string? result = null;
            btnOk.Click += (_, _) =>
            {
                result = string.IsNullOrWhiteSpace(txt.Text) ? "Manual Point" : txt.Text.Trim();
                win.DialogResult = true;
                win.Close();
            };
            btnCancel.Click += (_, _) => { win.DialogResult = false; win.Close(); };

            pnlBtns.Children.Add(btnOk);
            pnlBtns.Children.Add(btnCancel);
            sp.Children.Add(pnlBtns);

            win.Content = sp;
            txt.Focus();
            return win.ShowDialog() == true ? result : null;
        }

        private async void BtnRpRestore_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not RestorePointService.RestorePointItem item) return;
            var r = MessageBox.Show(
                $"Restore Windows to point #{item.SequenceNumber} ({item.Description})?\n\n" +
                "ATTENTION: Your computer will restart and restore system files to this state.",
                "Restore Windows System", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (r != MessageBoxResult.Yes) return;
            StatusText.Text = "Initiating Windows System Restore...";
            var (ok, msg) = await Task.Run(() => _engine.RestoreSystemToPoint(item.SequenceNumber));
            MessageBox.Show(msg, "System Restore", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

        private async void BtnRpDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not RestorePointService.RestorePointItem item) return;
            var r = MessageBox.Show(
                $"Delete System Restore Point #{item.SequenceNumber} ({item.Description})?",
                "Delete Restore Point", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (r != MessageBoxResult.Yes) return;
            var (ok, msg) = await Task.Run(() => _engine.DeleteRestorePoint(item.SequenceNumber));
            RefreshRestorePointsUi();
            StatusText.Text = msg;
        }
    }
}
