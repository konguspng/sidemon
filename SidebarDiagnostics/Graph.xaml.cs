using System.Windows.Controls;
using System.Windows.Input;
using SidebarDiagnostics.Models;
using SidebarDiagnostics.Monitoring;
using SidebarDiagnostics.Windows;
using System.ComponentModel;
using SidebarDiagnostics.Style;

namespace SidebarDiagnostics
{
    /// <summary>
    /// Interaction logic for Graph.xaml
    /// </summary>
    public partial class Graph : FlatWindow
    {
        public Graph(Sidebar sidebar)
        {
            InitializeComponent();

            DataContext = Model = new GraphModel();
            Model.BindData(sidebar.Model.MonitorManager);

            Show();
        }

        private void MetricList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Model == null || Model.Metrics == null)
            {
                return;
            }

            foreach (iMetric _metric in e.RemovedItems)
            {
                Model.Metrics.Remove(_metric);
            }

            foreach (iMetric _metric in e.AddedItems)
            {
                if (!Model.Metrics.Contains(_metric))
                {
                    Model.Metrics.Add(_metric);
                }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                Model.ResetAxes();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            DataContext = null;

            if (Model != null)
            {
                Model.Dispose();
                Model = null;
            }
        }

        public GraphModel Model { get; private set; }
    }
}
