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
    public class ConfigParam : INotifyPropertyChanged, ICloneable
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

                    case ParamKey.UsageAlert:
                        return Resources.SettingsUsageAlert;

                    case ParamKey.ShowSignInButton:
                        return Resources.SettingsShowSignInButton;

                    case ParamKey.ShowProviderIcon:
                        return Resources.SettingsShowProviderIcon;

                    case ParamKey.HideStoppedFans:
                        return Resources.SettingsHideStoppedFans;

                    case ParamKey.ShowFanControl:
                        return Resources.SettingsShowFanControl;

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

                    case ParamKey.UsageAlert:
                        return Resources.SettingsUsageAlertTooltip;

                    case ParamKey.ShowSignInButton:
                        return Resources.SettingsShowSignInButtonTooltip;

                    case ParamKey.ShowProviderIcon:
                        return Resources.SettingsShowProviderIconTooltip;

                    case ParamKey.HideStoppedFans:
                        return Resources.SettingsHideStoppedFansTooltip;

                    case ParamKey.ShowFanControl:
                        return Resources.SettingsShowFanControlTooltip;

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

            public static ConfigParam UsageAlert
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.UsageAlert, Value = 0 };
                }
            }

            public static ConfigParam ShowFanRPM
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.ShowFanRPM, Value = true };
                }
            }

            public static ConfigParam ShowSignInButton
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.ShowSignInButton, Value = true };
                }
            }

            public static ConfigParam ShowProviderIcon
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.ShowProviderIcon, Value = true };
                }
            }

            public static ConfigParam HideStoppedFans
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.HideStoppedFans, Value = true };
                }
            }

            public static ConfigParam ShowFanControl
            {
                get
                {
                    return new ConfigParam() { Key = ParamKey.ShowFanControl, Value = false };
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
        ShowFanRPM,
        UsageAlert,
        ShowSignInButton,
        ShowProviderIcon,
        HideStoppedFans,
        ShowFanControl
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
        IP,
        Minute
    }
}
