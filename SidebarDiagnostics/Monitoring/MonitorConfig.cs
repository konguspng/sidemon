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
    [Serializable]
    public enum MonitorType : byte
    {
        CPU,
        RAM,
        GPU,
        HD,
        Network,
        Motherboard,
        Battery,
        AIUsage
    }

    public class MonitorConfig : INotifyPropertyChanged, ICloneable
    {
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

        public MonitorConfig Clone()
        {
            MonitorConfig _clone = (MonitorConfig)MemberwiseClone();
            _clone.Hardware = _clone.Hardware.Select(h => h.Clone()).ToArray();
            _clone.Params = _clone.Params.Select(p => p.Clone()).ToArray();

            // Metrics must be deep-copied too: without this the Settings dialog's
            // working copy shares the same MetricConfig objects as the live settings,
            // so ticking a metric checkbox mutates Settings.Instance immediately and
            // Cancel can't undo it (and any later Settings.Save, e.g. from auto-fit,
            // silently persists the abandoned change).
            if (_clone.Metrics != null)
            {
                _clone.Metrics = _clone.Metrics.Select(m => m.Clone()).ToArray();
            }

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
                MonitorConfig _defaultRecord = _default.FirstOrDefault(d => d.Type == _record.Type) ?? _default[0];

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
                return new MonitorConfig[8]
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
                    },
                    new MonitorConfig()
                    {
                        Type = MonitorType.Motherboard,
                        Enabled = true,
                        Order = 6,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[3]
                        {
                            new MetricConfig(MetricKey.MotherboardTemp, true),
                            new MetricConfig(MetricKey.MotherboardFan, true),
                            new MetricConfig(MetricKey.MotherboardVoltage, false)
                        },
                        Params = new ConfigParam[4]
                        {
                            ConfigParam.Defaults.HardwareNames,
                            ConfigParam.Defaults.RoundAll,
                            ConfigParam.Defaults.UseFahrenheit,
                            ConfigParam.Defaults.TempAlert
                        }
                    },
                    new MonitorConfig()
                    {
                        Type = MonitorType.Battery,
                        Enabled = true,
                        Order = 0,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[4]
                        {
                            new MetricConfig(MetricKey.BatteryLevel, true),
                            new MetricConfig(MetricKey.BatteryTimeRemaining, true),
                            new MetricConfig(MetricKey.BatteryRate, false),
                            new MetricConfig(MetricKey.BatteryVoltage, false)
                        },
                        Params = new ConfigParam[2]
                        {
                            ConfigParam.Defaults.HardwareNames,
                            ConfigParam.Defaults.RoundAll
                        }
                    },
                    new MonitorConfig()
                    {
                        Type = MonitorType.AIUsage,
                        // the card stays hidden until a provider is ticked in
                        // Settings > Monitors > AI CLI Usage; every provider starts off
                        Enabled = true,
                        Order = 0,
                        Hardware = new HardwareConfig[0],
                        Metrics = new MetricConfig[3]
                        {
                            new MetricConfig(MetricKey.UsageFiveHour, true),
                            new MetricConfig(MetricKey.UsageWeekly, true),
                            new MetricConfig(MetricKey.UsageMonthly, true)
                        },
                        Params = new ConfigParam[5]
                        {
                            ConfigParam.Defaults.HardwareNames,
                            ConfigParam.Defaults.RoundAll,
                            ConfigParam.Defaults.UsageAlert,
                            ConfigParam.Defaults.ShowSignInButton,
                            ConfigParam.Defaults.ShowProviderIcon
                        }
                    }
                };
            }
        }
    }

    public class UsageBarToggle : INotifyPropertyChanged
    {
        private readonly HardwareConfig _owner;

        public UsageBarToggle(HardwareConfig owner, string key, string label)
        {
            _owner = owner;
            Key = key;
            Label = label;
        }

        public string Key { get; }

        public string Label { get; }

        public bool IsOn
        {
            get
            {
                return _owner.HiddenBars == null || Array.IndexOf(_owner.HiddenBars, Key) < 0;
            }
            set
            {
                if (IsOn == value) return;
                var _list = new List<string>(_owner.HiddenBars ?? new string[0]);
                if (value) _list.Remove(Key); else _list.Add(Key);
                _owner.HiddenBars = _list.Count == 0 ? null : _list.ToArray();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsOn"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class HardwareConfig : INotifyPropertyChanged, ICloneable
    {
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

        // live detection text shown next to AI CLI providers in Settings; never saved
        private string _status { get; set; }

        [JsonIgnore]
        public string Status
        {
            get
            {
                return _status;
            }
            set
            {
                if (string.Equals(_status, value, StringComparison.Ordinal))
                {
                    return;
                }

                _status = value;

                NotifyPropertyChanged("Status");
            }
        }

        // default ON: SideMon keeps the Claude login fresh by letting the Claude CLI renew it
        // (stored under a new name so the old opt-in "false" from 4.5.x does not carry over)
        private bool _autoRenew { get; set; } = true;

        [JsonPropertyName("AutoRefreshLogin")]
        public bool AutoRenew
        {
            get { return _autoRenew; }
            set { if (_autoRenew == value) return; _autoRenew = value; NotifyPropertyChanged("AutoRenew"); }
        }

        private string _renewStatus { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string RenewStatus
        {
            get { return _renewStatus; }
            set { if (_renewStatus == value) return; _renewStatus = value; NotifyPropertyChanged("RenewStatus"); }
        }

        private string _customPath { get; set; }

        // optional per-provider credentials path (AI CLI Usage only); null = defaults
        public string CustomPath
        {
            get
            {
                return _customPath;
            }
            set
            {
                if (string.Equals(_customPath, value, StringComparison.Ordinal))
                {
                    return;
                }

                _customPath = value;

                NotifyPropertyChanged("CustomPath");
            }
        }

        private string[] _hiddenBars { get; set; } = null;

        // AI CLI Usage only: bars of this provider the user switched off (UsageMonitor.BarKey)
        public string[] HiddenBars
        {
            get
            {
                return _hiddenBars;
            }
            set
            {
                _hiddenBars = value;

                NotifyPropertyChanged("HiddenBars");
            }
        }

        // Settings only: one checkbox per bar the provider reported in its last poll
        [JsonIgnore]
        public System.Collections.ObjectModel.ObservableCollection<UsageBarToggle> Bars { get; set; }

        private bool? _showExtra { get; set; } = null;

        // optional per-provider flag (AI CLI Usage only); null/true = show all bars, false = hide secondary bars
        public bool? ShowExtra
        {
            get
            {
                return _showExtra;
            }
            set
            {
                if (_showExtra == value)
                {
                    return;
                }

                _showExtra = value;

                NotifyPropertyChanged("ShowExtra");
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

        public MetricConfig Clone()
        {
            return (MetricConfig)MemberwiseClone();
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
        DriveWrite = 25,

        MotherboardTemp = 28,
        MotherboardFan = 29,
        MotherboardVoltage = 30,

        BatteryLevel = 31,
        BatteryVoltage = 32,
        BatteryRate = 33,
        BatteryTimeRemaining = 34,

        UsageStatus = 35,
        UsageFiveHour = 36,
        UsageWeekly = 37,
        UsageMonthly = 38
    }
}
