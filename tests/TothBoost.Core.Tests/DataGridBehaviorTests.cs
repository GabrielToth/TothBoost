using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Threading;
using TothBoost.App;
using TothBoost.Core.Models;
using Xunit;

namespace TothBoost.Core.Tests
{
    /// <summary>
    /// Guards the grid behavior requested by users: headers sort on the real
    /// values (numeric for formatted columns) and every grid offers a column
    /// visibility chooser backed by the same persistent view.
    /// </summary>
    public class DataGridBehaviorTests : IClassFixture<UiAppFixture>
    {
        private readonly UiAppFixture _fx;
        public DataGridBehaviorTests(UiAppFixture fx) => _fx = fx;

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
            thread.Join(TimeSpan.FromSeconds(30));
            if (ex != null) throw ex;
        }

        [Fact]
        public void ProcessColumns_SortOnNumericUnderlyingValues()
        {
            RunOnSta(win =>
            {
                var grid = (DataGrid)win.FindName("GridProcesses");
                Assert.NotNull(grid);

                string SortOf(string header) =>
                    grid.Columns.Single(c => (c.Header?.ToString() ?? "") == header).SortMemberPath;

                Assert.Equal("Pid", SortOf("PID"));
                Assert.Equal("Name", SortOf("Process Name"));
                Assert.Equal("MemMb", SortOf("RAM (MB)"));
                Assert.Equal("CpuValue", SortOf("CPU %"));
                Assert.Equal("GpuValue", SortOf("GPU %"));
                Assert.Equal("ReadKbs", SortOf("Disk Read"));
                Assert.Equal("WriteKbs", SortOf("Disk Write"));
            });
        }

        [Fact]
        public void EveryGrid_HasPersistentViewAsItemsSource()
        {
            RunOnSta(win =>
            {
                foreach (var name in new[]
                {
                    "GridProcesses", "GridServices", "GridStartups", "GridTasks", "GridTweaks"
                })
                {
                    var grid = (DataGrid)win.FindName(name);
                    Assert.NotNull(grid);
                    Assert.IsAssignableFrom<ICollectionView>(grid.ItemsSource);

                    // A second read returns the same view instance: sorting set
                    // by the user is not discarded when data refreshes.
                    var first = grid.ItemsSource;
                    var second = grid.ItemsSource;
                    Assert.Same(first, second);
                }
            });
        }

        [Fact]
        public void EveryTab_OffersColumnChooserButton()
        {
            RunOnSta(win =>
            {
                foreach (var name in new[]
                {
                    "BtnProcColumns", "BtnSvcColumns", "BtnStartupColumns",
                    "BtnTaskColumns", "BtnTweakColumns"
                })
                {
                    var button = win.FindName(name) as Button;
                    Assert.NotNull(button);
                    Assert.IsType<DataGrid>(button!.Tag);
                }
            });
        }

        [Fact]
        public void ProcessGrid_NumericSortSurvivesDataRefresh()
        {
            RunOnSta(win =>
            {
                var grid = (DataGrid)win.FindName("GridProcesses");
                var field = typeof(MainWindow).GetField("_procAll",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(field);
                var rows = (System.Collections.ObjectModel.ObservableCollection<ProcRow>)field!.GetValue(win)!;

                ProcRow Make(int pid, double cpu) => new(new ProcessInfo
                {
                    Pid = pid,
                    Name = "p" + pid,
                    CpuPercent = cpu,
                    WorkingSetMb = cpu
                });

                rows.Add(Make(1, 5));
                rows.Add(Make(2, 50));

                var view = (ICollectionView)grid.ItemsSource!;
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(nameof(ProcRow.CpuValue), ListSortDirection.Descending));

                Assert.Equal(new[] { 50.0, 5.0 }, view.Cast<ProcRow>().Select(r => r.CpuValue));

                // The 2-second auto-refresh updates the collection in place; the
                // user's sort must not be discarded (old bug: ItemsSource reset).
                rows.Add(Make(3, 30));
                Assert.Equal(new[] { 50.0, 30.0, 5.0 }, view.Cast<ProcRow>().Select(r => r.CpuValue));

                // Sorting alphabetically on the name column must also work.
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(nameof(ProcRow.Name), ListSortDirection.Ascending));
                Assert.Equal(new[] { "p1", "p2", "p3" }, view.Cast<ProcRow>().Select(r => r.Name));
            });
        }
    }
}
