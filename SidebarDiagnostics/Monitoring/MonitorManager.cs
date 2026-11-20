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
                bool enableCpu = config.Any(c => c.Enabled && c.Type == MonitorType.CPU);
                bool enableGpu = config.Any(c => c.Enabled && c.Type == MonitorType.GPU);
                bool enableMotherboard = config.Any(c => c.Enabled && (
                    c.Type == MonitorType.Motherboard ||
                    c.Type == MonitorType.Fans ||
                    (c.Type == MonitorType.CPU && c.Metrics.Any(m => m.Enabled && (m.Key == MetricKey.CPUFan || m.Key == MetricKey.CPUVoltage))) ||
                    (c.Type == MonitorType.RAM && c.Metrics.Any(m => m.Enabled && m.Key == MetricKey.RAMVoltage))
                ));
                bool enableMemory = config.Any(c => c.Enabled && c.Type == MonitorType.RAM);
                bool enableBattery = config.Any(c => c.Enabled && c.Type == MonitorType.Battery);
                if (TestHookForceHardwareFail != null && TestHookForceHardwareFail()) throw new ArgumentNullException("identity");
                _computer = new Computer()
                {
                    IsCpuEnabled = enableCpu,
                    IsControllerEnabled = enableMotherboard,
                    IsGpuEnabled = enableGpu,
                    IsStorageEnabled = false,
                    IsMotherboardEnabled = enableMotherboard,
                    IsMemoryEnabled = enableMemory,
                    IsNetworkEnabled = false,
                    IsBatteryEnabled = enableBattery
                };
                _computer.Open();
                _board = GetHardware(HardwareType.Motherboard).FirstOrDefault();

                UpdateBoard();

                MonitorPanels = config.Where(c => c.Enabled).OrderByDescending(c => c.Order).Select(c => NewPanel(c)).Where(p => p != null).ToArray();
            }
            catch (Exception e)
            {
                Utilities.ErrorLog.Write(e);

                if (e is ArgumentNullException) // ArgumentNullException("identity")
                {
                    HardwareLimited = true;
                    try { _computer?.Close(); } catch { }
                    try
                    {
                        _computer = new Computer()
                        {
                            IsCpuEnabled = false,
                            IsControllerEnabled = false,
                            IsGpuEnabled = false,
                            IsStorageEnabled = false,
                            IsMotherboardEnabled = false,
                            IsMemoryEnabled = true,
                            IsNetworkEnabled = false,
                            IsBatteryEnabled = true
                        };
                        _computer.Open();
                        _board = null;
                        
                        MonitorPanels = config.Where(c => c.Enabled).OrderByDescending(c => c.Order).Select(c => NewPanel(c)).Where(p => p != null).ToArray();
                    }
                    catch
                    {
                        MonitorPanels = Array.Empty<MonitorPanel>();
                    }
                }
                else
                {
                    MonitorPanels = Array.Empty<MonitorPanel>();
                }
            }
        }



        public bool HardwareLimited { get; private set; }
        public static Func<bool> TestHookForceHardwareFail { get; set; }

        public string HardwareLimitedReason { get { return global::SidebarDiagnostics.Framework.Resources.HardwareLimitedWarning; } }

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

                    foreach (Computer _probe in _probes)
                    {
                        try { _probe.Close(); } catch { }
                    }
                    _probes.Clear();

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

        private Dictionary<MonitorType, HardwareConfig[]> _cachedOfflineHardware = new Dictionary<MonitorType, HardwareConfig[]>();
        private readonly List<Computer> _probes = new List<Computer>();

        public HardwareConfig[] GetHardware(MonitorType type)
        {
            switch (type)
            {
                case MonitorType.CPU:
                case MonitorType.RAM:
                case MonitorType.GPU:
                case MonitorType.Motherboard:
                case MonitorType.Battery:
                    bool isEnabled = false;
                    if (_computer != null)
                    {
                        switch (type)
                        {
                            case MonitorType.CPU: isEnabled = _computer.IsCpuEnabled; break;
                            case MonitorType.RAM: isEnabled = _computer.IsMemoryEnabled; break;
                            case MonitorType.GPU: isEnabled = _computer.IsGpuEnabled; break;
                            case MonitorType.Motherboard: isEnabled = _computer.IsMotherboardEnabled; break;
                            case MonitorType.Battery: isEnabled = _computer.IsBatteryEnabled; break;
                        }
                    }

                    if (isEnabled)
                    {
                        return GetHardware(type.GetHardwareTypes()).Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name }).ToArray();
                    }
                    else
                    {
                        if (!_cachedOfflineHardware.TryGetValue(type, out var cached))
                        {
                            var probe = new Computer();
                            bool probeOpened = false;
                            cached = new HardwareConfig[0];
                            try
                            {
                                switch (type)
                                {
                                    case MonitorType.CPU: probe.IsCpuEnabled = true; break;
                                    case MonitorType.RAM: probe.IsMemoryEnabled = true; break;
                                    case MonitorType.GPU: probe.IsGpuEnabled = true; break;
                                    case MonitorType.Motherboard: probe.IsMotherboardEnabled = true; probe.IsControllerEnabled = true; break;
                                    case MonitorType.Battery: probe.IsBatteryEnabled = true; break;
                                }
                                probe.Open();
                                probeOpened = true;
                                var types = type.GetHardwareTypes();
                                cached = probe.Hardware.Where(h => types.Contains(h.HardwareType)).Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name }).ToArray();
                            }
                            catch (Exception _ex)
                            {
                                // probing is best-effort (driver / permission problems): never let it take the
                                // Settings window or the app down, just show no devices for this monitor
                                SidebarDiagnostics.Utilities.ErrorLog.Write(_ex);
                            }
                            finally
                            {
                                // LibreHardwareMonitor shares driver state between Computer instances:
                                // closing a probe while the live Computer is running breaks the live
                                // sensors (NullReferenceException in Cpu.Update every tick). Keep an
                                // opened probe alive (one per monitor type at most) and close it in
                                // Dispose together with the live Computer; only close a probe whose
                                // Open() failed.
                                if (probeOpened)
                                {
                                    _probes.Add(probe);
                                }
                                else
                                {
                                    try { probe.Close(); } catch { }
                                }
                            }
                            _cachedOfflineHardware[type] = cached;
                        }
                        return cached;
                    }

                case MonitorType.HD:
                    return DriveMonitor.GetHardware().ToArray();

                case MonitorType.Network:
                    return NetworkMonitor.GetHardware().ToArray();

                case MonitorType.AIUsage:
                    return UsageRegistry.GetHardware();

                case MonitorType.Fans:
                    if (_computer != null && _computer.IsMotherboardEnabled)
                    {
                        return FanMonitor.DiscoverFans(_board);
                    }
                    else
                    {
                        if (!_cachedOfflineHardware.TryGetValue(MonitorType.Fans, out var cachedFans))
                        {
                            var probe = new Computer();
                            bool probeOpened = false;
                            cachedFans = new HardwareConfig[0];
                            try
                            {
                                probe.IsMotherboardEnabled = true;
                                probe.IsControllerEnabled = true;
                                probe.Open();
                                probeOpened = true;
                                IHardware probeBoard = probe.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
                                probeBoard?.Update();
                                UpdateSubHardware(probeBoard);
                                cachedFans = FanMonitor.DiscoverFans(probeBoard);
                            }
                            catch (Exception _ex)
                            {
                                SidebarDiagnostics.Utilities.ErrorLog.Write(_ex);
                            }
                            finally
                            {
                                if (probeOpened)
                                {
                                    _probes.Add(probe);
                                }
                                else
                                {
                                    try { probe.Close(); } catch { }
                                }
                            }
                            _cachedOfflineHardware[MonitorType.Fans] = cachedFans;
                        }
                        return cachedFans;
                    }

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }

        public void Update()
        {
            if (_computer != null)
            {
                foreach (IHardware hw in _computer.Hardware)
                {
                    hw.Update();
                    UpdateSubHardware(hw);
                }
            }

            foreach (iMonitor _monitor in MonitorPanels.SelectMany(p => p.Monitors))
            {
                _monitor.Update();
            }
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

                case MonitorType.Motherboard:
                    return OHMPanel(
                        config.Type,
                        "M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Z M8 9h8v6H8Z M2 9h2 M2 13h2 M20 9h2 M20 13h2 M8 3v2 M12 3v2 M16 3v2 M8 19v2 M12 19v2 M16 19v2",
                        config.Hardware,
                        config.Metrics,
                        config.Params,
                        config.Type.GetHardwareTypes()
                        );

                case MonitorType.Battery:
                    return OHMPanel(
                        config.Type,
                        "M7 7h11a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2Z M20 10v4 M7 10v4",
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

                case MonitorType.AIUsage:
                    return UsagePanel(
                        config.Type,
                        config.Hardware,
                        config.Metrics,
                        config.Params
                        );

                case MonitorType.Fans:
                    return FanPanel(
                        config.Type,
                        config.Hardware,
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

        // null when no provider is switched on, so an empty card never shows in the sidebar
        private MonitorPanel UsagePanel(MonitorType type, HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters)
        {
            iMonitor[] _monitors = UsageMonitor.GetInstances(hardwareConfig, metrics, parameters);

            if (_monitors.Length == 0)
            {
                return null;
            }

            return new MonitorPanel(
                type.GetDescription(),
                "M12 3v3 M12 18v3 M3 12h3 M18 12h3 M5.6 5.6l2.1 2.1 M16.3 16.3l2.1 2.1 M5.6 18.4l2.1-2.1 M16.3 7.7l2.1-2.1 M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6Z",
                _monitors
                );
        }

        // null when no fans are enabled or detected, so an empty card never shows in the sidebar
        private MonitorPanel FanPanel(MonitorType type, HardwareConfig[] hardwareConfig, ConfigParam[] parameters)
        {
            iMonitor[] _monitors = FanMonitor.GetInstances(_board, hardwareConfig, parameters);

            if (_monitors.Length == 0)
            {
                return null;
            }

            return new MonitorPanel(
                type.GetDescription(),
                FanMonitor.IconPathData,
                _monitors
                );
        }

        private void UpdateBoard()
        {
            _board?.Update();
            UpdateSubHardware(_board);
        }

        private void UpdateSubHardware(IHardware hardware)
        {
            if (hardware?.SubHardware == null)
            {
                return;
            }

            foreach (IHardware sub in hardware.SubHardware)
            {
                if (sub == null)
                {
                    continue;
                }

                try
                {
                    sub.Update();
                }
                catch (Exception ex)
                {
                    string key = sub.Identifier?.ToString() ?? sub.Name ?? "SubHardware";
                    if (_loggedSubHardwareErrors.Add(key))
                    {
                        Utilities.ErrorLog.Write(ex);
                    }
                }

                UpdateSubHardware(sub);
            }
        }

        private readonly HashSet<string> _loggedSubHardwareErrors = new HashSet<string>();

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
}
