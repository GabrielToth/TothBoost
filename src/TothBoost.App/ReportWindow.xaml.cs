using System.Windows;
using TothBoost.Core.Utilities;

namespace TothBoost.App
{
    public partial class ReportWindow : Window
    {
        public ReportWindow(ActionReport report, string title)
        {
            InitializeComponent();
            Title = title;
            SummaryText.Text = $"{report.SuccessCount} succeeded · {report.ErrorCount} failed — {title}";
            SummaryText.Foreground = report.ErrorCount == 0
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.OrangeRed;

            ListReport.ItemsSource = report.Items.Select(l => new ReportLine
            {
                Icon = l.Success ? "✔" : "✘",
                Item = l.TargetName,
                Action = l.Action,
                Detail = l.Details
            }).ToList();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }

    public class ReportLine
    {
        public string Icon { get; set; } = "";
        public string Item { get; set; } = "";
        public string Action { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}
