using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using SidebarDiagnostics.Framework;
using SidebarDiagnostics.Monitoring;

namespace SidebarDiagnostics.Models
{
    public class GraphModel : INotifyPropertyChanged, IDisposable
    {
        public GraphModel()
        {
            PlotModel = NewPlotModel();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _monitorItems = null;
                    _monitor = null;

                    _hardwareItems = null;
                    _hardware = null;

                    _metricItems = null;

                    if (_metrics != null)
                    {
                        _metrics.CollectionChanged -= Metrics_CollectionChanged;

                        foreach (iMetric _metric in _metrics)
                        {
                            _metric.PropertyChanged -= Metric_PropertyChanged;
                        }

                        _metrics = null;
                    }

                    _plotModel = null;
                    _data = null;
                }

                _disposed = true;
            }
        }

        ~GraphModel()
        {
            Dispose(false);
        }

        public void BindData(MonitorManager manager)
        {
            BindMonitors(manager.MonitorPanels);

            ExpandConfig = true;
        }

        public void ResetAxes()
        {
            if (PlotModel != null)
            {
                PlotModel.ResetAllAxes();
                PlotModel.InvalidatePlot(false);
            }
        }

        private static PlotModel NewPlotModel()
        {
            PlotModel _model = new PlotModel();

            _model.Axes.Add(new LogarithmicAxis()
            {
                Position = AxisPosition.Left,
                MajorGridlineStyle = LineStyle.Solid
            });

            _model.Axes.Add(new DateTimeAxis()
            {
                Position = AxisPosition.Bottom,
                StringFormat = "T",
                Angle = 45
            });

            return _model;
        }

        public void SetupPlot()
        {
            _data = new Dictionary<iMetric, LineSeries>();

            PlotModel _model = NewPlotModel();

            foreach (iMetric _metric in Metrics)
            {
                LineSeries _series = new LineSeries()
                {
                    Title = _metric.FullName,
                    TrackerFormatString = string.Format("{0}\r\n{{4:#,##0.##}}{1}\r\n{{2:T}}", _metric.FullName, _metric.nAppend)
                };

                _data.Add(_metric, _series);

                _metric.PropertyChanged += Metric_PropertyChanged;

                _model.Series.Add(_series);
            }

            PlotModel = _model;
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void BindMonitors(MonitorPanel[] panels)
        {
            MonitorItems = panels;

            if (panels.Length > 0)
            {
                Monitor = panels[0];
            }
            else
            {
                Monitor = null;
            }
        }

        private void BindHardware(iMonitor[] monitors)
        {
            HardwareItems = monitors;

            if (monitors.Length > 0)
            {
                Hardware = monitors[0];
            }
            else
            {
                Hardware = null;
            }
        }

        private void BindMetrics(iMetric[] metrics)
        {
            MetricItems = metrics;
            Metrics = new ObservableCollection<iMetric>();
        }

        private void Metrics_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (iMetric _metric in e.OldItems)
                {
                    _metric.PropertyChanged -= Metric_PropertyChanged;
                }
            }

            SetupPlot();
        }

        private void Metric_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_disposed)
            {
                (sender as iMetric).PropertyChanged -= Metric_PropertyChanged;
                return;
            }

            if (e.PropertyName != "nValue")
            {
                return;
            }

            // sensor updates arrive on the polling thread; the plot belongs to the UI
            if (!App.Current.Dispatcher.CheckAccess())
            {
                App.Current.Dispatcher.BeginInvoke((Action)(() => Metric_PropertyChanged(sender, e)));
                return;
            }

            iMetric _metric = (iMetric)sender;

            if (_data == null || !_data.TryGetValue(_metric, out LineSeries _series))
            {
                _metric.PropertyChanged -= Metric_PropertyChanged;
                return;
            }

            DateTime _now = DateTime.Now;

            double _cutoff = DateTimeAxis.ToDouble(_now.AddSeconds(-Duration));

            List<DataPoint> _points = _series.Points;

            int _stale = 0;

            while (_stale < _points.Count && _points[_stale].X < _cutoff)
            {
                _stale++;
            }

            if (_stale > 0)
            {
                _points.RemoveRange(0, _stale);
            }

            _points.Add(new DataPoint(DateTimeAxis.ToDouble(_now), _metric.nValue > 0d ? _metric.nValue : 0.001d));

            PlotModel.InvalidatePlot(true);
        }

        private string _title { get; set; } = Resources.GraphTitle;

        public string Title
        {
            get
            {
                return _title;
            }
            set
            {
                _title = value;

                NotifyPropertyChanged("Title");
            }
        }

        private PlotModel _plotModel { get; set; }

        public PlotModel PlotModel
        {
            get
            {
                return _plotModel;
            }
            private set
            {
                _plotModel = value;

                NotifyPropertyChanged("PlotModel");
            }
        }

        private MonitorPanel[] _monitorItems { get; set; }

        public MonitorPanel[] MonitorItems
        {
            get
            {
                return _monitorItems;
            }
            set
            {
                _monitorItems = value;

                NotifyPropertyChanged("MonitorItems");
            }
        }

        private MonitorPanel _monitor { get; set; }

        public MonitorPanel Monitor
        {
            get
            {
                return _monitor;
            }
            set
            {
                _monitor = value;

                if (_monitor == null)
                {
                    BindHardware(new iMonitor[0]);
                }
                else
                {
                    BindHardware(_monitor.Monitors);
                }

                NotifyPropertyChanged("Monitor");
            }
        }

        private iMonitor[] _hardwareItems { get; set; }

        public iMonitor[] HardwareItems
        {
            get
            {
                return _hardwareItems;
            }
            set
            {
                _hardwareItems = value;

                NotifyPropertyChanged("HardwareItems");
            }
        }

        private iMonitor _hardware { get; set; }

        public iMonitor Hardware
        {
            get
            {
                return _hardware;
            }
            set
            {
                _hardware = value;

                if (_hardware == null)
                {
                    BindMetrics(new iMetric[0]);

                    Title = Resources.GraphTitle;
                }
                else
                {
                    BindMetrics(_hardware.Metrics.Where(m => m.IsNumeric).ToArray());

                    Title = string.Format("{0} - {1}", Resources.GraphTitle, _hardware.Name);
                }

                NotifyPropertyChanged("Hardware");
            }
        }

        private iMetric[] _metricItems { get; set; }

        public iMetric[] MetricItems
        {
            get
            {
                return _metricItems;
            }
            set
            {
                _metricItems = value;

                NotifyPropertyChanged("MetricItems");
            }
        }

        private ObservableCollection<iMetric> _metrics { get; set; }

        public ObservableCollection<iMetric> Metrics
        {
            get
            {
                return _metrics;
            }
            set
            {
                if (_metrics != null)
                {
                    foreach (iMetric _metric in _metrics)
                    {
                        _metric.PropertyChanged -= Metric_PropertyChanged;
                    }
                }

                _metrics = value;

                if (_metrics != null)
                {
                    SetupPlot();

                    _metrics.CollectionChanged += Metrics_CollectionChanged;
                }

                NotifyPropertyChanged("Metrics");
            }
        }

        public DurationItem[] DurationItems
        {
            get
            {

                return new DurationItem[5]
                {
                    new DurationItem(15, string.Format("15 {0}", Resources.GraphDurationSeconds)),
                    new DurationItem(30, string.Format("30 {0}", Resources.GraphDurationSeconds)),
                    new DurationItem(60, string.Format("1 {0}", Resources.GraphDurationMinute)),
                    new DurationItem(300, string.Format("5 {0}", Resources.GraphDurationMinutes)),
                    new DurationItem(900, string.Format("15 {0}", Resources.GraphDurationMinutes))
                };
            }
        }

        private int _duration { get; set; } = 15;

        public int Duration
        {
            get
            {
                return _duration;
            }
            set
            {
                _duration = value;

                NotifyPropertyChanged("Duration");
            }
        }

        private bool _expandConfig { get; set; } = true;

        public bool ExpandConfig
        {
            get
            {
                return _expandConfig;
            }
            set
            {
                _expandConfig = value;

                NotifyPropertyChanged("ExpandConfig");
            }
        }

        private Dictionary<iMetric, LineSeries> _data { get; set; }

        private bool _disposed { get; set; } = false;
    }

    public class DurationItem
    {
        public DurationItem(int seconds, string text)
        {
            Seconds = seconds;
            Text = text;
        }

        public int Seconds { get; set; }

        public string Text { get; set; }
    }
}
