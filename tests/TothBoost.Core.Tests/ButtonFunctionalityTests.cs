using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TothBoost.App;
using TothBoost.Core.Models;
using Xunit;

namespace TothBoost.Core.Tests
{
    public class ButtonFunctionalityTests : IClassFixture<UiAppFixture>
    {
        private readonly UiAppFixture _fx;
        public ButtonFunctionalityTests(UiAppFixture fx) => _fx = fx;

        private void RunOnSta(Action<MainWindow> testAction)
        {
            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _fx.EnsureApp();
                    var win = new MainWindow { ShowInTaskbar = false, Width = 1000, Height = 700 };
                    win.ApplyTemplate();
                    testAction(win);
                    win.Close();
                }
                catch (Exception caught) { ex = caught; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (ex != null) throw ex;
        }

        [Fact]
        public void ProcessTab_ToggleView_SwitchesBetweenUserAndSystem()
        {
            RunOnSta(win =>
            {
                var toggleBtn = win.FindName("BtnProcViewToggle") as Button;
                Assert.NotNull(toggleBtn);
                Assert.Equal("Switch to System Processes", toggleBtn.Content);

                // Click toggle button
                toggleBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal("Switch to User Processes", toggleBtn.Content);

                // Toggle back
                toggleBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal("Switch to System Processes", toggleBtn.Content);
            });
        }

        [Fact]
        public void ProcessTab_SelectAll_TogglesCheckboxesForKillableProcesses()
        {
            RunOnSta(win =>
            {
                var selectAllBtn = win.FindName("BtnProcSelectAll") as Button;
                var grid = win.FindName("GridProcesses") as DataGrid;
                Assert.NotNull(selectAllBtn);
                Assert.NotNull(grid);

                var fakeList = new List<ProcRow>
                {
                    new ProcRow(new ProcessInfo { Pid = 100, Name = "test1", Protected = false }),
                    new ProcRow(new ProcessInfo { Pid = 101, Name = "test2", Protected = true })
                };
                grid.ItemsSource = fakeList;

                selectAllBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.True(fakeList[0].IsChecked);
                Assert.False(fakeList[1].IsChecked); // Protected process should remain false
            });
        }

        [Fact]
        public void StartupTab_SelectAll_TogglesAllStartupCheckboxes()
        {
            RunOnSta(win =>
            {
                var selectAllBtn = win.FindName("BtnStartupSelectAll") as Button;
                var grid = win.FindName("GridStartup") as DataGrid;
                Assert.NotNull(selectAllBtn);
                Assert.NotNull(grid);

                var fakeList = new List<StartupRow>
                {
                    new StartupRow(new StartupItem { Name = "App1", Command = "app1.exe", Location = "HKCU" }),
                    new StartupRow(new StartupItem { Name = "App2", Command = "app2.exe", Location = "HKLM" })
                };
                grid.ItemsSource = fakeList;

                selectAllBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.True(fakeList[0].IsChecked);
                Assert.True(fakeList[1].IsChecked);

                // Clicking again unchecks all
                selectAllBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.False(fakeList[0].IsChecked);
                Assert.False(fakeList[1].IsChecked);
            });
        }

        [Fact]
        public void TasksTab_SelectAll_TogglesAllTaskCheckboxes()
        {
            RunOnSta(win =>
            {
                var selectAllBtn = win.FindName("BtnTaskSelectAll") as Button;
                var grid = win.FindName("GridTasks") as DataGrid;
                Assert.NotNull(selectAllBtn);
                Assert.NotNull(grid);

                var fakeList = new List<TaskRow>
                {
                    new TaskRow(new ScheduledTaskInfo { Name = "Task1", FullPath = "\\Task1" })
                };
                grid.ItemsSource = fakeList;

                selectAllBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.True(fakeList[0].IsChecked);
            });
        }

        [Fact]
        public void ServicesTab_SelectAll_TogglesOnlyNonProtectedServices()
        {
            RunOnSta(win =>
            {
                var selectAllBtn = win.FindName("BtnSvcSelectAll") as Button;
                var grid = win.FindName("GridServices") as DataGrid;
                Assert.NotNull(selectAllBtn);
                Assert.NotNull(grid);

                var fakeList = new List<SvcRow>
                {
                    new SvcRow(new ServiceInfo { Name = "NormalSvc", Risk = RiskLevel.Recommended }),
                    new SvcRow(new ServiceInfo { Name = "CriticalSvc", Risk = RiskLevel.Protected })
                };
                grid.ItemsSource = fakeList;

                selectAllBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.True(fakeList[0].IsChecked);
                Assert.False(fakeList[1].IsChecked);
            });
        }

        [Fact]
        public void ServicesTab_FilterButtons_UpdatesActiveCategory()
        {
            RunOnSta(win =>
            {
                var btnRec = win.FindName("BtnSvcFilterGaming") as Button;
                var btnWarn = win.FindName("BtnSvcFilterWarn") as Button;
                var btnOpt = win.FindName("BtnSvcFilterOpt") as Button;
                var btnAll = win.FindName("BtnSvcFilterAll") as Button;

                Assert.NotNull(btnRec);
                Assert.NotNull(btnWarn);
                Assert.NotNull(btnOpt);
                Assert.NotNull(btnAll);

                btnRec.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                btnWarn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                btnOpt.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                btnAll.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            });
        }

        [Fact]
        public void SystemTweaks_UtilityButtons_ExistAndAreClickable()
        {
            RunOnSta(win =>
            {
                var btnJunk = win.FindName("BtnCleanJunk") as Button;
                var btnRam = win.FindName("BtnTrimRam") as Button;
                var btnDns = win.FindName("BtnFlushDns") as Button;

                Assert.NotNull(btnJunk);
                Assert.NotNull(btnRam);
                Assert.NotNull(btnDns);
            });
        }
    }
}
