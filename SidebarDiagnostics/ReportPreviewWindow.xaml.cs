using System;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace SidebarDiagnostics
{
    public partial class ReportPreviewWindow : Window
    {
        private readonly string _report;
        private readonly string _defaultFileName;

        public ReportPreviewWindow(string report)
            : this(report, null, true, "sidemon-log-report.txt")
        {
        }

        public ReportPreviewWindow(string report, string title, bool canSend, string defaultFileName = "sidemon-report.txt")
        {
            InitializeComponent();
            _report = report ?? string.Empty;
            _defaultFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "sidemon-report.txt" : defaultFileName;
            ReportTextBox.Text = _report;

            if (!string.IsNullOrWhiteSpace(title))
            {
                Title = title;
            }

            if (!canSend)
            {
                SendButton.Visibility = Visibility.Collapsed;
                CancelButton.Content = "Close";
                IntroText.Text = "Review the report below. Your username, computer name, MAC/IP addresses, tokens, and file paths have been removed. Direct sending is unavailable in this build; save it to a file to attach to an issue.";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = _defaultFileName
            };

            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, _report, new UTF8Encoding(false));
                    StatusText.Text = "Saved to file.";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Could not save file: {ex.Message}";
                }
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            SendButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
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
            SaveButton.IsEnabled = true;
            CancelButton.Content = "Close";
            CancelButton.IsEnabled = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
