using System;
using System.Collections.Concurrent;
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
using System.Threading;
using System.Threading.Tasks;
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
                _registeredLiveComputer = true;
                Interlocked.Increment(ref _activeLiveComputers);
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
                        if (!_registeredLiveComputer)
                        {
                            _registeredLiveComputer = true;
                            Interlocked.Increment(ref _activeLiveComputers);
                        }
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
                    if (_registeredLiveComputer)
                    {
                        _registeredLiveComputer = false;
                        Interlocked.Decrement(ref _activeLiveComputers);
                    }

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

        private static readonly MonitorType[] OfflineProbeTypes = new MonitorType[]
        {
            MonitorType.CPU,
            MonitorType.RAM,
            MonitorType.GPU,
            MonitorType.Motherboard,
            MonitorType.Battery,
            MonitorType.Fans
        };

        private static readonly ConcurrentDictionary<MonitorType, HardwareConfig[]> _offlineHardwareCache = new ConcurrentDictionary<MonitorType, HardwareConfig[]>();
        private static readonly SemaphoreSlim _probeLock = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentQueue<MonitorType> _probeQueue = new ConcurrentQueue<MonitorType>();
        private static readonly ConcurrentDictionary<MonitorType, byte> _probeQueued = new ConcurrentDictionary<MonitorType, byte>();
        private static int _cacheGeneration;
        private static int _probeWorkerRunning;

        public static event Action<MonitorType> OfflineHardwareCached;

        public static void InvalidateOfflineHardwareCache()
        {
            Interlocked.Increment(ref _cacheGeneration);
            _offlineHardwareCache.Clear();
        }

        public static bool TryGetCachedOfflineHardware(MonitorType type, out HardwareConfig[] hardware)
        {
            if (_offlineHardwareCache.TryGetValue(type, out HardwareConfig[] cached))
            {
                hardware = CloneHardwareList(cached);
                return true;
            }

            hardware = null;
            return false;
        }

        private static HardwareConfig[] CloneHardwareList(HardwareConfig[] list)
        {
            if (list == null || list.Length == 0)
            {
                return Array.Empty<HardwareConfig>();
            }

            HardwareConfig[] copy = new HardwareConfig[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                copy[i] = list[i].Clone();
            }
            return copy;
        }

        private bool IsLiveGroupEnabled(MonitorType type)
        {
            if (_computer == null)
            {
                return false;
            }

            switch (type)
            {
                case MonitorType.CPU: return _computer.IsCpuEnabled;
                case MonitorType.RAM: return _computer.IsMemoryEnabled;
                case MonitorType.GPU: return _computer.IsGpuEnabled;
                case MonitorType.Motherboard: return _computer.IsMotherboardEnabled;
                case MonitorType.Battery: return _computer.IsBatteryEnabled;
                case MonitorType.Fans: return _computer.IsMotherboardEnabled;
                default: return false;
            }
        }

        public bool TryGetHardwareCached(MonitorType type, out HardwareConfig[] hardware)
        {
            switch (type)
            {
                case MonitorType.CPU:
                case MonitorType.RAM:
                case MonitorType.GPU:
                case MonitorType.Motherboard:
                case MonitorType.Battery:
                    if (IsLiveGroupEnabled(type))
                    {
                        hardware = GetHardware(type.GetHardwareTypes())
                            .Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name })
                            .ToArray();
                        return true;
                    }

                    return TryGetCachedOfflineHardware(type, out hardware);

                case MonitorType.HD:
                    hardware = DriveMonitor.GetHardware().ToArray();
                    return true;

                case MonitorType.Network:
                    hardware = NetworkMonitor.GetHardware().ToArray();
                    return true;

                case MonitorType.AIUsage:
                    hardware = UsageRegistry.GetHardware();
                    return true;

                case MonitorType.Fans:
                    if (IsLiveGroupEnabled(MonitorType.Fans))
                    {
                        hardware = FanMonitor.DiscoverFans(_board);
                        return true;
                    }

                    return TryGetCachedOfflineHardware(MonitorType.Fans, out hardware);

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }

        public HardwareConfig[] GetHardware(MonitorType type)
        {
            return TryGetHardwareCached(type, out HardwareConfig[] hardware)
                ? hardware
                : Array.Empty<HardwareConfig>();
        }

        public void StartOfflineHardwarePrewarm()
        {
            if (_disposed)
            {
                return;
            }

            bool enqueuedAny = false;
            foreach (MonitorType type in OfflineProbeTypes)
            {
                if (IsLiveGroupEnabled(type) || _offlineHardwareCache.ContainsKey(type))
                {
                    continue;
                }

                if (_probeQueued.TryAdd(type, 0))
                {
                    _probeQueue.Enqueue(type);
                    enqueuedAny = true;
                }
            }

            if (enqueuedAny)
            {
                EnsureProbeWorkerRunning();
            }
        }

        private static void EnsureProbeWorkerRunning()
        {
            if (Interlocked.CompareExchange(ref _probeWorkerRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(RunProbeWorkerAsync);
        }

        private static async Task RunProbeWorkerAsync()
        {
            try
            {
                while (_probeQueue.TryDequeue(out MonitorType type))
                {
                    _probeQueued.TryRemove(type, out _);

                    if (_offlineHardwareCache.ContainsKey(type))
                    {
                        continue;
                    }

                    await _probeLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (_offlineHardwareCache.ContainsKey(type))
                        {
                            continue;
                        }

                        int generation = Volatile.Read(ref _cacheGeneration);
                        ProbeOfflineType(type, generation);
                    }
                    finally
                    {
                        _probeLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                SidebarDiagnostics.Utilities.ErrorLog.Write(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _probeWorkerRunning, 0);

                if (!_probeQueue.IsEmpty)
                {
                    EnsureProbeWorkerRunning();
                }
            }
        }

        private static void ProbeOfflineType(MonitorType type, int generation)
        {
            var probe = new Computer();
            HardwareConfig[] discovered = Array.Empty<HardwareConfig>();
            HardwareConfig[] discoveredFans = null;
            HardwareConfig[] discoveredBoard = null;

            bool probeMotherboardPair = type == MonitorType.Motherboard || type == MonitorType.Fans;

            try
            {
                switch (type)
                {
                    case MonitorType.CPU: probe.IsCpuEnabled = true; break;
                    case MonitorType.RAM: probe.IsMemoryEnabled = true; break;
                    case MonitorType.GPU: probe.IsGpuEnabled = true; break;
                    case MonitorType.Motherboard:
                    case MonitorType.Fans:
                        probe.IsMotherboardEnabled = true;
                        probe.IsControllerEnabled = true;
                        break;
                    case MonitorType.Battery: probe.IsBatteryEnabled = true; break;
                }

                probe.Open();

                if (probeMotherboardPair)
                {
                    var boardTypes = MonitorType.Motherboard.GetHardwareTypes();
                    discoveredBoard = probe.Hardware
                        .Where(h => boardTypes.Contains(h.HardwareType))
                        .Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name })
                        .ToArray();

                    IHardware probeBoard = probe.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
                    probeBoard?.Update();
                    UpdateSubHardwareStatic(probeBoard);
                    discoveredFans = FanMonitor.DiscoverFans(probeBoard);
                }
                else
                {
                    var types = type.GetHardwareTypes();
                    discovered = probe.Hardware
                        .Where(h => types.Contains(h.HardwareType))
                        .Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name })
                        .ToArray();
                }
            }
            catch (Exception _ex)
            {
                // probing is best-effort (driver / permission problems): never let it take the
                // Settings window or the app down, just show no devices for this monitor
                SidebarDiagnostics.Utilities.ErrorLog.Write(_ex);
                if (probeMotherboardPair)
                {
                    discoveredBoard = Array.Empty<HardwareConfig>();
                    discoveredFans = Array.Empty<HardwareConfig>();
                }
            }
            finally
            {
                // LibreHardwareMonitor's Computer.Close() unconditionally calls static
                // OpCode.Close() and Mutexes.Close(), which tears down global driver state
                // used by the live Computer and breaks Cpu/Motherboard updates on every tick.
                // Clearing the Is*Enabled flags removes and calls IGroup.Close() on every
                // hardware group opened by this probe without touching static OpCode/Mutexes.
                CloseProbeGroups(probe);
            }

            if (Volatile.Read(ref _cacheGeneration) != generation)
            {
                return;
            }

            if (probeMotherboardPair)
            {
                _offlineHardwareCache[MonitorType.Motherboard] = discoveredBoard ?? Array.Empty<HardwareConfig>();
                _offlineHardwareCache[MonitorType.Fans] = discoveredFans ?? Array.Empty<HardwareConfig>();
                RaiseOfflineHardwareCached(MonitorType.Motherboard);
                RaiseOfflineHardwareCached(MonitorType.Fans);
            }
            else
            {
                _offlineHardwareCache[type] = discovered ?? Array.Empty<HardwareConfig>();
                RaiseOfflineHardwareCached(type);
            }
        }

        private static void CloseProbeGroups(Computer probe)
        {
            if (probe == null)
            {
                return;
            }

            try { probe.IsCpuEnabled = false; } catch { }
            try { probe.IsMemoryEnabled = false; } catch { }
            try { probe.IsGpuEnabled = false; } catch { }
            try { probe.IsMotherboardEnabled = false; } catch { }
            try { probe.IsControllerEnabled = false; } catch { }
            try { probe.IsBatteryEnabled = false; } catch { }
        }

        private static void RaiseOfflineHardwareCached(MonitorType type)
        {
            try
            {
                OfflineHardwareCached?.Invoke(type);
            }
            catch (Exception ex)
            {
                SidebarDiagnostics.Utilities.ErrorLog.Write(ex);
            }
        }

        private static void UpdateSubHardwareStatic(IHardware hardware)
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
                catch { }

                UpdateSubHardwareStatic(sub);
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

        private static int _activeLiveComputers;
        internal static bool HasActiveLiveComputer => Volatile.Read(ref _activeLiveComputers) > 0;
        private bool _registeredLiveComputer;

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
