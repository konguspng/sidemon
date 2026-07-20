using System.Windows;

namespace SidebarDiagnostics
{
    /// <summary>
    /// Small confirmation dialog shown after a background change (e.g. installing
    /// the PawnIO driver) that requires reloading the sidebar to take effect.
    /// </summary>
    public partial class ReloadPromptDialog : Window
    {
        public ReloadPromptDialog(string message)
        {
            InitializeComponent();

            MessageText.Text = message;
        }

        public bool ReloadRequested { get; private set; } = false;

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadRequested = true;

            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadRequested = false;

            Close();
        }
    }
}
