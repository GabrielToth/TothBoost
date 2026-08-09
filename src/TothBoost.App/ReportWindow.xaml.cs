using System.Windows;
using TothBoost.Core.Models;

namespace TothBoost.App
{
    public partial class ReportWindow : Window
    {
        public ReportWindow(ActionReport report, string title)
        {
            InitializeComponent();
            Title = title;
            SummaryText.Text = $"{report.Succeeded} concluídos · {report.Failed} falhas — {title}";
            SummaryText.Foreground = report.Failed == 0
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.OrangeRed;
            ListReport.ItemsSource = report.Lines.Select(l => new ReportLine
            {
                Icon = l.Success ? "✔" : "✘",
                Item = l.Item,
                Action = l.Action,
                Detail = l.Detail
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