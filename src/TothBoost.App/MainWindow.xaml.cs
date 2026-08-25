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
        private bool _busy;
        private readonly DispatcherTimer _procTimer = new() { Interval = TimeSpan.FromSeconds(2) };

        public MainWindow()
        {
            _procTimer.Tick += async (_, _) => await AutoRefreshProcesses();
            InitializeComponent();
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

            StartupFilter.ItemsSource = new[] { "All", "Enabled", "Disabled (Quarantine)" };
            StartupFilter.SelectedIndex = 0;

            TaskFilter.ItemsSource = new[] { "All", "Enabled", "Disabled" };
            TaskFilter.SelectedIndex = 0;

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

        private void ProcSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyProcFilter();

        private void ApplyProcFilter()
        {
            var q = ProcSearch.Text?.Trim().ToLowerInvariant() ?? "";
            var userList = _procAll.Where(r => !r.IsProtected).ToList();
            var sysList = _procAll.Where(r => r.IsProtected).ToList();

            if (!string.IsNullOrEmpty(q))
            {
                userList = userList.Where(r => r.Name.ToLowerInvariant().Contains(q) || r.Pid.ToString().Contains(q)).ToList();
                sysList = sysList.Where(r => r.Name.ToLowerInvariant().Contains(q) || r.Pid.ToString().Contains(q)).ToList();
            }

            GridProcesses.ItemsSource = userList;
            GridProcSystem.ItemsSource = sysList;
            ProcCount.Text = $"{userList.Count} user processes · {sysList.Count} system processes";
        }

        private async void BtnProcRefresh_Click(object sender, RoutedEventArgs e) => await RefreshProcesses();

        private void BtnProcSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var userList = _procAll.Where(r => !r.IsProtected).ToList();
            var targetState = !userList.All(r => r.IsChecked);
            int marked = 0;
            foreach (var r in userList)
            {
                r.IsChecked = targetState;
                if (targetState) marked++;
            }
            StatusText.Text = marked > 0
                ? $"{marked} process(es) checked. System protected processes excluded."
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
            var q = StartupSearch.Text?.Trim().ToLowerInvariant() ?? "";
            var f = StartupFilter.SelectedIndex;
            var list = _startupAll.AsEnumerable();

            if (f == 1) list = list.Where(i => i.IsOn);
            else if (f == 2) list = list.Where(i => !i.IsOn);

            if (!string.IsNullOrEmpty(q))
            {
                list = list.Where(i => i.Name.ToLowerInvariant().Contains(q) || i.Location.ToLowerInvariant().Contains(q) || i.Command.ToLowerInvariant().Contains(q));
            }

            var res = list.ToList();
            GridStartups.ItemsSource = res;
            StartupCount.Text = $"{res.Count} item(s)";
        }

        private void BtnStartupSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var visible = GridStartups.ItemsSource as List<StartupRow>;
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
                        report.Add(item.Name, "quarantined", true);
                    }
                    catch (Exception ex) { report.AddError(item.Name, "disable startup", ex); }
                }
                return report;
            }, "Disable Startup Items");
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
                    report.Add(r.Name, disable ? "quarantined" : "enabled", true);
                }
                catch (Exception ex) { report.AddError(r.Name, "toggle startup", ex); }
                return report;
            }, disable ? "Quarantine Startup" : "Enable Startup");
        }

        // ---------- Services ----------

        private void SvcSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySvcFilter();
        private void SvcFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySvcFilter();

        private void ApplySvcFilter()
        {
            var q = SvcSearch.Text?.Trim().ToLowerInvariant() ?? "";
            var f = SvcFilter.SelectedIndex;
            var list = _svcAll.AsEnumerable();

            list = f switch
            {
                1 => list.Where(s => s.S.Risk == RiskLevel.Recommended),
                2 => list.Where(s => s.S.IsProtected),
                3 => list.Where(s => s.S.Risk == RiskLevel.Warning),
                4 => list.Where(s => s.S.Risk == RiskLevel.Optional),
                5 => list.Where(s => !s.IsOn),
                6 => list.Where(s => s.RunningState == "Running"),
                _ => list
            };

            if (!string.IsNullOrEmpty(q))
            {
                list = list.Where(s => s.Name.ToLowerInvariant().Contains(q) || s.DisplayName.ToLowerInvariant().Contains(q) || s.Description.ToLowerInvariant().Contains(q));
            }

            var res = list.ToList();
            GridServices.ItemsSource = res;
            SvcCount.Text = $"{res.Count} service(s)";
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
            var q = TaskSearch.Text?.Trim().ToLowerInvariant() ?? "";
            var f = TaskFilter.SelectedIndex;
            var list = _taskAll.AsEnumerable();

            if (f == 1) list = list.Where(t => t.IsOn);
            else if (f == 2) list = list.Where(t => !t.IsOn);

            if (!string.IsNullOrEmpty(q))
            {
                list = list.Where(t => t.Name.ToLowerInvariant().Contains(q) || t.FullPath.ToLowerInvariant().Contains(q));
            }

            var res = list.ToList();
            GridTasks.ItemsSource = res;
            TaskCount.Text = $"{res.Count} task(s)";
        }

        private void BtnTaskSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var visible = GridTasks.ItemsSource as List<TaskRow>;
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

        // ---------- Tweaks ----------

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
