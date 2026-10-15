using System;
using System.Windows;

namespace SidebarDiagnostics
{
    public partial class ReportPreviewWindow : Window
    {
        private string _report;

        public ReportPreviewWindow(string report)
        {
            InitializeComponent();
            _report = report;
            ReportTextBox.Text = report;
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            SendButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            StatusText.Text = "Sending report...";

            var result = await ReportService.Send(_report);

            if (result.Success)
            {
                StatusText.Text = $"Report sent. ID: {result.Id}";
            }
            else
            {
                StatusText.Text = result.FriendlyMessage;
            }

            SendButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Close";
            CancelButton.IsEnabled = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
