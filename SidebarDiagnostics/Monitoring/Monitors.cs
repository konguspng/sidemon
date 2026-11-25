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
            if (PropertyChanged == null)
            {
                return;
            }

            UiNotify.Post(this, propertyName, () =>
            {
                var _h = PropertyChanged;
                if (_h != null) _h(this, new PropertyChangedEventArgs(propertyName));
            });
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

                case MonitorType.Motherboard:
                    InitMotherboard(
                        metrics,
                        parameters.GetValue<bool>(ParamKey.RoundAll),
                        parameters.GetValue<bool>(ParamKey.UseFahrenheit),
                        parameters.GetValue<int>(ParamKey.TempAlert)
                        );
                    break;

                case MonitorType.Battery:
                    InitBattery(
                        metrics,
                        parameters.GetValue<bool>(ParamKey.RoundAll)
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
            base.Update();
        }

        private void UpdateHardware()
        {
            // Removed to avoid redundant hardware updates. Hardware is updated in MonitorManager.Update().
        }

        internal static IEnumerable<ISensor> BoardSensors(IHardware hardware)
        {
            if (hardware == null)
            {
                yield break;
            }

            if (hardware.Sensors != null)
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    yield return sensor;
                }
            }

            if (hardware.SubHardware != null)
            {
                foreach (IHardware sub in hardware.SubHardware)
                {
                    foreach (ISensor sensor in BoardSensors(sub))
                    {
                        yield return sensor;
                    }
                }
            }
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
                        _voltage = BoardSensors(board).Where(s => s.SensorType == SensorType.Voltage && s.Name.Contains("CPU")).FirstOrDefault();
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
                    _tempSensor = BoardSensors(board).Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("CPU")).FirstOrDefault();
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
                    ISensor[] _boardSensors = BoardSensors(board).ToArray();

                    if (showFanRPM)
                    {
                        // 1. Prioritize RPM sensor
                        _fanSensor = _boardSensors.Where(s => s.SensorType == SensorType.Fan && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                            ?? _boardSensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault();
                        // 2. Fallback to Control (Percent) sensor
                        if (_fanSensor == null)
                        {
                            _fanSensor = _boardSensors.Where(s => s.SensorType == SensorType.Control && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                                ?? _boardSensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault();
                        }
                    }
                    else
                    {
                        // 1. Prioritize Control (Percent) sensor
                        _fanSensor = _boardSensors.Where(s => s.SensorType == SensorType.Control && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                            ?? _boardSensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault();
                        // 2. Fallback to RPM sensor
                        if (_fanSensor == null)
                        {
                            _fanSensor = _boardSensors.Where(s => s.SensorType == SensorType.Fan && s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault()
                                ?? _boardSensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault();
                        }
                    }
                }

                if (_fanSensor == null)
                {
                    ISensor[] _hwSensors = BoardSensors(_hardware).ToArray();

                    if (showFanRPM)
                    {
                        _fanSensor = _hwSensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault()
                            ?? _hwSensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault();
                    }
                    else
                    {
                        _fanSensor = _hwSensors.Where(s => s.SensorType == SensorType.Control).FirstOrDefault()
                            ?? _hwSensors.Where(s => s.SensorType == SensorType.Fan).FirstOrDefault();
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
                    _voltage = BoardSensors(board).Where(s => s.SensorType == SensorType.Voltage && s.Name.Contains("RAM")).FirstOrDefault();
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
                ISensor[] _gpuSensors = BoardSensors(_hardware).ToArray();

                if (showFanRPM)
                {
                    // 1. Prioritize RPM sensor
                    _fanSensor = _gpuSensors.Where(s => s.SensorType == SensorType.Fan).OrderBy(s => s.Index).FirstOrDefault();
                    // 2. Fallback to Control (Percent) sensor
                    if (_fanSensor == null)
                    {
                        _fanSensor = _gpuSensors.Where(s => s.SensorType == SensorType.Control).OrderBy(s => s.Index).FirstOrDefault();
                    }
                }
                else
                {
                    // 1. Prioritize Control (Percent) sensor
                    _fanSensor = _gpuSensors.Where(s => s.SensorType == SensorType.Control).OrderBy(s => s.Index).FirstOrDefault();
                    // 2. Fallback to RPM sensor
                    if (_fanSensor == null)
                    {
                        _fanSensor = _gpuSensors.Where(s => s.SensorType == SensorType.Fan).OrderBy(s => s.Index).FirstOrDefault();
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

        public void InitMotherboard(MetricConfig[] metrics, bool roundAll, bool useFahrenheit, double tempAlert)
        {
            List<iMetric> _sensorList = new List<iMetric>();
            ISensor[] _boardSensors = BoardSensors(_hardware).ToArray();

            if (metrics.IsEnabled(MetricKey.MotherboardTemp))
            {
                ISensor _tempSensor = _boardSensors.Where(s => s.SensorType == SensorType.Temperature).OrderBy(s => s.Index).FirstOrDefault();

                if (_tempSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_tempSensor, MetricKey.MotherboardTemp, DataType.Celcius, null, roundAll, tempAlert, (useFahrenheit ? CelciusToFahrenheit.Instance : null)));
                }
            }

            if (metrics.IsEnabled(MetricKey.MotherboardFan))
            {
                ISensor _fanSensor = _boardSensors.Where(s => s.SensorType == SensorType.Fan).OrderBy(s => s.Index).FirstOrDefault()
                    ?? _boardSensors.Where(s => s.SensorType == SensorType.Control).OrderBy(s => s.Index).FirstOrDefault();

                if (_fanSensor != null)
                {
                    DataType fanDataType = _fanSensor.SensorType == SensorType.Control ? DataType.Percent : DataType.RPM;
                    _sensorList.Add(new OHMMetric(_fanSensor, MetricKey.MotherboardFan, fanDataType));
                }
            }

            if (metrics.IsEnabled(MetricKey.MotherboardVoltage))
            {
                ISensor _voltage = _boardSensors.Where(s => s.SensorType == SensorType.Voltage).OrderBy(s => s.Index).FirstOrDefault();

                if (_voltage != null)
                {
                    _sensorList.Add(new OHMMetric(_voltage, MetricKey.MotherboardVoltage, DataType.Voltage, null, roundAll));
                }
            }

            Metrics = _sensorList.ToArray();
        }

        public void InitBattery(MetricConfig[] metrics, bool roundAll)
        {
            List<iMetric> _sensorList = new List<iMetric>();

            if (metrics.IsEnabled(MetricKey.BatteryLevel))
            {
                ISensor _levelSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Level).FirstOrDefault();

                if (_levelSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_levelSensor, MetricKey.BatteryLevel, DataType.Percent, null, roundAll));
                }
            }

            if (metrics.IsEnabled(MetricKey.BatteryVoltage))
            {
                ISensor _voltage = _hardware.Sensors.Where(s => s.SensorType == SensorType.Voltage).FirstOrDefault();

                if (_voltage != null)
                {
                    _sensorList.Add(new OHMMetric(_voltage, MetricKey.BatteryVoltage, DataType.Voltage, null, roundAll));
                }
            }

            if (metrics.IsEnabled(MetricKey.BatteryRate))
            {
                // charge/discharge rate is only meaningful shown in Watts (no Ampere
                // DataType exists in this app); a Current-only sensor is skipped
                ISensor _rateSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.Power).FirstOrDefault();

                if (_rateSensor != null)
                {
                    _sensorList.Add(new OHMMetric(_rateSensor, MetricKey.BatteryRate, DataType.Watt, null, roundAll));
                }
            }

            if (metrics.IsEnabled(MetricKey.BatteryTimeRemaining))
            {
                ISensor _timeSensor = _hardware.Sensors.Where(s => s.SensorType == SensorType.TimeSpan).FirstOrDefault();

                if (_timeSensor != null)
                {
                    // LibreHardwareMonitor reports remaining time as a TimeSpan sensor
                    // in seconds; convert to minutes for a more readable metric.
                    _sensorList.Add(new OHMMetric(_timeSensor, MetricKey.BatteryTimeRemaining, DataType.Minute, null, roundAll, 0, SecondsToMinutes.Instance));
                }
            }

            Metrics = _sensorList.ToArray();
        }

        private IHardware _hardware { get; set; }

        private bool _disposed { get; set; } = false;
    }
}
