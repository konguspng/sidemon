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

                case MonitorType.Motherboard:
                    return new HardwareType[1] { HardwareType.Motherboard };

                case MonitorType.Battery:
                    return new HardwareType[1] { HardwareType.Battery };

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

                case MonitorType.Motherboard:
                    return Resources.Motherboard;

                case MonitorType.Battery:
                    return Resources.Battery;

                case MonitorType.AIUsage:
                    return Resources.AIUsage;

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }

        public static T GetValue<T>(this ConfigParam[] parameters, ParamKey key)
        {
            var p = parameters.FirstOrDefault(x => x.Key == key); return p != null ? (T)p.Value : default(T);
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

                case MetricKey.MotherboardTemp:
                    return Resources.MotherboardTemp;

                case MetricKey.MotherboardFan:
                    return Resources.MotherboardFan;

                case MetricKey.MotherboardVoltage:
                    return Resources.MotherboardVoltage;

                case MetricKey.BatteryLevel:
                    return Resources.BatteryLevel;

                case MetricKey.BatteryVoltage:
                    return Resources.BatteryVoltage;

                case MetricKey.BatteryRate:
                    return Resources.BatteryRate;

                case MetricKey.BatteryTimeRemaining:
                    return Resources.BatteryTimeRemaining;

                case MetricKey.UsageStatus:
                    return Resources.UsageStatusLabel;

                case MetricKey.UsageFiveHour:
                    return Resources.UsageFiveHour;

                case MetricKey.UsageWeekly:
                    return Resources.UsageWeekly;

                case MetricKey.UsageMonthly:
                    return Resources.UsageMonthly;

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

                case MetricKey.MotherboardTemp:
                    return Resources.MotherboardTempLabel;

                case MetricKey.MotherboardFan:
                    return Resources.MotherboardFanLabel;

                case MetricKey.MotherboardVoltage:
                    return Resources.MotherboardVoltageLabel;

                case MetricKey.BatteryLevel:
                    return Resources.BatteryLevelLabel;

                case MetricKey.BatteryVoltage:
                    return Resources.BatteryVoltageLabel;

                case MetricKey.BatteryRate:
                    return Resources.BatteryRateLabel;

                case MetricKey.BatteryTimeRemaining:
                    return Resources.BatteryTimeRemainingLabel;

                case MetricKey.UsageStatus:
                    return Resources.UsageStatusLabel;

                case MetricKey.UsageFiveHour:
                    return Resources.UsageFiveHourLabel;

                case MetricKey.UsageWeekly:
                    return Resources.UsageWeeklyLabel;

                case MetricKey.UsageMonthly:
                    return Resources.UsageMonthlyLabel;

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

                case DataType.Minute:
                    return " min";

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
