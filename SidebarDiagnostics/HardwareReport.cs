using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using SidebarDiagnostics.Monitoring;

namespace SidebarDiagnostics
{
    public static class HardwareReport
    {
        public const int MaxReportBytes = 400 * 1024;
        private const string ReportMarker = "[SideMon hardware report]";
        private const int SM_CMONITORS = 80;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static readonly HashSet<string> SensitiveIdRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nic", "nvme", "hdd", "ssd", "scsi", "storage", "disk", "hid", "usb", "smart", "ata", "sata", "ahci"
        };

        private static readonly HashSet<string> KnownSensorSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "voltage", "current", "power", "clock", "temperature", "load", "frequency", "fan", "flow",
            "control", "level", "factor", "data", "smalldata", "throughput", "timespan", "timing",
            "energy", "noise", "conductivity", "humidity"
        };

        private static readonly HashSet<string> KnownHardwareRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "intelcpu", "amdcpu", "cpu", "ram", "vram", "memory", "gpu-nvidia", "gpu-amd", "gpu-intel",
            "gpu-intel-integrated", "gpu-intel-discrete",
            "motherboard", "lpc", "superio", "ec", "embeddedcontroller", "battery", "psu", "cooler",
            "controller", "powermonitor", "nic", "nvme", "hdd", "ssd", "scsi", "storage", "disk", "hid", "usb", "ahci"
        };

        private static readonly Regex GuidRegex = new Regex(
            @"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?",
            RegexOptions.Compiled);

        private static readonly Regex MacSegmentRegex = new Regex(
            @"(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}|^[0-9a-fA-F]{12}$",
            RegexOptions.Compiled);

        private static readonly Regex LongHexRegex = new Regex(
            @"^[0-9a-fA-F]{8,}$",
            RegexOptions.Compiled);

        private static readonly Regex SerialLikeSegmentRegex = new Regex(
            @"^(?=.*\d)(?=.*[a-zA-Z])[a-zA-Z0-9_-]{10,}$",
            RegexOptions.Compiled);

        private static readonly Regex MacAddressRegex = new Regex(
            @"\b(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}\b|\b(?:[0-9a-fA-F]{4}\.){2}[0-9a-fA-F]{4}\b",
            RegexOptions.Compiled);

        private static readonly Regex Ipv4Regex = new Regex(
            @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
            RegexOptions.Compiled);

        private static readonly Regex Ipv6Regex = new Regex(
            @"(?<![0-9a-fA-F:])(?:(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|(?:[0-9a-fA-F]{1,4}:){1,7}:|(?:[0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|(?:[0-9a-fA-F]{1,4}:){1,5}(?::[0-9a-fA-F]{1,4}){1,2}|(?:[0-9a-fA-F]{1,4}:){1,4}(?::[0-9a-fA-F]{1,4}){1,3}|(?:[0-9a-fA-F]{1,4}:){1,3}(?::[0-9a-fA-F]{1,4}){1,4}|(?:[0-9a-fA-F]{1,4}:){1,2}(?::[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:(?::[0-9a-fA-F]{1,4}){1,6}|:(?::[0-9a-fA-F]{1,4}){1,7}|::)(?:%[0-9a-zA-Z_.-]+)?(?![0-9a-fA-F:])",
            RegexOptions.Compiled);

        public static Task<string> BuildReportAsync()
        {
            return Task.Run(() => BuildReport());
        }

        public static string BuildReport()
        {
            return BuildReport(TryGetLiveMonitorManager());
        }

        internal static string BuildReport(MonitorManager liveManager)
        {
            var sb = new StringBuilder(32768);

            sb.AppendLine(ReportMarker);
            sb.AppendLine();

            AppendHeader(sb);
            sb.AppendLine();

            AppendMachineInfo(sb);
            sb.AppendLine();

            AppendSensorTree(sb);
            sb.AppendLine();

            AppendSideMonMonitors(sb, liveManager);
            sb.AppendLine();

            AppendWmiFallbacks(sb);

            string scrubbed = ScrubReport(sb.ToString());
            return EnforceSizeCap(scrubbed, MaxReportBytes);
        }

        private static MonitorManager TryGetLiveMonitorManager()
        {
            try
            {
                return App.Current?.Sidebar?.Model?.MonitorManager;
            }
            catch
            {
                return null;
            }
        }

        private static void AppendHeader(StringBuilder sb)
        {
            sb.AppendLine("=== Header ===");
            sb.AppendLine($"SideMon: {FormatThreePartVersion(Assembly.GetExecutingAssembly().GetName().Version)}");
            sb.AppendLine($"OS: {GetOsDescription()}");
            sb.AppendLine($"64-bit: OS={(Environment.Is64BitOperatingSystem ? "yes" : "no")}, Process={(Environment.Is64BitProcess ? "yes" : "no")}");
            sb.AppendLine($"Elevated: {(IsElevated() ? "yes" : "no")}");
            sb.AppendLine($"PawnIO: {GetPawnIoSummary()}");
            sb.AppendLine($"LibreHardwareMonitor: {GetLhmVersion()}");
            sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription} (CLR {FormatThreePartVersion(Environment.Version)})");
            sb.AppendLine($"Culture: {CultureInfo.CurrentCulture.Name} (UI: {CultureInfo.CurrentUICulture.Name})");
            sb.AppendLine($"Screens: {GetScreenCount()}");
        }

        private static string FormatThreePartVersion(Version version)
        {
            if (version == null)
            {
                return "unknown";
            }

            if (version.Build >= 0)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }

            return $"{version.Major}.{version.Minor}";
        }

        private static string GetOsDescription()
        {
            try
            {
                string productName = null;
                string displayVersion = null;
                string currentBuild = null;
                object ubr = null;

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        productName = key.GetValue("ProductName") as string;
                        displayVersion = key.GetValue("DisplayVersion") as string;
                        currentBuild = key.GetValue("CurrentBuild") as string;
                        ubr = key.GetValue("UBR");
                    }
                }

                string desc = RuntimeInformation.OSDescription?.Trim() ?? Environment.OSVersion.VersionString;
                var parts = new List<string> { desc };

                if (!string.IsNullOrWhiteSpace(displayVersion))
                {
                    parts.Add(displayVersion);
                }

                if (!string.IsNullOrWhiteSpace(currentBuild))
                {
                    string buildText = ubr != null ? $"Build {currentBuild} (UBR {ubr})" : $"Build {currentBuild}";
                    parts.Add(buildText);
                }
                else if (!string.IsNullOrWhiteSpace(productName))
                {
                    parts.Add(productName);
                }

                return string.Join(", ", parts);
            }
            catch
            {
                return RuntimeInformation.OSDescription ?? "unknown";
            }
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static string GetPawnIoSummary()
        {
            bool fileInstalled = false;
            bool lhmInstalled = false;
            string version = null;

            try
            {
                fileInstalled = Utilities.PawnIO.IsInstalled;
            }
            catch { }

            try
            {
                lhmInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
                Version v = LibreHardwareMonitor.PawnIo.PawnIo.Version;
                if (v != null)
                {
                    version = FormatThreePartVersion(v);
                }
            }
            catch { }

            if (string.IsNullOrEmpty(version) && fileInstalled)
            {
                try
                {
                    string lib = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll");
                    if (File.Exists(lib))
                    {
                        FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(lib);
                        if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                        {
                            version = fvi.ProductVersion.Trim();
                        }
                        else if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                        {
                            version = fvi.FileVersion.Trim();
                        }
                    }
                }
                catch { }
            }

            bool installed = fileInstalled || lhmInstalled;
            if (!installed)
            {
                return "no";
            }

            return string.IsNullOrEmpty(version) ? "yes" : $"yes ({version})";
        }

        private static string GetLhmVersion()
        {
            try
            {
                Assembly asm = typeof(Computer).Assembly;
                var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (infoAttr != null && !string.IsNullOrWhiteSpace(infoAttr.InformationalVersion))
                {
                    string info = infoAttr.InformationalVersion;
                    int plusIdx = info.IndexOf('+');
                    if (plusIdx > 0)
                    {
                        string commit = info.Substring(plusIdx + 1);
                        if (commit.Length > 8)
                        {
                            commit = commit.Substring(0, 8);
                        }
                        return $"{info.Substring(0, plusIdx)} ({commit})";
                    }
                    return info;
                }

                return FormatThreePartVersion(asm.GetName().Version);
            }
            catch
            {
                return "unknown";
            }
        }

        private static int GetScreenCount()
        {
            try
            {
                int count = GetSystemMetrics(SM_CMONITORS);
                return count > 0 ? count : 1;
            }
            catch
            {
                return 0;
            }
        }

        private static void AppendMachineInfo(StringBuilder sb)
        {
            sb.AppendLine("=== Machine ===");

            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        using (obj)
                        {
                            string mfg = SafeWmiString(obj, "Manufacturer");
                            string model = SafeWmiString(obj, "Model");
                            sb.AppendLine($"Manufacturer: {mfg ?? "unknown"}");
                            sb.AppendLine($"Model: {model ?? "unknown"}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"System: unavailable ({ex.GetType().Name})");
            }

            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        using (obj)
                        {
                            string bios = SafeWmiString(obj, "SMBIOSBIOSVersion");
                            sb.AppendLine($"BIOS: {bios ?? "unknown"}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"BIOS: unavailable ({ex.GetType().Name})");
            }

            try
            {
                var cpus = new List<string>();
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_Processor"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        using (obj)
                        {
                            string name = SafeWmiString(obj, "Name");
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                cpus.Add(name.Trim());
                            }
                        }
                    }
                }

                if (cpus.Count == 0)
                {
                    sb.AppendLine("CPU: unknown");
                }
                else
                {
                    for (int i = 0; i < cpus.Count; i++)
                    {
                        sb.AppendLine(cpus.Count == 1 ? $"CPU: {cpus[i]}" : $"CPU #{i}: {cpus[i]}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"CPU: unavailable ({ex.GetType().Name})");
            }

            try
            {
                int gpuIdx = 0;
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        using (obj)
                        {
                            string name = SafeWmiString(obj, "Name") ?? "unknown";
                            string driver = SafeWmiString(obj, "DriverVersion") ?? "unknown";
                            string ramText = FormatAdapterRam(obj["AdapterRAM"]);
                            sb.AppendLine($"GPU #{gpuIdx}: {name} | Driver: {driver} | AdapterRAM: {ramText}");
                            gpuIdx++;
                        }
                    }
                }

                if (gpuIdx == 0)
                {
                    sb.AppendLine("GPU: none found");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"GPU: unavailable ({ex.GetType().Name})");
            }
        }

        private static string SafeWmiString(ManagementObject obj, string propertyName)
        {
            try
            {
                object val = obj[propertyName];
                return val?.ToString()?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string FormatAdapterRam(object value)
        {
            if (value == null)
            {
                return "unknown";
            }

            try
            {
                ulong bytes = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                if (bytes == 0)
                {
                    return "0 MB";
                }

                double mb = bytes / (1024d * 1024d);
                if (mb >= 1024d)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0:0.##} GB ({1} B)", mb / 1024d, bytes);
                }

                return string.Format(CultureInfo.InvariantCulture, "{0:0} MB ({1} B)", mb, bytes);
            }
            catch
            {
                return "unknown";
            }
        }

        private static void AppendSensorTree(StringBuilder sb)
        {
            sb.AppendLine("=== Sensors ===");

            Computer computer = new Computer()
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsBatteryEnabled = true,
                IsPsuEnabled = true,
                IsPowerMonitorEnabled = true
            };

            try
            {
                computer.Open();

                UpdateAllHardware(computer);
                Thread.Sleep(1000);
                UpdateAllHardware(computer);

                int hardwareCount = 0;
                int sensorCount = 0;

                foreach (IHardware hw in computer.Hardware)
                {
                    AppendHardwareNode(sb, hw, 0, ref hardwareCount, ref sensorCount);
                }

                sb.AppendLine($"Total hardware nodes: {hardwareCount}, Total sensors: {sensorCount}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Hardware scan error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                CloseReportComputer(computer);
            }
        }

        private static void UpdateAllHardware(Computer computer)
        {
            if (computer?.Hardware == null)
            {
                return;
            }

            foreach (IHardware hw in computer.Hardware)
            {
                UpdateHardwareRecursive(hw);
            }
        }

        private static void UpdateHardwareRecursive(IHardware hardware)
        {
            if (hardware == null)
            {
                return;
            }

            try
            {
                hardware.Update();
            }
            catch { }

            if (hardware.SubHardware == null)
            {
                return;
            }

            foreach (IHardware sub in hardware.SubHardware)
            {
                UpdateHardwareRecursive(sub);
            }
        }

        private static void CloseReportComputer(Computer computer)
        {
            if (computer == null)
            {
                return;
            }

            // Disable every group so IGroup.Close() runs cleanly for all opened groups.
            try { computer.IsCpuEnabled = false; } catch { }
            try { computer.IsGpuEnabled = false; } catch { }
            try { computer.IsMemoryEnabled = false; } catch { }
            try { computer.IsMotherboardEnabled = false; } catch { }
            try { computer.IsControllerEnabled = false; } catch { }
            try { computer.IsStorageEnabled = false; } catch { }
            try { computer.IsNetworkEnabled = false; } catch { }
            try { computer.IsBatteryEnabled = false; } catch { }
            try { computer.IsPsuEnabled = false; } catch { }
            try { computer.IsPowerMonitorEnabled = false; } catch { }

            // Computer.Close() in LHM 0.9.7 unconditionally calls static OpCode.Close() and
            // Mutexes.Close(), which would disrupt the live sidebar Computer if one is running.
            if (!MonitorManager.HasActiveLiveComputer)
            {
                try { computer.Close(); } catch { }
            }
        }

        private static void AppendHardwareNode(StringBuilder sb, IHardware hw, int depth, ref int hardwareCount, ref int sensorCount)
        {
            if (hw == null)
            {
                return;
            }

            hardwareCount++;
            string indent = new string(' ', depth * 2);
            string displayName = FormatHardwareName(hw);
            string hwId = SanitizeIdentifier(hw.Identifier?.ToString());

            sb.AppendLine($"{indent}[{hw.HardwareType}] {displayName} (id: {hwId})");

            if (hw.Sensors != null)
            {
                string sensorIndent = indent + "  ";
                foreach (ISensor sensor in hw.Sensors)
                {
                    if (sensor == null)
                    {
                        continue;
                    }

                    sensorCount++;
                    string val = FormatSensorFloat(sensor.Value);
                    string min = FormatSensorFloat(sensor.Min);
                    string max = FormatSensorFloat(sensor.Max);
                    string sid = SanitizeIdentifier(sensor.Identifier?.ToString());
                    sb.AppendLine($"{sensorIndent}{sensor.SensorType} | {sensor.Name} | Value={val} | Min={min} | Max={max} | Id={sid}");
                }
            }

            if (hw.SubHardware != null)
            {
                foreach (IHardware sub in hw.SubHardware)
                {
                    AppendHardwareNode(sb, sub, depth + 1, ref hardwareCount, ref sensorCount);
                }
            }
        }

        private static string FormatHardwareName(IHardware hw)
        {
            string name = hw.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return "unknown";
            }

            if (hw.HardwareType == HardwareType.Storage || hw.HardwareType == HardwareType.Network)
            {
                // Strip any parenthetical/trailing MAC, IP, or GUID tokens while preserving model / adapter description.
                name = MacAddressRegex.Replace(name, "*");
                name = Ipv4Regex.Replace(name, "*");
                name = Ipv6Regex.Replace(name, "*");
                name = Regex.Replace(name, @"\{[0-9a-fA-F-]{36}\}", "*");
            }

            return name;
        }

        private static string FormatSensorFloat(float? value)
        {
            if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
            {
                return "null";
            }

            double rounded = Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string SanitizeIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return "";
            }

            bool leadingSlash = identifier.StartsWith("/", StringComparison.Ordinal);
            string[] parts = identifier.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return identifier;
            }

            bool sensitiveRoot = SensitiveIdRoots.Contains(parts[0]);

            for (int i = 0; i < parts.Length; i++)
            {
                string seg = parts[i];

                if (i == 0 && KnownHardwareRoots.Contains(seg))
                {
                    continue;
                }

                if (KnownSensorSegments.Contains(seg))
                {
                    continue;
                }

                if (IsSmallInteger(seg))
                {
                    continue;
                }

                if (seg.IndexOf('%') >= 0 || seg.IndexOf('{') >= 0 || seg.IndexOf('}') >= 0 ||
                    GuidRegex.IsMatch(seg) || MacSegmentRegex.IsMatch(seg) || LongHexRegex.IsMatch(seg) || SerialLikeSegmentRegex.IsMatch(seg))
                {
                    parts[i] = "*";
                    continue;
                }

                if (sensitiveRoot && i > 0)
                {
                    parts[i] = "*";
                }
            }

            return (leadingSlash ? "/" : "") + string.Join("/", parts);
        }

        private static bool IsSmallInteger(string seg)
        {
            return seg.Length > 0 && seg.Length <= 4 && seg.All(char.IsDigit);
        }

        private static void AppendSideMonMonitors(StringBuilder sb, MonitorManager liveManager)
        {
            sb.AppendLine("=== SideMon Monitors ===");

            try
            {
                MonitorConfig[] configs = Framework.Settings.Instance?.MonitorConfig;
                if (configs != null && configs.Length > 0)
                {
                    var enabledTypes = configs.Where(c => c != null && c.Enabled).Select(c => c.Type.ToString()).ToArray();
                    sb.AppendLine("Configured enabled monitors: " + (enabledTypes.Length > 0 ? string.Join(", ", enabledTypes) : "none"));
                }
            }
            catch { }

            if (liveManager == null)
            {
                sb.AppendLine("Live sidebar panels: not available");
                return;
            }

            MonitorPanel[] panels;
            try
            {
                panels = liveManager.MonitorPanels;
            }
            catch
            {
                sb.AppendLine("Live sidebar panels: not available");
                return;
            }

            if (panels == null || panels.Length == 0)
            {
                sb.AppendLine("Live sidebar panels: none");
                return;
            }

            foreach (MonitorPanel panel in panels)
            {
                if (panel == null)
                {
                    continue;
                }

                iMonitor[] monitors = panel.Monitors;
                int monCount = monitors?.Length ?? 0;
                sb.AppendLine($"Panel: {panel.Title} ({monCount} monitor(s))");

                if (monitors == null)
                {
                    continue;
                }

                foreach (iMonitor monitor in monitors)
                {
                    if (monitor == null)
                    {
                        continue;
                    }

                    iMetric[] metrics = monitor.Metrics;
                    int total = metrics?.Length ?? 0;
                    var missing = new List<string>();

                    if (metrics != null)
                    {
                        foreach (iMetric metric in metrics)
                        {
                            if (metric == null)
                            {
                                continue;
                            }

                            string text = metric.Text;
                            bool noValue = string.IsNullOrWhiteSpace(text) ||
                                text.Equals("No Value", StringComparison.OrdinalIgnoreCase) ||
                                text.Equals("-", StringComparison.Ordinal) ||
                                (metric.IsNumeric && (double.IsNaN(metric.Value) || double.IsInfinity(metric.Value)));

                            if (noValue)
                            {
                                string label = !string.IsNullOrWhiteSpace(metric.FullName) ? metric.FullName : metric.Label;
                                missing.Add($"{label} ({metric.Key})");
                            }
                        }
                    }

                    string monitorName = string.IsNullOrWhiteSpace(monitor.Name) ? "unnamed" : monitor.Name.Trim();
                    if (missing.Count > 0)
                    {
                        sb.AppendLine($"  Monitor: {monitorName} | Metrics: {total} | No value ({missing.Count}): {string.Join(", ", missing)}");
                    }
                    else
                    {
                        sb.AppendLine($"  Monitor: {monitorName} | Metrics: {total} | No value: 0");
                    }
                }
            }
        }

        private static void AppendWmiFallbacks(StringBuilder sb)
        {
            sb.AppendLine("=== WMI Fallbacks ===");
            sb.AppendLine("MSAcpi_ThermalZoneTemperature (root\\WMI): " + QueryWmiCount("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"));
            sb.AppendLine("Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine (root\\CIMV2): " + QueryWmiCount("root\\CIMV2", "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"));
        }

        private static string QueryWmiCount(string scope, string query)
        {
            try
            {
                int count = 0;
                using (var searcher = new ManagementObjectSearcher(scope, query))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject obj in results)
                    {
                        using (obj)
                        {
                            count++;
                        }
                    }
                }

                return $"available (count: {count})";
            }
            catch (ManagementException mex)
            {
                if (mex.ErrorCode == ManagementStatus.AccessDenied)
                {
                    return "access denied";
                }

                if (mex.ErrorCode == ManagementStatus.NotSupported || mex.ErrorCode == ManagementStatus.InvalidClass)
                {
                    return "not supported";
                }

                return $"error ({mex.ErrorCode})";
            }
            catch (UnauthorizedAccessException)
            {
                return "access denied";
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name})";
            }
        }

        internal static string ScrubReport(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string scrubbed = ReportService.Scrub(text);

            // Scrub MAC addresses, UUIDs/GUIDs, IPv4, and IPv6 addresses.
            scrubbed = MacAddressRegex.Replace(scrubbed, "<mac>");
            scrubbed = GuidRegex.Replace(scrubbed, "<uuid>");
            scrubbed = Ipv4Regex.Replace(scrubbed, "<ip>");
            scrubbed = Ipv6Regex.Replace(scrubbed, "<ipv6>");

            // Scrub host/machine/user identifiers case-insensitively.
            scrubbed = ScrubLiteral(scrubbed, Environment.MachineName, "<machine>");
            try
            {
                scrubbed = ScrubLiteral(scrubbed, Dns.GetHostName(), "<machine>");
            }
            catch { }

            scrubbed = ScrubLiteral(scrubbed, Environment.UserName, "<user>");

            string domain = Environment.UserDomainName;
            if (!string.IsNullOrWhiteSpace(domain) &&
                domain.Length >= 3 &&
                !domain.Equals("WORKGROUP", StringComparison.OrdinalIgnoreCase) &&
                !domain.Equals("NT AUTHORITY", StringComparison.OrdinalIgnoreCase))
            {
                scrubbed = ScrubLiteral(scrubbed, domain, "<domain>");
            }

            return scrubbed;
        }

        private static string ScrubLiteral(string input, string literal, string replacement)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrWhiteSpace(literal))
            {
                return input;
            }

            return Regex.Replace(input, Regex.Escape(literal), replacement, RegexOptions.IgnoreCase);
        }

        private static string EnforceSizeCap(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            {
                return text;
            }

            const string suffix = "\r\n[truncated at 400 KB]\r\n";
            int budget = maxBytes - Encoding.UTF8.GetByteCount(suffix);
            if (budget <= 0)
            {
                return suffix;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int cut = budget;
            // Back off to a valid UTF-8 character boundary.
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
            {
                cut--;
            }

            string prefix = Encoding.UTF8.GetString(bytes, 0, cut);
            int lastNewline = prefix.LastIndexOf('\n');
            if (lastNewline > 0)
            {
                prefix = prefix.Substring(0, lastNewline).TrimEnd('\r');
            }

            return prefix + suffix;
        }
    }
}
