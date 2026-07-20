using System.Windows;

namespace SidebarDiagnostics
{
    /// <summary>
    /// Small indeterminate-progress window used for short background operations
    /// (e.g. downloading and installing the PawnIO driver) so the app never looks
    /// frozen with no feedback while it works.
    /// </summary>
    public partial class ProgressDialog : Window
    {
        public ProgressDialog(string message)
        {
            InitializeComponent();

            MessageText.Text = message;
        }
    }
}
