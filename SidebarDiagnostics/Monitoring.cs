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
    public class MonitorManager : INotifyPropertyChanged, IDisposable
    {
        public MonitorManager(MonitorConfig[] config)
        {
            try
            {
                _computer = new Computer()
                {
                    IsCpuEnabled = true,
                    IsControllerEnabled = true,
                    IsGpuEnabled = true,
                    IsStorageEnabled = false,
                    IsMotherboardEnabled = true,
                    IsMemoryEnabled = true,
                    IsNetworkEnabled = false
                };
                _computer.Open();
                _board = GetHardware(HardwareType.Motherboard).FirstOrDefault();

                UpdateBoard();

                MonitorPanels = config.Where(c => c.Enabled).OrderByDescending(c => c.Order).Select(c => NewPanel(c)).ToArray();
            }
            catch (Exception e)
            {
                // a fresh machine may not have the PawnIO driver loaded yet, or the
                // hardware library can throw on unusual configurations; degrade to no
                // panels rather than crashing the whole app on first run
                Utilities.ErrorLog.Write(e);

                MonitorPanels = Array.Empty<MonitorPanel>();
            }
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
                    foreach (MonitorPanel _panel in MonitorPanels)
                    {
                        _panel.Dispose();
                    }

                    _computer?.Close();

                    _monitorPanels = null;
                    _computer = null;
                    _board = null;
                }

                _disposed = true;
            }
        }

        ~MonitorManager()
        {
            Dispose(false);
        }

        public HardwareConfig[] GetHardware(MonitorType type)
        {
            switch (type)
            {
                case MonitorType.CPU:
                case MonitorType.RAM:
                case MonitorType.GPU:
                    return GetHardware(type.GetHardwareTypes()).Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name }).ToArray();

                case MonitorType.HD:
                    return DriveMonitor.GetHardware().ToArray();

                case MonitorType.Network:
                    return NetworkMonitor.GetHardware().ToArray();

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }

        public void Update()
        {
            UpdateBoard();

            foreach (iMonitor _monitor in MonitorPanels.SelectMany(p => p.Monitors))
            {
                _monitor.Update();
            }
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private IEnumerable<IHardware> GetHardware(params HardwareType[] types)
        {
            return _computer == null ? Enumerable.Empty<IHardware>() : _computer.Hardware.Where(h => types.Contains(h.HardwareType));
        }

        private MonitorPanel NewPanel(MonitorConfig config)
        {
            switch (config.Type)
            {
                case MonitorType.CPU:
                    return OHMPanel(
                        config.Type,
                        "M9 2v3 M15 2v3 M9 19v3 M15 19v3 M2 9h3 M2 15h3 M19 9h3 M19 15h3 M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Z M10 10h4v4h-4Z",
                        config.Hardware,
                        config.Metrics,
                        config.Params,
                        config.Type.GetHardwareTypes()
                        );

                case MonitorType.RAM:
                    return OHMPanel(
                        config.Type,
                        "M4 6h16a1 1 0 0 1 1 1v8H3V7a1 1 0 0 1 1-1Z M6 15v3 M10 15v3 M14 15v3 M18 15v3 M7.5 9v3 M12 9v3 M16.5 9v3",
                        config.Hardware,
                        config.Metrics,
                        config.Params,
                        config.Type.GetHardwareTypes()
                        );

                case MonitorType.GPU:
                    return OHMPanel(
                        config.Type,
                        "M3 5v15 M3 7h17a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H3 M7 17v3 M11 17v3 M17 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z",
                        config.Hardware,
                        config.Metrics,
                        config.Params,
                        config.Type.GetHardwareTypes()
                        );

                case MonitorType.HD:
                    return DrivePanel(
                        config.Type,
                        config.Hardware,
                        config.Metrics,
                        config.Params
                        );

                case MonitorType.Network:
                    return NetworkPanel(
                        config.Type,
                        config.Hardware,
                        config.Metrics,
                        config.Params
                        );

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }

        private MonitorPanel OHMPanel(MonitorType type, string pathData, HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters, params HardwareType[] hardwareTypes)
        {
            return new MonitorPanel(
                type.GetDescription(),
                pathData,
                OHMMonitor.GetInstances(hardwareConfig, metrics, parameters, type, _board, GetHardware(hardwareTypes).ToArray())
                );
        }

        private MonitorPanel DrivePanel(MonitorType type, HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters)
        {
            return new MonitorPanel(
                type.GetDescription(),
                "M22 12H2 M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z M6 16h.01 M10 16h.01",
                DriveMonitor.GetInstances(hardwareConfig, metrics, parameters)
                );
        }

        private MonitorPanel NetworkPanel(MonitorType type, HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters)
        {
            return new MonitorPanel(
                type.GetDescription(),
                "M9 2h6v6H9Z M3 16h6v6H3Z M15 16h6v6h-6Z M6 16v-3h12v3 M12 8v5",
                NetworkMonitor.GetInstances(hardwareConfig, metrics, parameters)
                );
        }

        private void UpdateBoard()
        {
            _board?.Update();
        }

        private MonitorPanel[] _monitorPanels { get; set; }

        public MonitorPanel[] MonitorPanels
        {
            get
            {
                return _monitorPanels;
            }
            private set
            {
                _monitorPanels = value;

                NotifyPropertyChanged("MonitorPanels");
            }
        }

        private Computer _computer { get; set; }

        private IHardware _board { get; set; }

        private bool _disposed { get; set; } = false;
    }

    public class MonitorPanel : INotifyPropertyChanged, IDisposable
    {
        public MonitorPanel(string title, string iconData, params iMonitor[] monitors)
        {
            IconPath = Geometry.Parse(iconData);
            Title = title;

            Monitors = monitors;
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
                    foreach (iMonitor _monitor in Monitors)
                    {
                        _monitor.Dispose();
                    }

                    _monitors = null;
                    _iconPath = null;
                }

                _disposed = true;
            }
        }

        ~MonitorPanel()
        {
            Dispose(false);
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private Geometry _iconPath { get; set; }

        public Geometry IconPath
        {
            get
            {
                return _iconPath;
            }
            private set
            {
                _iconPath = value;

                NotifyPropertyChanged("IconPath");
            }
        }

        private string _title { get; set; }

        public string Title
        {
            get
            {
                return _title;
            }
            private set
            {
                _title = value;

                NotifyPropertyChanged("Title");
            }
        }

        private iMonitor[] _monitors { get; set; }

        public iMonitor[] Monitors
        {
            get
            {
                return _monitors;
            }
            private set
            {
                _monitors = value;

                NotifyPropertyChanged("Monitors");
            }
        }

        private bool _disposed { get; set; } = false;
    }

    public interface iMonitor : INotifyPropertyChanged, IDisposable
    {
        string ID { get; }

        string Name { get; }

        bool ShowName { get; }

        iMetric[] Metrics { get; }

        void Update();
    }

    public class BaseMonitor : iMonitor
    {
        public BaseMonitor(string id, string name, bool showName)
        {
            ID = id;
            Name = name;
            ShowName = showName;
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
                    foreach (iMetric _metric in Metrics)
                    {
                        _metric.Dispose();
                    }

                    _metrics = null;
                }

                _disposed = true;
            }
        }

        ~BaseMonitor()
        {
            Dispose(false);
        }

        public virtual void Update()
        {
            foreach (iMetric _metric in Metrics)
            {
                _metric.Update();
            }
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private string _id { get; set; }

        public string ID
        {
            get
            {
                return _id;
            }
            protected set
            {
                _id = value;

                NotifyPropertyChanged("ID");
            }
        }

        private string _name { get; set; }

        public string Name
        {
            get
            {
                return _name;
            }
            protected set
            {
                _name = value;

                NotifyPropertyChanged("Name");
            }
        }

        private bool _showName { get; set; }

        public bool ShowName
        {
            get
            {
                return _showName;
            }
            protected set
            {
                _showName = value;

                NotifyPropertyChanged("ShowName");
            }
        }

        private iMetric[] _metrics { get; set; }

        public iMetric[] Metrics
        {
            get
            {
                return _metrics;
            }
            protected set
            {
                _metrics = value;

                NotifyPropertyChanged("Metrics");
            }
        }

        private bool _disposed { get; set; } = false;
    }

    public class OHMMonitor : BaseMonitor
    {
        public OHMMonitor(MonitorType type, string id, string name, IHardware hardware, IHardware board, MetricConfig[] metrics, ConfigParam[] parameters) : base(id, name, parameters.GetValue<bool>(ParamKey.HardwareNames))
        {
            _hardware = hardware;

            UpdateHardware();

            switch (type)
            {
                case MonitorType.CPU:
                    InitCPU(
                        board,
                        metrics,
                        parameters.GetValue<bool>(ParamKey.RoundAll),
                        parameters.GetValue<bool>(ParamKey.AllCoreClocks),
                        parameters.GetValue<bool>(ParamKey.UseGHz),
                        parameters.GetValue<bool>(ParamKey.UseFahrenheit),
                        parameters.GetValue<int>(ParamKey.TempAlert),
                        parameters.Any(p => p.Key == ParamKey.UseWatts) && parameters.GetValue<bool>(ParamKey.UseWatts),
                        parameters.Any(p => p.Key == ParamKey.ShowFanRPM) ? parameters.GetValue<bool>(ParamKey.ShowFanRPM) : true
                        );
                    break;

                case MonitorType.RAM:
                    InitRAM(
                        board,
                        metrics,
                        parameters.GetValue<bool>(ParamKey.RoundAll)
                        );
                    break;

                case MonitorType.GPU:
                    InitGPU(
                        metrics,
                        parameters.GetValue<bool>(ParamKey.RoundAll),
                        parameters.GetValue<bool>(ParamKey.UseGHz),
                        parameters.GetValue<bool>(ParamKey.UseFahrenheit),
                        parameters.GetValue<int>(ParamKey.TempAlert),
                        parameters.Any(p => p.Key == ParamKey.UseWatts) && parameters.GetValue<bool>(ParamKey.UseWatts),
                        parameters.Any(p => p.Key == ParamKey.ShowVRAMGB) && parameters.GetValue<bool>(ParamKey.ShowVRAMGB),
                        parameters.Any(p => p.Key == ParamKey.ShowFanRPM) ? parameters.GetValue<bool>(ParamKey.ShowFanRPM) : false
                        );
                    break;

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
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
                    _hardware = null;
                }

                _disposed = true;
            }
        }

        ~OHMMonitor()
        {
            Dispose(false);
        }

        public static iMonitor[] GetInstances(HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters, MonitorType type, IHardware board, IHardware[] hardware)
        {
            return (
                from hw in hardware
                join c in hardwareConfig on hw.Identifier.ToString() equals c.ID into merged
                from n in merged.DefaultIfEmpty(new HardwareConfig() { ID = hw.Identifier.ToString(), Name = hw.Name, ActualName = hw.Name }).Select(n => { if (n.ActualName != hw.Name) { n.Name = n.ActualName = hw.Name; } return n; })
                where n.Enabled
                orderby n.Order descending, n.Name ascending
                select new OHMMonitor(type, n.ID, n.Name ?? n.ActualName, hw, board, metrics, parameters)
                ).ToArray();
        }

        public override void Update()
        {
            UpdateHardware();

            base.Update();
        }

        private void UpdateHardware()
        {
            _hardware.Update();
        }

        private void InitCPU(IHardware board, MetricConfig[] metrics, bool roundAll, bool allCoreClocks, bool useGHz, bool useFahrenheit, double tempAlert, bool useWatts = false, bool showFanRPM = true)
        {
            List<OHMMetric> _sensorList = new List<OHMMetric>();

            if (metrics.IsEnabled(MetricKey.CPUClock))
            {
                Regex regex = new Regex(@"^.*(CPU|Core).*#(\d+)$");

                var coreClocks = _hardware.Sensors
                    .Where(s => s.SensorType == SensorType.Clock)
                    .Select(s => new
                    {
                        Match = regex.Match(s.Name),
                        Sensor = s
                    })
                    .Where(s => s.Match.Success)
                    .Select(s => new
                    {
                        Index = int.Parse(s.Match.Groups[2].Value),
                        s.Sensor
                    })
                    .OrderBy(s => s.Index)
                    .ToList();

                if (coreClocks.Count > 0)
                {
                    if (allCoreClocks)
                    {
                        foreach (var coreClock in coreClocks)
                        {
                            _sensorList.Add(new OHMMetric(coreClock.Sensor, MetricKey.CPUClock, DataType.MHz, string.Format("{0} {1}", Resources.CPUCoreClockLabel, coreClock.Index - 1), (useGHz ? false : true), 0, (useGHz ? MHzToGHz.Instance : null)));
                        }
                    }
                    else
                    {
                        ISensor firstClock = coreClocks
                            .Select(s => s.Sensor)
                            .FirstOrDefault();

                        _sensorList.Add(new OHMMetric(firstClock, MetricKey.CPUClock, DataType.MHz, null, (useGHz ? false : true), 0, (useGHz ? MHzToGHz.Instance : null)));
                    }
                }
            }

            if (metrics.IsEnabled(MetricKey.CPUVoltage))
            {
                if (useWatts)
                {
                    // Show CPU package power (Watts) instead of voltage
                    ISensor _power = _hardware.Sensors
                        .Where(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"))
                        .FirstOrDefault()
                        ?? _hardware.Sensors.Where(s => s.SensorType == SensorType.Power).FirstOrDefault();

                    if (_power != null)
                    {
                        _sensorList.Add(new OHMMetric(_power, MetricKey.CPUVoltage, DataType.Watt, Resources.PowerLabel, roundAll));
                    }
                }
                else
                {
                    ISensor _voltage = null;

                    if (board != null)
                    {
                        _voltage = board.Sensors.Where(s => s.SensorType == SensorType.Voltage && s.Name.Contains("CPU")).FirstOrDefault();
                    }

                    if (_voltage == null)
                    {
                        _voltage = _hardware.Sensors.Where(s => s.SensorType == SensorType.Voltage).FirstOrDefault();
                    }

                    if (_voltage != null)
                    {
                        _sensorList.Add(new OHMMetric(_voltage, MetricKey.CPUVoltage, DataType.Voltage, null, roundAll));
                    }
                }
            }

            if (metrics.IsEnabled(MetricKey.CPUTemp))
            {
                ISensor _tempSensor = null;

                _tempSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("CCDs Max (Tdie)")).FirstOrDefault(); // Check for AMD core chiplet dies (CCDs)

                if (board != null && _tempSensor == null)
                {
                    _tempSensor = board.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("CPU")).FirstOrDefault();
                }

                if (_tempSensor == null)
                {
                    _tempSensor =
                        _hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature && (s.Name == "CPU Package" || s.Name.Contains("Tdie"))).FirstOrDefault() ??
                        _hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature).FirstOrDefault();
                }

                if (_tempSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_tempSensor, MetricKey.CPUTemp, DataType.Celcius, null, roundAll, tempAlert, (useFahrenheit ? CelciusToFahrenheit.Instance : null)));
                }
            }

            if (metrics.IsEnabled(MetricKey.CPUFan))
            {
                ISensor _fanSensor = null;

                if (board != null)
                {
                    if (showFanRPM)
                    {
                        // 1. Prioritize RPM sensor
                        _fanSensor = board.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                            ?? board.Sensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault();
                        // 2. Fallback to Control (Percent) sensor
                        if (_fanSensor == null)
                        {
                            _fanSensor = board.Sensors.Where(s => s.SensorType == SensorType.Control && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                                ?? board.Sensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault();
                        }
                    }
                    else
                    {
                        // 1. Prioritize Control (Percent) sensor
                        _fanSensor = board.Sensors.Where(s => s.SensorType == SensorType.Control && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                            ?? board.Sensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault();
                        // 2. Fallback to RPM sensor
                        if (_fanSensor == null)
                        {
                            _fanSensor = board.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                                ?? board.Sensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault();
                        }
                    }
                }

                if (_fanSensor == null)
                {
                    if (showFanRPM)
                    {
                        _fanSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault()
                            ?? _hardware.Sensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault();
                    }
                    else
                    {
                        _fanSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault()
                            ?? _hardware.Sensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault();
                    }
                }

                if (_fanSensor != null)
                {
                    DataType fanDataType = _fanSensor.SensorType == SensorType.Control ? DataType.Percent : DataType.RPM;
                    _sensorList.Add(new OHMMetric(_fanSensor, MetricKey.CPUFan, fanDataType, null, roundAll));
                }
            }

            bool _loadEnabled = metrics.IsEnabled(MetricKey.CPULoad);
            bool _coreLoadEnabled = metrics.IsEnabled(MetricKey.CPUCoreLoad);

            if (_loadEnabled || _coreLoadEnabled)
            {
                ISensor[] _loadSensors = _hardware.Sensors.Where(s => s.SensorType == SensorType.Load).ToArray();

                if (_loadSensors.Length > 0)
                {
                    if (_loadEnabled)
                    {
                        ISensor _totalCPU = _loadSensors.Where(s => s.Index == 0).FirstOrDefault();

                        if (_totalCPU != null)
                        {
                            _sensorList.Add(new OHMMetric(_totalCPU, MetricKey.CPULoad, DataType.Percent, null, roundAll));
                        }
                    }

                    if (_coreLoadEnabled)
                    {
                        for (int i = 1; i <= _loadSensors.Max(s => s.Index); i++)
                        {
                            ISensor _coreLoad = _loadSensors.Where(s => s.Index == i).FirstOrDefault();

                            if (_coreLoad != null)
                            {
                                _sensorList.Add(new OHMMetric(_coreLoad, MetricKey.CPUCoreLoad, DataType.Percent, string.Format("{0} {1}", Resources.CPUCoreLoadLabel, i - 1), roundAll));
                            }
                        }
                    }
                }
            }

            Metrics = _sensorList.ToArray();
        }

        public void InitRAM(IHardware board, MetricConfig[] metrics, bool roundAll)
        {
            List<OHMMetric> _sensorList = new List<OHMMetric>();

            if (metrics.IsEnabled(MetricKey.RAMClock))
            {
                ISensor _ramClock = _hardware.Sensors.Where(s => s.SensorType == SensorType.Clock).FirstOrDefault();

                if (_ramClock != null)
                {
                    _sensorList.Add(new OHMMetric(_ramClock, MetricKey.RAMClock, DataType.MHz, null, true));
                }
            }

            if (metrics.IsEnabled(MetricKey.RAMVoltage))
            {
                ISensor _voltage = null;

                if (board != null)
                {
                    _voltage = board.Sensors.Where(s => s.SensorType == SensorType.Voltage && s.Name.Contains("RAM")).FirstOrDefault();
                }

                if (_voltage == null)
                {
                    _voltage = _hardware.Sensors.Where(s => s.SensorType == SensorType.Voltage).FirstOrDefault();
                }

                if (_voltage != null)
                {
                    _sensorList.Add(new OHMMetric(_voltage, MetricKey.RAMVoltage, DataType.Voltage, null, roundAll));
                }
            }

            if (metrics.IsEnabled(MetricKey.RAMLoad))
            {
                ISensor _loadSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Load && s.Index == 0).FirstOrDefault();

                if (_loadSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_loadSensor, MetricKey.RAMLoad, DataType.Percent, null, roundAll));
                }
            }

            if (metrics.IsEnabled(MetricKey.RAMUsed))
            {
                ISensor _usedSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Data && s.Index == 0).FirstOrDefault();

                if (_usedSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_usedSensor, MetricKey.RAMUsed, DataType.Gigabyte, null, roundAll));
                }
            }

            if (metrics.IsEnabled(MetricKey.RAMFree))
            {
                ISensor _freeSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Data && s.Index == 1).FirstOrDefault();

                if (_freeSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_freeSensor, MetricKey.RAMFree, DataType.Gigabyte, null, roundAll));
                }
            }

            Metrics = _sensorList.ToArray();
        }

        public void InitGPU(MetricConfig[] metrics, bool roundAll, bool useGHz, bool useFahrenheit, double tempAlert, bool useWatts = false, bool showVRAMGB = false, bool showFanRPM = false)
        {
            List<iMetric> _sensorList = new List<iMetric>();

            if (metrics.IsEnabled(MetricKey.GPUCoreClock))
            {
                ISensor _coreClock = _hardware.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core")).FirstOrDefault();

                if (_coreClock != null)
                {
                    _sensorList.Add(new OHMMetric(_coreClock, MetricKey.GPUCoreClock, DataType.MHz, null, (useGHz ? false : true), 0, (useGHz ? MHzToGHz.Instance : null)));
                }
            }

            if (metrics.IsEnabled(MetricKey.GPUVRAMClock))
            {
                ISensor _vramClock = _hardware.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.Contains("Memory")).FirstOrDefault();

                if (_vramClock != null)
                {
                    _sensorList.Add(new OHMMetric(_vramClock, MetricKey.GPUVRAMClock, DataType.MHz, null, (useGHz ? false : true), 0, (useGHz ? MHzToGHz.Instance : null)));
                }
            }

            if (metrics.IsEnabled(MetricKey.GPUCoreLoad))
            {
                ISensor _coreLoad = _hardware.Sensors.Where(s => s.SensorType == SensorType.Load && s.Name.Contains("Core")).FirstOrDefault() ??
                    _hardware.Sensors.Where(s => s.SensorType == SensorType.Load && s.Index == 0).FirstOrDefault();

                if (_coreLoad != null)
                {
                    _sensorList.Add(new OHMMetric(_coreLoad, MetricKey.GPUCoreLoad, DataType.Percent, null, roundAll));
                }
            }

            if (metrics.IsEnabled(MetricKey.GPUVRAMLoad))
            {
                ISensor _memoryUsed = _hardware.Sensors.Where(s => (s.SensorType == SensorType.Data || s.SensorType == SensorType.SmallData) && s.Name == "GPU Memory Used").FirstOrDefault();
                ISensor _memoryTotal = _hardware.Sensors.Where(s => (s.SensorType == SensorType.Data || s.SensorType == SensorType.SmallData) && s.Name == "GPU Memory Total").FirstOrDefault();

                if (_memoryUsed != null && _memoryTotal != null)
                {
                    _sensorList.Add(new GPUVRAMMLoadMetric(_memoryUsed, _memoryTotal, MetricKey.GPUVRAMLoad, DataType.Percent, null, roundAll, 0, null, showVRAMGB));
                }
                else
                {
                    ISensor _vramLoad = _hardware.Sensors.Where(s => s.SensorType == SensorType.Load && s.Name.Contains("Memory")).FirstOrDefault() ??
                        _hardware.Sensors.Where(s => s.SensorType == SensorType.Load && s.Index == 1).FirstOrDefault();

                    if (_vramLoad != null)
                    {
                        _sensorList.Add(new OHMMetric(_vramLoad, MetricKey.GPUVRAMLoad, DataType.Percent, null, roundAll));
                    }
                }
            }

            if (metrics.IsEnabled(MetricKey.GPUVoltage))
            {
                if (useWatts)
                {
                    // Show GPU total board power (Watts) instead of voltage
                    ISensor _power = _hardware.Sensors
                        .Where(s => s.SensorType == SensorType.Power && (s.Name.Contains("GPU Package") || s.Name.Contains("Board") || s.Name.Contains("Total")))
                        .FirstOrDefault()
                        ?? _hardware.Sensors.Where(s => s.SensorType == SensorType.Power).FirstOrDefault();

                    if (_power != null)
                    {
                        _sensorList.Add(new OHMMetric(_power, MetricKey.GPUVoltage, DataType.Watt, Resources.PowerLabel, roundAll));
                    }
                }
                else
                {
                    ISensor _voltage = _hardware.Sensors.Where(s => s.SensorType == SensorType.Voltage && s.Index == 0).FirstOrDefault();

                    if (_voltage != null)
                    {
                        _sensorList.Add(new OHMMetric(_voltage, MetricKey.GPUVoltage, DataType.Voltage, null, roundAll));
                    }
                }
            }

            if (metrics.IsEnabled(MetricKey.GPUTemp))
            {
                ISensor _tempSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Index == 0).FirstOrDefault();

                if (_tempSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_tempSensor, MetricKey.GPUTemp, DataType.Celcius, null, roundAll, tempAlert, (useFahrenheit ? CelciusToFahrenheit.Instance : null)));
                }
            }

            if (metrics.IsEnabled(MetricKey.GPUFan))
            {
                ISensor _fanSensor = null;

                if (showFanRPM)
                {
                    // 1. Prioritize RPM sensor
                    _fanSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Fan).OrderBy(s => s.Index).FirstOrDefault();
                    // 2. Fallback to Control (Percent) sensor
                    if (_fanSensor == null)
                    {
                        _fanSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Control).OrderBy(s => s.Index).FirstOrDefault();
                    }
                }
                else
                {
                    // 1. Prioritize Control (Percent) sensor
                    _fanSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Control).OrderBy(s => s.Index).FirstOrDefault();
                    // 2. Fallback to RPM sensor
                    if (_fanSensor == null)
                    {
                        _fanSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Fan).OrderBy(s => s.Index).FirstOrDefault();
                    }
                }

                if (_fanSensor != null)
                {
                    DataType fanDataType = _fanSensor.SensorType == SensorType.Control ? DataType.Percent : DataType.RPM;
                    _sensorList.Add(new OHMMetric(_fanSensor, MetricKey.GPUFan, fanDataType));
                }
            }

            Metrics = _sensorList.ToArray();
        }

        private IHardware _hardware { get; set; }

        private bool _disposed { get; set; } = false;
    }

    // Drive space comes from DriveInfo (cheap, admin-independent, immune to broken
    // perf-counter registries). Read/write throughput still needs LogicalDisk counters;
    // if those are unavailable the IO metrics are silently skipped instead of erroring.
    public class DriveMonitor : BaseMonitor
    {
        private const string CATEGORYNAME = "LogicalDisk";

        private const string BYTESREADPERSECOND = "Disk Read Bytes/sec";
        private const string BYTESWRITEPERSECOND = "Disk Write Bytes/sec";

        public DriveMonitor(string id, string name, MetricConfig[] metrics, bool roundAll = false, double usedSpaceAlert = 0) : base(id, name, true)
        {
            _loadEnabled = metrics.IsEnabled(MetricKey.DriveLoad);

            bool _loadBarEnabled = metrics.IsEnabled(MetricKey.DriveLoadBar);
            bool _usedEnabled = metrics.IsEnabled(MetricKey.DriveUsed);
            bool _freeEnabled = metrics.IsEnabled(MetricKey.DriveFree);
            bool _readEnabled = metrics.IsEnabled(MetricKey.DriveRead);
            bool _writeEnabled = metrics.IsEnabled(MetricKey.DriveWrite);

            if (_loadBarEnabled)
            {
                if (metrics.Count(m => m.Enabled) == 1 && new Regex("^[A-Z]:$").IsMatch(name))
                {
                    Status = State.LoadBarInline;
                }
                else
                {
                    Status = State.LoadBarStacked;
                }
            }
            else
            {
                Status = State.NoLoadBar;
            }

            _spaceEnabled = _loadBarEnabled || _loadEnabled || _usedEnabled || _freeEnabled;

            List<iMetric> _metrics = new List<iMetric>();

            if (_loadBarEnabled || _loadEnabled)
            {
                LoadMetric = new BaseMetric(MetricKey.DriveLoad, DataType.Percent, null, roundAll, usedSpaceAlert);
                _metrics.Add(LoadMetric);
            }

            if (_usedEnabled)
            {
                UsedMetric = new BaseMetric(MetricKey.DriveUsed, DataType.Gigabyte, null, roundAll);
                _metrics.Add(UsedMetric);
            }

            if (_freeEnabled)
            {
                FreeMetric = new BaseMetric(MetricKey.DriveFree, DataType.Gigabyte, null, roundAll);
                _metrics.Add(FreeMetric);
            }

            if (_readEnabled || _writeEnabled)
            {
                try
                {
                    if (_readEnabled)
                    {
                        _metrics.Add(new PCMetric(new PerformanceCounter(CATEGORYNAME, BYTESREADPERSECOND, id), MetricKey.DriveRead, DataType.kBps, null, roundAll, 0, BytesPerSecondConverter.Instance));
                    }

                    if (_writeEnabled)
                    {
                        _metrics.Add(new PCMetric(new PerformanceCounter(CATEGORYNAME, BYTESWRITEPERSECOND, id), MetricKey.DriveWrite, DataType.kBps, null, roundAll, 0, BytesPerSecondConverter.Instance));
                    }
                }
                catch (Exception e)
                {
                    SidebarDiagnostics.Utilities.ErrorLog.Write(e);
                }
            }

            Metrics = _metrics.ToArray();
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
                    _loadMetric = null;
                    _usedMetric = null;
                    _freeMetric = null;
                }

                _disposed = true;
            }
        }

        ~DriveMonitor()
        {
            Dispose(false);
        }

        public static IEnumerable<HardwareConfig> GetHardware()
        {
            Regex _regex = new Regex("^[A-Z]:$");

            return DriveInfo.GetDrives()
                .Where(d => (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady)
                .Select(d => d.Name.TrimEnd(DIRSEPARATOR))
                .Where(n => _regex.IsMatch(n))
                .OrderBy(n => n[0])
                .Select(n => new HardwareConfig() { ID = n, Name = n, ActualName = n });
        }

        private static readonly char[] DIRSEPARATOR = new char[1] { char.Parse("\\") };

        public static iMonitor[] GetInstances(HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters)
        {
            bool _roundAll = parameters.GetValue<bool>(ParamKey.RoundAll);
            int _usedSpaceAlert = parameters.GetValue<int>(ParamKey.UsedSpaceAlert);

            return (
                from hw in GetHardware()
                join c in hardwareConfig on hw.ID equals c.ID into merged
                from n in merged.DefaultIfEmpty(hw).Select(n => { n.ActualName = hw.Name; return n; })
                where n.Enabled
                orderby n.Order descending, n.Name ascending
                select new DriveMonitor(n.ID, n.Name ?? n.ActualName, metrics, _roundAll, _usedSpaceAlert)
                ).ToArray();
        }

        public override void Update()
        {
            if (_spaceEnabled)
            {
                try
                {
                    DriveInfo _drive = new DriveInfo(ID);

                    if (_drive.IsReady)
                    {
                        double _totalGB = _drive.TotalSize / 1073741824d;
                        double _freeGB = _drive.TotalFreeSpace / 1073741824d;
                        double _usedGB = _totalGB - _freeGB;
                        double _usedPercent = _totalGB > 0d ? _usedGB / _totalGB * 100d : 0d;

                        if (LoadMetric != null)
                        {
                            LoadMetric.Update(_usedPercent);
                        }

                        if (UsedMetric != null)
                        {
                            UsedMetric.Update(_usedGB);
                        }

                        if (FreeMetric != null)
                        {
                            FreeMetric.Update(_freeGB);
                        }
                    }
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }

            base.Update();
        }

        private State _status { get; set; }

        public State Status
        {
            get
            {
                return _status;
            }
            private set
            {
                _status = value;

                NotifyPropertyChanged("Status");
            }
        }

        private iMetric _loadMetric { get; set; }

        public iMetric LoadMetric
        {
            get
            {
                return _loadMetric;
            }
            private set
            {
                _loadMetric = value;

                NotifyPropertyChanged("LoadMetric");
            }
        }

        private iMetric _usedMetric { get; set; }

        public iMetric UsedMetric
        {
            get
            {
                return _usedMetric;
            }
            private set
            {
                _usedMetric = value;

                NotifyPropertyChanged("UsedMetric");
            }
        }

        private iMetric _freeMetric { get; set; }

        public iMetric FreeMetric
        {
            get
            {
                return _freeMetric;
            }
            private set
            {
                _freeMetric = value;

                NotifyPropertyChanged("FreeMetric");
            }
        }

        public iMetric[] DriveMetrics
        {
            get
            {
                if (_loadEnabled)
                {
                    return Metrics;
                }
                else
                {
                    return Metrics.Where(m => m.Key != MetricKey.DriveLoad).ToArray();
                }
            }
        }

        private bool _loadEnabled { get; set; }

        private bool _spaceEnabled { get; set; }

        private bool _disposed { get; set; } = false;

        public enum State : byte
        {
            NoLoadBar,
            LoadBarInline,
            LoadBarStacked
        }
    }

    // Network throughput is measured from NetworkInterface byte counters sampled each
    // poll; no perf counters, no name-mangling between counter instances and adapters.
    public class NetworkMonitor : BaseMonitor
    {
        public NetworkMonitor(NetworkInterface nic, string name, string extIP, MetricConfig[] metrics, bool showName = true, bool roundAll = false, bool useBytes = false, double bandwidthInAlert = 0, double bandwidthOutAlert = 0) : base(nic.Id, name, showName)
        {
            _nic = nic;

            iConverter _converter;

            if (useBytes)
            {
                _converter = BytesPerSecondConverter.Instance;
            }
            else
            {
                _converter = BitsPerSecondConverter.Instance;
            }

            List<iMetric> _metrics = new List<iMetric>();

            if (metrics.IsEnabled(MetricKey.NetworkIP))
            {
                string _ipAddress = GetAdapterIPAddress(nic);

                if (!string.IsNullOrEmpty(_ipAddress))
                {
                    _metrics.Add(new IPMetric(_ipAddress, MetricKey.NetworkIP, DataType.IP));
                }
            }

            if (!string.IsNullOrEmpty(extIP))
            {
                _metrics.Add(new IPMetric(extIP, MetricKey.NetworkExtIP, DataType.IP));
            }

            if (metrics.IsEnabled(MetricKey.NetworkIn))
            {
                InMetric = new BaseMetric(MetricKey.NetworkIn, DataType.kbps, null, roundAll, bandwidthInAlert, _converter);
                _metrics.Add(InMetric);
            }

            if (metrics.IsEnabled(MetricKey.NetworkOut))
            {
                OutMetric = new BaseMetric(MetricKey.NetworkOut, DataType.kbps, null, roundAll, bandwidthOutAlert, _converter);
                _metrics.Add(OutMetric);
            }

            Metrics = _metrics.ToArray();
        }

        ~NetworkMonitor()
        {
            Dispose(false);
        }

        public static IEnumerable<HardwareConfig> GetHardware()
        {
            return GetAdapters()
                .OrderBy(n => n.Name)
                .Select(n => new HardwareConfig() { ID = n.Id, Name = n.Name, ActualName = n.Name });
        }

        private static IEnumerable<NetworkInterface> GetAdapters()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n =>
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    n.OperationalStatus == OperationalStatus.Up);
        }

        public static iMonitor[] GetInstances(HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters)
        {
            bool _showName = parameters.GetValue<bool>(ParamKey.HardwareNames);
            bool _roundAll = parameters.GetValue<bool>(ParamKey.RoundAll);
            bool _useBytes = parameters.GetValue<bool>(ParamKey.UseBytes);
            int _bandwidthInAlert = parameters.GetValue<int>(ParamKey.BandwidthInAlert);
            int _bandwidthOutAlert = parameters.GetValue<int>(ParamKey.BandwidthOutAlert);

            string _extIP = null;

            if (metrics.IsEnabled(MetricKey.NetworkExtIP))
            {
                _extIP = GetExternalIPAddress();
            }

            return (
                from nic in GetAdapters()
                join c in hardwareConfig on nic.Id equals c.ID into merged
                from n in merged.DefaultIfEmpty(new HardwareConfig() { ID = nic.Id, Name = nic.Name, ActualName = nic.Name }).Select(n => { n.ActualName = nic.Name; return n; })
                where n.Enabled
                orderby n.Order descending, n.Name ascending
                select new NetworkMonitor(nic, n.Name ?? n.ActualName, _extIP, metrics, _showName, _roundAll, _useBytes, _bandwidthInAlert, _bandwidthOutAlert)
                ).ToArray();
        }

        public override void Update()
        {
            if (InMetric == null && OutMetric == null)
            {
                base.Update();
                return;
            }

            try
            {
                IPInterfaceStatistics _stats = _nic.GetIPStatistics();

                long _now = Stopwatch.GetTimestamp();

                if (_lastTimestamp > 0)
                {
                    double _seconds = (_now - _lastTimestamp) / (double)Stopwatch.Frequency;

                    if (_seconds > 0d)
                    {
                        if (InMetric != null)
                        {
                            double _bytesIn = Math.Max(0, _stats.BytesReceived - _lastBytesReceived);
                            InMetric.Update(_bytesIn / _seconds);
                        }

                        if (OutMetric != null)
                        {
                            double _bytesOut = Math.Max(0, _stats.BytesSent - _lastBytesSent);
                            OutMetric.Update(_bytesOut / _seconds);
                        }
                    }
                }

                _lastBytesReceived = _stats.BytesReceived;
                _lastBytesSent = _stats.BytesSent;
                _lastTimestamp = _now;
            }
            catch (NetworkInformationException)
            {
                return;
            }

            base.Update();
        }

        private static string GetAdapterIPAddress(NetworkInterface nic)
        {
            foreach (IPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return unicast.Address.ToString();
                }
            }

            return null;
        }

        private static string GetExternalIPAddress()
        {
            try
            {
                using (HttpClient _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) })
                {
                    return _client.GetStringAsync(Constants.URLs.IPIFY).GetAwaiter().GetResult();
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        public iMetric InMetric { get; private set; }

        public iMetric OutMetric { get; private set; }

        private NetworkInterface _nic { get; set; }

        private long _lastBytesReceived { get; set; }

        private long _lastBytesSent { get; set; }

        private long _lastTimestamp { get; set; }
    }

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
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
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
            Update(_counter.NextValue());
        }

        private PerformanceCounter _counter { get; set; }

        private bool _disposed { get; set; } = false;
    }

    [Serializable]
    public enum MonitorType : byte
    {
        CPU,
        RAM,
        GPU,
        HD,
        Network
    }

    public class MonitorConfig : INotifyPropertyChanged, ICloneable
    {
        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public MonitorConfig Clone()
        {
            MonitorConfig _clone = (MonitorConfig)MemberwiseClone();
            _clone.Hardware = _clone.Hardware.Select(h => h.Clone()).ToArray();
            _clone.Params = _clone.Params.Select(p => p.Clone()).ToArray();

            if (_clone.HardwareOC != null)
            {
                _clone.HardwareOC = new ObservableCollection<HardwareConfig>(_clone.HardwareOC.Select(h => h.Clone()));
            }

            return _clone;
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        private MonitorType _type { get; set; }

        public MonitorType Type
        {
            get
            {
                return _type;
            }
            set
            {
                _type = value;

                NotifyPropertyChanged("Type");
            }
        }

        private bool _enabled { get; set; }

        public bool Enabled
        {
            get
            {
                return _enabled;
            }
            set
            {
                _enabled = value;

                NotifyPropertyChanged("Enabled");
            }
        }

        private byte _order { get; set; }

        public byte Order
        {
            get
            {
                return _order;
            }
            set
            {
                _order = value;

                NotifyPropertyChanged("Order");
            }
        }

        private HardwareConfig[] _hardware { get; set; }

        public HardwareConfig[] Hardware
        {
            get
            {
                return _hardware;
            }
            set
            {
                _hardware = value;

                NotifyPropertyChanged("Hardware");
            }
        }

        private ObservableCollection<HardwareConfig> _hardwareOC { get; set; }

        [JsonIgnore]
        public ObservableCollection<HardwareConfig> HardwareOC
        {
            get
            {
                return _hardwareOC;
            }
            set
            {
                _hardwareOC = value;

                NotifyPropertyChanged("HardwareOC");
            }
        }

        private MetricConfig[] _metrics { get; set; }

        public MetricConfig[] Metrics
        {
            get
            {
                return _metrics;
            }
            set
            {
                _metrics = value;

                NotifyPropertyChanged("Metrics");
            }
        }

        private ConfigParam[] _params { get; set; }

        public ConfigParam[] Params
        {
            get
            {
                return _params;
            }
            set
            {
                _params = value;

                NotifyPropertyChanged("Params");
            }
        }

        [JsonIgnore]
        public string Name
        {
            get
            {
                return Type.GetDescription();
            }
        }

        public static MonitorConfig[] CheckConfig(MonitorConfig[] config)
        {
            MonitorConfig[] _default = Default;

            if (config == null)
            {
                return _default;
            }

            config = (
                from def in _default
                join rec in config on def.Type equals rec.Type into merged
                from newrec in merged.DefaultIfEmpty(def)
                select newrec
                ).ToArray();

            foreach (MonitorConfig _record in config)
            {
                MonitorConfig _defaultRecord = _default.Single(d => d.Type == _record.Type);

                if (_record.Hardware == null)
                {
                    _record.Hardware = _defaultRecord.Hardware;
                }

                if (_record.Metrics == null)
                {
                    _record.Metrics = _defaultRecord.Metrics;
                }
                else
                {
                    _record.Metrics = (
                        from def in _defaultRecord.Metrics
                        join metric in _record.Metrics on def.Key equals metric.Key into merged
                        from newmetric in merged.DefaultIfEmpty(def)
                        select newmetric
                        ).ToArray();
                }

                if (_record.Params == null)
                {
                    _record.Params = _defaultRecord.Params;
                }
                else
                {
                    _record.Params = (
                        from def in _defaultRecord.Params
                        join param in _record.Params on def.Key equals param.Key into merged
                        from newparam in merged.DefaultIfEmpty(def)
                        select newparam
                        ).ToArray();
                }
            }

            return config;
        }

        public static MonitorConfig[] Default
        {
            get
            {
                return new MonitorConfig[5]
                {
                    new MonitorConfig()
                    {
                        Type = MonitorType.CPU,
                        Enabled = true,
                        Order = 5,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[6]
                        {
                            new MetricConfig(MetricKey.CPUClock, true),
                            new MetricConfig(MetricKey.CPUTemp, true),
                            new MetricConfig(MetricKey.CPUVoltage, true),
                            new MetricConfig(MetricKey.CPUFan, true),
                            new MetricConfig(MetricKey.CPULoad, true),
                            new MetricConfig(MetricKey.CPUCoreLoad, true)
                        },
                        Params = new ConfigParam[8]
                        {
                            ConfigParam.Defaults.HardwareNames,
                            ConfigParam.Defaults.RoundAll,
                            ConfigParam.Defaults.AllCoreClocks,
                            ConfigParam.Defaults.UseGHz,
                            ConfigParam.Defaults.UseFahrenheit,
                            ConfigParam.Defaults.TempAlert,
                            ConfigParam.Defaults.UseWatts,
                            ConfigParam.Defaults.ShowFanRPM
                        }
                    },
                    new MonitorConfig()
                    {
                        Type = MonitorType.RAM,
                        Enabled = true,
                        Order = 4,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[5]
                        {
                            new MetricConfig(MetricKey.RAMClock, true),
                            new MetricConfig(MetricKey.RAMVoltage, true),
                            new MetricConfig(MetricKey.RAMLoad, true),
                            new MetricConfig(MetricKey.RAMUsed, true),
                            new MetricConfig(MetricKey.RAMFree, true)
                        },
                        Params = new ConfigParam[2]
                        {
                            ConfigParam.Defaults.NoHardwareNames,
                            ConfigParam.Defaults.RoundAll
                        }
                    },
                    new MonitorConfig()
                    {
                        Type = MonitorType.GPU,
                        Enabled = true,
                        Order = 3,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[7]
                        {
                            new MetricConfig(MetricKey.GPUCoreClock, true),
                            new MetricConfig(MetricKey.GPUVRAMClock, true),
                            new MetricConfig(MetricKey.GPUCoreLoad, true),
                            new MetricConfig(MetricKey.GPUVRAMLoad, true),
                            new MetricConfig(MetricKey.GPUVoltage, true),
                            new MetricConfig(MetricKey.GPUTemp, true),
                            new MetricConfig(MetricKey.GPUFan, true)
                        },
                        Params = new ConfigParam[8]
                        {
                            ConfigParam.Defaults.HardwareNames,
                            ConfigParam.Defaults.RoundAll,
                            ConfigParam.Defaults.UseGHz,
                            ConfigParam.Defaults.UseFahrenheit,
                            ConfigParam.Defaults.TempAlert,
                            ConfigParam.Defaults.UseWatts,
                            ConfigParam.Defaults.ShowVRAMGB,
                            new ConfigParam() { Key = ParamKey.ShowFanRPM, Value = false }
                        }
                    },
                    new MonitorConfig()
                    {
                        Type = MonitorType.HD,
                        Enabled = true,
                        Order = 2,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[6]
                        {
                            new MetricConfig(MetricKey.DriveLoadBar, true),
                            new MetricConfig(MetricKey.DriveLoad, true),
                            new MetricConfig(MetricKey.DriveUsed, true),
                            new MetricConfig(MetricKey.DriveFree, true),
                            new MetricConfig(MetricKey.DriveRead, true),
                            new MetricConfig(MetricKey.DriveWrite, true)
                        },
                        Params = new ConfigParam[2]
                        {
                            ConfigParam.Defaults.RoundAll,
                            ConfigParam.Defaults.UsedSpaceAlert
                        }
                    },
                    new MonitorConfig()
                    {
                        Type = MonitorType.Network,
                        Enabled = true,
                        Order = 1,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[4]
                        {
                            new MetricConfig(MetricKey.NetworkIP, true),
                            new MetricConfig(MetricKey.NetworkExtIP, false),
                            new MetricConfig(MetricKey.NetworkIn, true),
                            new MetricConfig(MetricKey.NetworkOut, true)
                        },
                        Params = new ConfigParam[5]
                        {
                            ConfigParam.Defaults.HardwareNames,
                            ConfigParam.Defaults.RoundAll,
                            ConfigParam.Defaults.UseBytes,
                            ConfigParam.Defaults.BandwidthInAlert,
                            ConfigParam.Defaults.BandwidthOutAlert
                        }
                    }
                };
            }
        }
    }

    public class HardwareConfig : INotifyPropertyChanged, ICloneable
    {
        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public HardwareConfig Clone()
        {
            return (HardwareConfig)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        private string _id { get; set; }

        public string ID
        {
            get
            {
                return _id;
            }
            set
            {
                _id = value;

                NotifyPropertyChanged("ID");
            }
        }

        private string _name { get; set; }

        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;

                NotifyPropertyChanged("Name");
            }
        }

        private string _actualName { get; set; }

        public string ActualName
        {
            get
            {
                return _actualName;
            }
            set
            {
                _actualName = value;

                NotifyPropertyChanged("ActualName");
            }
        }

        private bool _enabled { get; set; } = true;

        public bool Enabled
        {
            get
            {
                return _enabled;
            }
            set
            {
                _enabled = value;

                NotifyPropertyChanged("Enabled");
            }
        }

        private byte _order { get; set; } = 0;

        public byte Order
        {
            get
            {
                return _order;
            }
            set
            {
                _order = value;

                NotifyPropertyChanged("Order");
            }
        }
    }

    public class MetricConfig : INotifyPropertyChanged, ICloneable
    {
        public MetricConfig() { }

        public MetricConfig(MetricKey key, bool enabled)
        {
            Key = key;
            Enabled = enabled;
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ConfigParam Clone()
        {
            return (ConfigParam)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        private MetricKey _key { get; set; }

        public MetricKey Key
        {
            get
            {
                return _key;
            }
            set
            {
                _key = value;

                NotifyPropertyChanged("Key");
            }
        }

        private bool _enabled { get; set; }

        public bool Enabled
        {
            get
            {
                return _enabled;
            }
            set
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;

                NotifyPropertyChanged("Enabled");
            }
        }

        [JsonIgnore]
        public string Name
        {
            get
            {
                return Key.GetFullName();
            }
        }
    }

    [Serializable]
    public enum MetricKey : byte
    {
        CPUClock = 0,
        CPUTemp = 1,
        CPUVoltage = 2,
        CPUFan = 3,
        CPULoad = 4,
        CPUCoreLoad = 5,

        RAMClock = 6,
        RAMVoltage = 7,
        RAMLoad = 8,
        RAMUsed = 9,
        RAMFree = 10,

        GPUCoreClock = 11,
        GPUVRAMClock = 12,
        GPUCoreLoad = 13,
        GPUVRAMLoad = 14,
        GPUVoltage = 15,
        GPUTemp = 16,
        GPUFan = 17,

        NetworkIP = 26,
        NetworkExtIP = 27,
        NetworkIn = 18,
        NetworkOut = 19,

        DriveLoadBar = 20,
        DriveLoad = 21,
        DriveUsed = 22,
        DriveFree = 23,
        DriveRead = 24,
        DriveWrite = 25
    }

    public class ConfigParam : INotifyPropertyChanged, ICloneable
    {
        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ConfigParam Clone()
        {
            return (ConfigParam)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        private ParamKey _key { get; set; }

        public ParamKey Key
        {
            get
            {
                return _key;
            }
            set
            {
                _key = value;

                NotifyPropertyChanged("Key");
            }
        }

        private object _value { get; set; }

        [JsonConverter(typeof(ParamValueConverter))]
        public object Value
        {
            get
            {
                return _value;
            }
            set
            {
                if (value.GetType() == typeof(long))
                {
                    _value = Convert.ToInt32(value);
                }
                else
                {
                    _value = value;
                }

                NotifyPropertyChanged("Value");
            }
        }

        [JsonIgnore]
        public Type Type
        {
            get
            {
                return Value.GetType();
            }
        }

        [JsonIgnore]
        public string TypeString
        {
            get
            {
                return Type.ToString();
            }
        }

        [JsonIgnore]
        public string Name
        {
            get
            {
                switch (Key)
                {
                    case ParamKey.HardwareNames:
                        return Resources.SettingsShowHardwareNames;

                    case ParamKey.UseFahrenheit:
                        return Resources.SettingsUseFahrenheit;

                    case ParamKey.AllCoreClocks:
                        return Resources.SettingsAllCoreClocks;

                    case ParamKey.CoreLoads:
                        return Resources.SettingsCoreLoads;

                    case ParamKey.TempAlert:
                        return Resources.SettingsTemperatureAlert;

                    case ParamKey.DriveDetails:
                        return Resources.SettingsShowDriveDetails;

                    case ParamKey.UsedSpaceAlert:
                        return Resources.SettingsUsedSpaceAlert;

                    case ParamKey.BandwidthInAlert:
                        return Resources.SettingsBandwidthInAlert;

                    case ParamKey.BandwidthOutAlert:
                        return Resources.SettingsBandwidthOutAlert;

                    case ParamKey.UseBytes:
                        return Resources.SettingsUseBytesPerSecond;

                    case ParamKey.RoundAll:
                        return Resources.SettingsRoundAllDecimals;

                    case ParamKey.DriveSpace:
                        return Resources.SettingsShowDriveSpace;

                    case ParamKey.DriveIO:
                        return Resources.SettingsShowDriveIO;

                    case ParamKey.UseGHz:
                        return Resources.SettingsUseGHz;

                    case ParamKey.UseWatts:
                        return Resources.SettingsUseWatts;

                    case ParamKey.ShowVRAMGB:
                        return Resources.SettingsShowVRAMGB;

                    case ParamKey.ShowFanRPM:
                        return Resources.SettingsShowFanRPM;

                    default:
                        return "Unknown";
                }
            }
        }

        [JsonIgnore]
        public string Tooltip
        {
            get
            {
                switch (Key)
                {
                    case ParamKey.HardwareNames:
                        return Resources.SettingsShowHardwareNamesTooltip;

                    case ParamKey.UseFahrenheit:
                        return Resources.SettingsUseFahrenheitTooltip;

                    case ParamKey.AllCoreClocks:
                        return Resources.SettingsAllCoreClocksTooltip;

                    case ParamKey.CoreLoads:
                        return Resources.SettingsCoreLoadsTooltip;

                    case ParamKey.TempAlert:
                        return Resources.SettingsTemperatureAlertTooltip;

                    case ParamKey.DriveDetails:
                        return Resources.SettingsDriveDetailsTooltip;

                    case ParamKey.UsedSpaceAlert:
                        return Resources.SettingsUsedSpaceAlertTooltip;

                    case ParamKey.BandwidthInAlert:
                        return Resources.SettingsBandwidthInAlertTooltip;

                    case ParamKey.BandwidthOutAlert:
                        return Resources.SettingsBandwidthOutAlertTooltip;

                    case ParamKey.UseBytes:
                        return Resources.SettingsUseBytesPerSecondTooltip;

                    case ParamKey.RoundAll:
                        return Resources.SettingsRoundAllDecimalsTooltip;

                    case ParamKey.DriveSpace:
                        return Resources.SettingsShowDriveSpaceTooltip;

                    case ParamKey.DriveIO:
                        return Resources.SettingsShowDriveIOTooltip;

                    case ParamKey.UseGHz:
                        return Resources.SettingsUseGHzTooltip;

                    case ParamKey.UseWatts:
                        return Resources.SettingsUseWattsTooltip;

                    case ParamKey.ShowVRAMGB:
                        return Resources.SettingsShowVRAMGBTooltip;

                    case ParamKey.ShowFanRPM:
                        return Resources.SettingsShowFanRPMTooltip;

                    default:
                        return "Unknown";
                }
            }
        }

        public static class Defaults
        {
            public static ConfigParam HardwareNames
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.HardwareNames, Value = true };
                }
            }

            public static ConfigParam NoHardwareNames
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.HardwareNames, Value = false };
                }
            }

            public static ConfigParam UseFahrenheit
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.UseFahrenheit, Value = false };
                }
            }

            public static ConfigParam AllCoreClocks
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.AllCoreClocks, Value = false };
                }
            }

            public static ConfigParam CoreLoads
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.CoreLoads, Value = true };
                }
            }

            public static ConfigParam TempAlert
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.TempAlert, Value = 0 };
                }
            }

            public static ConfigParam DriveDetails
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.DriveDetails, Value = false };
                }
            }

            public static ConfigParam UsedSpaceAlert
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.UsedSpaceAlert, Value = 0 };
                }
            }

            public static ConfigParam BandwidthInAlert
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.BandwidthInAlert, Value = 0 };
                }
            }

            public static ConfigParam BandwidthOutAlert
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.BandwidthOutAlert, Value = 0 };
                }
            }

            public static ConfigParam UseBytes
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.UseBytes, Value = false };
                }
            }

            public static ConfigParam RoundAll
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.RoundAll, Value = false };
                }
            }

            public static ConfigParam ShowDriveSpace
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.DriveSpace, Value = true };
                }
            }

            public static ConfigParam ShowDriveIO
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.DriveIO, Value = true };
                }
            }

            public static ConfigParam UseGHz
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.UseGHz, Value = false };
                }
            }

            public static ConfigParam UseWatts
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.UseWatts, Value = false };
                }
            }

            public static ConfigParam ShowVRAMGB
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.ShowVRAMGB, Value = false };
                }
            }

            public static ConfigParam ShowFanRPM
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.ShowFanRPM, Value = true };
                }
            }
        }
    }

    // ConfigParam.Value is bool or int depending on the parameter; System.Text.Json
    // would otherwise deserialize it as a JsonElement.
    public class ParamValueConverter : JsonConverter<object>
    {
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;

                case JsonTokenType.False:
                    return false;

                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out int _int))
                    {
                        return _int;
                    }
                    return reader.GetDouble();

                case JsonTokenType.String:
                    return reader.GetString();

                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
        }
    }

    [Serializable]
    public enum ParamKey : byte
    {
        HardwareNames,
        UseFahrenheit,
        AllCoreClocks,
        CoreLoads,
        TempAlert,
        DriveDetails,
        UsedSpaceAlert,
        BandwidthInAlert,
        BandwidthOutAlert,
        UseBytes,
        RoundAll,
        DriveSpace,
        DriveIO,
        UseGHz,
        UseWatts,
        ShowVRAMGB,
        ShowFanRPM
    }

    public enum DataType : byte
    {
        Dynamic,
        Bit,
        Kilobit,
        Megabit,
        Gigabit,
        Byte,
        Kilobyte,
        Megabyte,
        Gigabyte,
        bps,
        kbps,
        Mbps,
        Gbps,
        Bps,
        kBps,
        MBps,
        GBps,
        MHz,
        GHz,
        Voltage,
        Watt,
        Percent,
        RPM,
        Celcius,
        Fahrenheit,
        IP
    }

    public interface iConverter
    {
        void Convert(ref double value);

        void Convert(ref double value, out double normalized, out DataType targetType);

        DataType TargetType { get; }

        bool IsDynamic { get; }
    }

    public class CelciusToFahrenheit : iConverter
    {
        private CelciusToFahrenheit() { }

        public void Convert(ref double value)
        {
            value = value * 1.8d + 32d;
        }

        public void Convert(ref double value, out double normalized, out DataType targetType)
        {
            Convert(ref value);
            normalized = value;
            targetType = TargetType;
        }

        public DataType TargetType
        {
            get
            {
                return DataType.Fahrenheit;
            }
        }

        public bool IsDynamic
        {
            get
            {
                return false;
            }
        }

        private static CelciusToFahrenheit _instance { get; set; } = null;

        public static CelciusToFahrenheit Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CelciusToFahrenheit();
                }

                return _instance;
            }
        }
    }

    public class MHzToGHz : iConverter
    {
        private MHzToGHz() { }

        public void Convert(ref double value)
        {
            value = value / 1000d;
        }

        public void Convert(ref double value, out double normalized, out DataType targetType)
        {
            Convert(ref value);
            normalized = value;
            targetType = TargetType;
        }

        public DataType TargetType
        {
            get
            {
                return DataType.GHz;
            }
        }

        public bool IsDynamic
        {
            get
            {
                return false;
            }
        }

        private static MHzToGHz _instance { get; set; } = null;

        public static MHzToGHz Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MHzToGHz();
                }

                return _instance;
            }
        }
    }

    public class BitsPerSecondConverter : iConverter
    {
        private BitsPerSecondConverter() { }

        public void Convert(ref double value)
        {
            double _normalized;
            DataType _dataType;

            Convert(ref value, out _normalized, out _dataType);
        }

        public void Convert(ref double value, out double normalized, out DataType targetType)
        {
            normalized = value /= 128d;

            if (value < 1024d)
            {
                targetType = DataType.kbps;
                return;
            }
            else if (value < 1048576d)
            {
                value /= 1024d;
                targetType = DataType.Mbps;
                return;
            }
            else
            {
                value /= 1048576d;
                targetType = DataType.Gbps;
                return;
            }
        }

        public DataType TargetType
        {
            get
            {
                return DataType.kbps;
            }
        }

        public bool IsDynamic
        {
            get
            {
                return true;
            }
        }

        private static BitsPerSecondConverter _instance { get; set; } = null;

        public static BitsPerSecondConverter Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BitsPerSecondConverter();
                }

                return _instance;
            }
        }
    }

    public class BytesPerSecondConverter : iConverter
    {
        private BytesPerSecondConverter() { }

        public void Convert(ref double value)
        {
            double _normalized;
            DataType _dataType;

            Convert(ref value, out _normalized, out _dataType);
        }

        public void Convert(ref double value, out double normalized, out DataType targetType)
        {
            normalized = value /= 1024d;

            if (value < 1024d)
            {
                targetType = DataType.kBps;
                return;
            }
            else if (value < 1048576d)
            {
                value /= 1024d;
                targetType = DataType.MBps;
                return;
            }
            else
            {
                value /= 1048576d;
                targetType = DataType.GBps;
                return;
            }
        }

        public DataType TargetType
        {
            get
            {
                return DataType.kBps;
            }
        }

        public bool IsDynamic
        {
            get
            {
                return true;
            }
        }

        private static BytesPerSecondConverter _instance { get; set; } = null;

        public static BytesPerSecondConverter Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BytesPerSecondConverter();
                }

                return _instance;
            }
        }
    }

    public static class Extensions
    {
        public static bool IsEnabled(this MetricConfig[] metrics, MetricKey key)
        {
            return metrics.Any(m => m.Key == key && m.Enabled);
        }

        public static HardwareType[] GetHardwareTypes(this MonitorType type)
        {
            switch (type)
            {
                case MonitorType.CPU:
                    return new HardwareType[1] { HardwareType.Cpu };

                case MonitorType.RAM:
                    return new HardwareType[1] { HardwareType.Memory };

                case MonitorType.GPU:
                    return new HardwareType[3] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel };

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }

        public static string GetDescription(this MonitorType type)
        {
            switch (type)
            {
                case MonitorType.CPU:
                    return Resources.CPU;

                case MonitorType.RAM:
                    return Resources.RAM;

                case MonitorType.GPU:
                    return Resources.GPU;

                case MonitorType.HD:
                    return Resources.Drives;

                case MonitorType.Network:
                    return Resources.Network;

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }

        public static T GetValue<T>(this ConfigParam[] parameters, ParamKey key)
        {
            return (T)parameters.Single(p => p.Key == key).Value;
        }

        public static string GetFullName(this MetricKey key)
        {
            switch (key)
            {
                case MetricKey.CPUClock:
                    return Resources.CPUClock;

                case MetricKey.CPUTemp:
                    return Resources.CPUTemp;

                case MetricKey.CPUVoltage:
                    return Resources.CPUVoltage;

                case MetricKey.CPUFan:
                    return Resources.CPUFan;

                case MetricKey.CPULoad:
                    return Resources.CPULoad;

                case MetricKey.CPUCoreLoad:
                    return Resources.CPUCoreLoad;

                case MetricKey.RAMClock:
                    return Resources.RAMClock;

                case MetricKey.RAMVoltage:
                    return Resources.RAMVoltage;

                case MetricKey.RAMLoad:
                    return Resources.RAMLoad;

                case MetricKey.RAMUsed:
                    return Resources.RAMUsed;

                case MetricKey.RAMFree:
                    return Resources.RAMFree;

                case MetricKey.GPUCoreClock:
                    return Resources.GPUCoreClock;

                case MetricKey.GPUVRAMClock:
                    return Resources.GPUVRAMClock;

                case MetricKey.GPUCoreLoad:
                    return Resources.GPUCoreLoad;

                case MetricKey.GPUVRAMLoad:
                    return Resources.GPUVRAMLoad;

                case MetricKey.GPUVoltage:
                    return Resources.GPUVoltage;

                case MetricKey.GPUTemp:
                    return Resources.GPUTemp;

                case MetricKey.GPUFan:
                    return Resources.GPUFan;

                case MetricKey.NetworkIP:
                    return Resources.NetworkIP;

                case MetricKey.NetworkExtIP:
                    return Resources.NetworkExtIP;

                case MetricKey.NetworkIn:
                    return Resources.NetworkIn;

                case MetricKey.NetworkOut:
                    return Resources.NetworkOut;

                case MetricKey.DriveLoadBar:
                    return Resources.DriveLoadBar;

                case MetricKey.DriveLoad:
                    return Resources.DriveLoad;

                case MetricKey.DriveUsed:
                    return Resources.DriveUsed;

                case MetricKey.DriveFree:
                    return Resources.DriveFree;

                case MetricKey.DriveRead:
                    return Resources.DriveRead;

                case MetricKey.DriveWrite:
                    return Resources.DriveWrite;

                default:
                    return "Unknown";
            }
        }

        public static string GetLabel(this MetricKey key)
        {
            switch (key)
            {
                case MetricKey.CPUClock:
                    return Resources.CPUClockLabel;

                case MetricKey.CPUTemp:
                    return Resources.CPUTempLabel;

                case MetricKey.CPUVoltage:
                    return Resources.CPUVoltageLabel;

                case MetricKey.CPUFan:
                    return Resources.CPUFanLabel;

                case MetricKey.CPULoad:
                    return Resources.CPULoadLabel;

                case MetricKey.CPUCoreLoad:
                    return Resources.CPUCoreLoadLabel;

                case MetricKey.RAMClock:
                    return Resources.RAMClockLabel;

                case MetricKey.RAMVoltage:
                    return Resources.RAMVoltageLabel;

                case MetricKey.RAMLoad:
                    return Resources.RAMLoadLabel;

                case MetricKey.RAMUsed:
                    return Resources.RAMUsedLabel;

                case MetricKey.RAMFree:
                    return Resources.RAMFreeLabel;

                case MetricKey.GPUCoreClock:
                    return Resources.GPUCoreClockLabel;

                case MetricKey.GPUVRAMClock:
                    return Resources.GPUVRAMClockLabel;

                case MetricKey.GPUCoreLoad:
                    return Resources.GPUCoreLoadLabel;

                case MetricKey.GPUVRAMLoad:
                    return Resources.GPUVRAMLoadLabel;

                case MetricKey.GPUVoltage:
                    return Resources.GPUVoltageLabel;

                case MetricKey.GPUTemp:
                    return Resources.GPUTempLabel;

                case MetricKey.GPUFan:
                    return Resources.GPUFanLabel;

                case MetricKey.NetworkIP:
                    return Resources.NetworkIPLabel;

                case MetricKey.NetworkExtIP:
                    return Resources.NetworkExtIPLabel;

                case MetricKey.NetworkIn:
                    return Resources.NetworkInLabel;

                case MetricKey.NetworkOut:
                    return Resources.NetworkOutLabel;

                case MetricKey.DriveLoadBar:
                    return Resources.DriveLoadBarLabel;

                case MetricKey.DriveLoad:
                    return Resources.DriveLoadLabel;

                case MetricKey.DriveUsed:
                    return Resources.DriveUsedLabel;

                case MetricKey.DriveFree:
                    return Resources.DriveFreeLabel;

                case MetricKey.DriveRead:
                    return Resources.DriveReadLabel;

                case MetricKey.DriveWrite:
                    return Resources.DriveWriteLabel;

                default:
                    return "Unknown";
            }
        }

        public static string GetAppend(this DataType type)
        {
            switch (type)
            {
                case DataType.Bit:
                    return " b";

                case DataType.Kilobit:
                    return " kb";

                case DataType.Megabit:
                    return " mb";

                case DataType.Gigabit:
                    return " gb";

                case DataType.Byte:
                    return " B";

                case DataType.Kilobyte:
                    return " KB";

                case DataType.Megabyte:
                    return " MB";

                case DataType.Gigabyte:
                    return " GB";

                case DataType.bps:
                    return " bps";

                case DataType.kbps:
                    return " kbps";

                case DataType.Mbps:
                    return " Mbps";

                case DataType.Gbps:
                    return " Gbps";

                case DataType.Bps:
                    return " B/s";

                case DataType.kBps:
                    return " kB/s";

                case DataType.MBps:
                    return " MB/s";

                case DataType.GBps:
                    return " GB/s";

                case DataType.MHz:
                    return " MHz";

                case DataType.GHz:
                    return " GHz";

                case DataType.Voltage:
                    return " V";

                case DataType.Watt:
                    return " W";

                case DataType.Percent:
                    return "%";

                case DataType.RPM:
                    return " RPM";

                case DataType.Celcius:
                    return " C";

                case DataType.Fahrenheit:
                    return " F";

                case DataType.IP:
                    return string.Empty;

                default:
                    throw new ArgumentException("Invalid DataType.");
            }
        }

        public static double Round(this double value, bool doRound)
        {
            if (!doRound)
            {
                return value;
            }

            return Math.Round(value);
        }
    }
}