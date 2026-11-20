using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using System.Windows.Media;
using LibreHardwareMonitor.Hardware;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Monitoring
{
    public interface iMetric : INotifyPropertyChanged, IDisposable
    {
        MetricKey Key { get; }

        string FullName { get; }

        string Label { get; }

        double Value { get; }

        string Append { get; }

        double nValue { get; }

        string nAppend { get; }

        string Text { get; }

        bool IsAlert { get; }

        bool IsNumeric { get; }

        bool IsVisible { get; }

        void Update();

        void Update(double value);
    }

    public class BaseMetric : iMetric
    {
        public BaseMetric(MetricKey key, DataType dataType, string label = null, bool round = false, double alertValue = 0, iConverter converter = null)
        {
            _converter = converter;
            _round = round;
            _alertValue = alertValue;

            Key = key;

            if (label == null)
            {
                FullName = key.GetFullName();
                Label = key.GetLabel();
            }
            else
            {
                FullName = Label = label;
            }

            nAppend = Append = converter == null ? dataType.GetAppend() : converter.TargetType.GetAppend();
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
                    if (_blinking)
                    {
                        _blinking = false;
                        AlertBlinker.Unsubscribe(this);
                    }

                    _converter = null;
                }

                _disposed = true;
            }
        }

        ~BaseMetric()
        {
            Dispose(false);
        }

        public virtual void Update() { }

        public void Update(double value)
        {
            if (Text != null && Value == value) return;

            double _val = value;

            if (_converter == null)
            {
                nValue = _val;
            }
            else if (_converter.IsDynamic)
            {
                double _nVal;
                DataType _dataType;

                _converter.Convert(ref _val, out _nVal, out _dataType);

                nValue = _nVal;
                Append = _dataType.GetAppend();
            }
            else
            {
                _converter.Convert(ref _val);

                nValue = _val;
            }

            Value = _val;

            if (_alertValue > 0 && _alertValue <= nValue)
            {
                if (!IsAlert)
                {
                    IsAlert = true;
                }
            }
            else if (IsAlert)
            {
                IsAlert = false;
            }

            Text = string.Format(
                "{0:#,##0.##}{1}",
                _val.Round(_round),
                Append
                );
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    var _h = PropertyChanged;
                    if (_h != null) _h(this, new PropertyChangedEventArgs(propertyName));
                }));
            }
            else
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private MetricKey _key { get; set; }

        public MetricKey Key
        {
            get
            {
                return _key;
            }
            protected set
            {
                _key = value;

                NotifyPropertyChanged("Key");
            }
        }

        private string _fullName { get; set; }

        public string FullName
        {
            get
            {
                return _fullName;
            }
            protected set
            {
                _fullName = value;

                NotifyPropertyChanged("FullName");
            }
        }

        private string _label { get; set; }

        public string Label
        {
            get
            {
                return _label;
            }
            protected set
            {
                _label = value;

                NotifyPropertyChanged("Label");
            }
        }

        private double _value { get; set; }

        public double Value
        {
            get
            {
                return _value;
            }
            protected set
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;

                NotifyPropertyChanged("Value");
            }
        }

        private string _append { get; set; }

        public string Append
        {
            get
            {
                return _append;
            }
            protected set
            {
                if (string.Equals(_append, value, StringComparison.Ordinal))
                {
                    return;
                }

                _append = value;

                NotifyPropertyChanged("Append");
            }
        }

        private double _nValue { get; set; }

        public double nValue
        {
            get
            {
                return _nValue;
            }
            set
            {
                if (_nValue == value)
                {
                    return;
                }

                _nValue = value;

                NotifyPropertyChanged("nValue");
            }
        }

        private string _nAppend { get; set; }

        public string nAppend
        {
            get
            {
                return _nAppend;
            }
            set
            {
                if (string.Equals(_nAppend, value, StringComparison.Ordinal))
                {
                    return;
                }

                _nAppend = value;

                NotifyPropertyChanged("nAppend");
            }
        }

        private string _text { get; set; }

        public string Text
        {
            get
            {
                return _text;
            }
            protected set
            {
                if (string.Equals(_text, value, StringComparison.Ordinal))
                {
                    return;
                }

                _text = value;

                NotifyPropertyChanged("Text");
            }
        }

        private bool _isAlert { get; set; }

        public bool IsAlert
        {
            get
            {
                return _isAlert;
            }
            protected set
            {
                if (_isAlert == value)
                {
                    return;
                }

                _isAlert = value;

                NotifyPropertyChanged("IsAlert");

                if (value)
                {
                    if (Framework.Settings.Instance.AlertBlink && !_blinking)
                    {
                        _blinking = true;
                        AlertBlinker.Subscribe(this);
                    }
                }
                else if (_blinking)
                {
                    _blinking = false;
                    AlertBlinker.Unsubscribe(this);
                }
            }
        }

        public virtual bool IsNumeric
        {
            get { return true; }
        }

        private bool _isVisible { get; set; } = true;

        public bool IsVisible
        {
            get
            {
                return _isVisible;
            }
            protected set
            {
                if (_isVisible == value)
                {
                    return;
                }

                _isVisible = value;

                NotifyPropertyChanged("IsVisible");
            }
        }

        public string AlertColor
        {
            get
            {
                return AlertBlinker.Flag ? Framework.Settings.Instance.FontColor : Framework.Settings.Instance.AlertFontColor;
            }
        }

        internal void BlinkTick()
        {
            NotifyPropertyChanged("AlertColor");
        }

        private bool _blinking = false;

        protected iConverter _converter { get; set; }

        protected bool _round { get; set; }

        protected double _alertValue { get; set; }

        private bool _disposed { get; set; } = false;
    }

    // One shared timer drives every blinking alert instead of one DispatcherTimer per
    // metric. Subscribe/Unsubscribe may be called from the polling thread.
    internal static class AlertBlinker
    {
        private static readonly object _lock = new object();

        private static readonly List<BaseMetric> _subscribers = new List<BaseMetric>();

        private static DispatcherTimer _timer;

        public static bool Flag { get; private set; }

        public static void Subscribe(BaseMetric metric)
        {
            lock (_lock)
            {
                if (!_subscribers.Contains(metric))
                {
                    _subscribers.Add(metric);
                }
            }

            App.Current?.Dispatcher.BeginInvoke((Action)EnsureTimer);
        }

        public static void Unsubscribe(BaseMetric metric)
        {
            bool _empty;

            lock (_lock)
            {
                _subscribers.Remove(metric);
                _empty = _subscribers.Count == 0;
            }

            if (_empty)
            {
                App.Current?.Dispatcher.BeginInvoke((Action)StopTimer);
            }
        }

        private static void EnsureTimer()
        {
            lock (_lock)
            {
                if (_timer != null || _subscribers.Count == 0)
                {
                    return;
                }
            }

            _timer = new DispatcherTimer(DispatcherPriority.Background, App.Current.Dispatcher);
            _timer.Interval = TimeSpan.FromSeconds(0.5d);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private static void StopTimer()
        {
            lock (_lock)
            {
                if (_subscribers.Count > 0 || _timer == null)
                {
                    return;
                }
            }

            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
            Flag = false;
        }

        private static void Timer_Tick(object sender, EventArgs e)
        {
            Flag = !Flag;

            BaseMetric[] _current;

            lock (_lock)
            {
                _current = _subscribers.ToArray();
            }

            foreach (BaseMetric _metric in _current)
            {
                _metric.BlinkTick();
            }
        }
    }

    public class OHMMetric : BaseMetric
    {
        public OHMMetric(ISensor sensor, MetricKey key, DataType dataType, string label = null, bool round = false, double alertValue = 0, iConverter converter = null) : base(key, dataType, label, round, alertValue, converter)
        {
            _sensor = sensor;
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!_disposed)
            {
                if (disposing)
                {
                    _sensor = null;
                }

                _disposed = true;
            }
        }

        ~OHMMetric()
        {
            Dispose(false);
        }

        public override void Update()
        {
            if (_sensor.Value.HasValue)
            {
                Update(_sensor.Value.Value);
            }
            else
            {
                Text = "No Value";
            }
        }

        private ISensor _sensor { get; set; }

        private bool _disposed { get; set; } = false;
    }

    public class GPUVRAMMLoadMetric : BaseMetric
    {
        public GPUVRAMMLoadMetric(ISensor memoryUsedSensor, ISensor memoryTotalSensor, MetricKey key, DataType dataType, string label = null, bool round = false, double alertValue = 0, iConverter converter = null, bool showAsData = false) : base(key, dataType, label, round, alertValue, converter)
        {
            _memoryUsedSensor = memoryUsedSensor;
            _memoryTotalSensor = memoryTotalSensor;
            _showAsData = showAsData;
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!_disposed)
            {
                if (disposing)
                {
                    _memoryUsedSensor = null;
                    _memoryTotalSensor = null;
                }

                _disposed = true;
            }
        }

        ~GPUVRAMMLoadMetric()
        {
            Dispose(false);
        }

        public override void Update()
        {
            if (_memoryUsedSensor.Value.HasValue && _memoryTotalSensor.Value.HasValue)
            {
                float used = _memoryUsedSensor.Value.Value;
                float total = _memoryTotalSensor.Value.Value;

                if (_showAsData)
                {
                    // Used and Total are in MB in LibreHardwareMonitor.
                    // Convert to GB if total is large enough.
                    if (total >= 1000f)
                    {
                        float usedGB = used / 1024f;
                        float totalGB = total / 1024f;
                        Text = string.Format("{0:0.0} GB / {1:0.0} GB", usedGB, totalGB);
                    }
                    else
                    {
                        Text = string.Format("{0:0} MB / {1:0} MB", used, total);
                    }
                    
                    // Keep updating percentage values for alerts and charts
                    nValue = (used / total) * 100f;
                    Value = used;
                }
                else
                {
                    float load = used / total * 100f;
                    Update(load);
                }
            }
            else
            {
                Text = "No Value";
            }
        }

        private ISensor _memoryUsedSensor { get; set; }

        private ISensor _memoryTotalSensor { get; set; }

        private bool _showAsData { get; set; }

        private bool _disposed { get; set; } = false;
    }

    public class IPMetric : BaseMetric
    {
        public IPMetric(string ipAddress, MetricKey key, DataType dataType, string label = null, bool round = false, double alertValue = 0, iConverter converter = null) : base(key, dataType, label, round, alertValue, converter)
        {
            Text = ipAddress;
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~IPMetric()
        {
            Dispose(false);
        }

        public override bool IsNumeric
        {
            get { return false; }
        }
    }

    public class PCMetric : BaseMetric
    {
        public PCMetric(PerformanceCounter counter, MetricKey key, DataType dataType, string label = null, bool round = false, double alertValue = 0, iConverter converter = null) : base(key, dataType, label, round, alertValue, converter)
        {
            _counter = counter;
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!_disposed)
            {
                if (disposing)
                {
                    if (_counter != null)
                    {
                        _counter.Dispose();
                        _counter = null;
                    }
                }

                _disposed = true;
            }
        }

        ~PCMetric()
        {
            Dispose(false);
        }

        public override void Update()
        {
            try
            {
                Update(_counter.NextValue());
            }
            catch (Exception) { // the counter\'s instance (e.g. a drive letter) went away since the
                // counter was created; skip this tick rather than crash the reload
            }
        }

        private PerformanceCounter _counter { get; set; }

        private bool _disposed { get; set; } = false;
    }
}
