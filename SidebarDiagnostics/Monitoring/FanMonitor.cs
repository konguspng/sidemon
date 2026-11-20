using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Monitoring
{
    public class FanMetric : BaseMetric
    {
        private static readonly Regex HashNumberRegex = new Regex(@"#(\d+)", RegexOptions.Compiled);
        private static readonly Regex AnyNumberRegex = new Regex(@"(\d+)", RegexOptions.Compiled);

        public FanMetric(ISensor sensor, ISensor controlSensor, string label, bool round = false, bool hideStopped = true, bool showControl = false)
            : base(MetricKey.MotherboardFan, DataType.RPM, label, round)
        {
            _sensor = sensor;
            _controlSensor = controlSensor;
            _hideStopped = hideStopped;
            _showControl = showControl;

            IsVisible = ShouldShowFan(_sensor?.Value, _hideStopped);
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
                    _controlSensor = null;
                }

                _disposed = true;
            }
        }

        ~FanMetric()
        {
            Dispose(false);
        }

        public override void Update()
        {
            float? rpm = _sensor?.Value;
            IsVisible = ShouldShowFan(rpm, _hideStopped);

            if (rpm.HasValue)
            {
                double val = rpm.Value;
                Value = val;
                nValue = val;
                Text = FormatFanText(val, _round, Append, _showControl, _controlSensor?.Value);
            }
            else
            {
                Text = "No Value";
            }
        }

        public static bool ShouldShowFan(float? rpm, bool hideStopped)
        {
            if (!hideStopped)
            {
                return true;
            }

            return rpm.HasValue && rpm.Value > 0f;
        }

        public static string FormatFanText(double rpm, bool roundAll, string rpmAppend, bool showControl, float? controlPercent)
        {
            string baseText = string.Format("{0:#,##0.##}{1}", rpm.Round(roundAll), rpmAppend);

            if (showControl && controlPercent.HasValue)
            {
                return string.Format("{0} ({1:0}%)", baseText, Math.Round(controlPercent.Value));
            }

            return baseText;
        }

        public static bool TryExtractSensorNumber(string name, out int number)
        {
            number = 0;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            Match hashMatch = HashNumberRegex.Match(name);
            if (hashMatch.Success && int.TryParse(hashMatch.Groups[1].Value, out number))
            {
                return true;
            }

            Match anyMatch = AnyNumberRegex.Match(name);
            if (anyMatch.Success && int.TryParse(anyMatch.Groups[1].Value, out number))
            {
                return true;
            }

            return false;
        }

        public static bool IsSameHardware(IHardware a, IHardware b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (ReferenceEquals(a, b))
            {
                return true;
            }

            string idA = a.Identifier?.ToString();
            string idB = b.Identifier?.ToString();

            return !string.IsNullOrEmpty(idA) && string.Equals(idA, idB, StringComparison.OrdinalIgnoreCase);
        }

        public static ISensor MatchControlSensor(ISensor fanSensor, IEnumerable<ISensor> controlSensors)
        {
            if (fanSensor == null || controlSensors == null)
            {
                return null;
            }

            ISensor[] sameHardware = controlSensors
                .Where(c => c != null && c.SensorType == SensorType.Control && IsSameHardware(fanSensor.Hardware, c.Hardware))
                .ToArray();

            if (sameHardware.Length == 0)
            {
                return null;
            }

            string fanName = (fanSensor.Name ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(fanName))
            {
                ISensor exactMatch = sameHardware.FirstOrDefault(c =>
                    string.Equals((c.Name ?? string.Empty).Trim(), fanName, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    return exactMatch;
                }
            }

            if (TryExtractSensorNumber(fanName, out int fanNumber))
            {
                ISensor numberMatch = sameHardware.FirstOrDefault(c =>
                    TryExtractSensorNumber(c.Name, out int ctrlNumber) && ctrlNumber == fanNumber);

                if (numberMatch != null)
                {
                    return numberMatch;
                }

                // Do not match a control sensor whose name carries a different number.
                return sameHardware.FirstOrDefault(c =>
                    c.Index == fanSensor.Index && !TryExtractSensorNumber(c.Name, out _));
            }

            return sameHardware.FirstOrDefault(c => c.Index == fanSensor.Index);
        }

        private ISensor _sensor { get; set; }

        private ISensor _controlSensor { get; set; }

        private bool _hideStopped { get; set; }

        private bool _showControl { get; set; }

        private bool _disposed { get; set; } = false;
    }

    public class FanMonitor : BaseMonitor
    {
        // 24x24 stroke-outline fan glyph matching the other sidebar panel icons
        public const string IconPathData = "M10.827 16.379a6.082 6.082 0 0 1-8.618-7.002l5.412 1.45a6.082 6.082 0 0 1 7.002-8.618l-1.45 5.412a6.082 6.082 0 0 1 8.618 7.002l-5.412-1.45a6.082 6.082 0 0 1-7.002 8.618l1.45-5.412Z M12 12v.01";

        public FanMonitor(string id, string name, bool showName, iMetric[] metrics) : base(id, name, showName)
        {
            Metrics = metrics ?? Array.Empty<iMetric>();
        }

        ~FanMonitor()
        {
            Dispose(false);
        }

        public static HardwareConfig[] DiscoverFans(IHardware board)
        {
            if (board == null)
            {
                return Array.Empty<HardwareConfig>();
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<HardwareConfig> result = new List<HardwareConfig>();

            foreach (ISensor sensor in OHMMonitor.BoardSensors(board))
            {
                if (sensor == null || sensor.SensorType != SensorType.Fan || sensor.Identifier == null)
                {
                    continue;
                }

                string id = sensor.Identifier.ToString();
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                {
                    continue;
                }

                result.Add(new HardwareConfig()
                {
                    ID = id,
                    Name = sensor.Name,
                    ActualName = sensor.Name,
                    Enabled = false,
                    IsSpinning = sensor.Value.HasValue && sensor.Value.Value > 0f
                });
            }

            return result.ToArray();
        }

        public static iMonitor[] GetInstances(IHardware board, HardwareConfig[] hardwareConfig, ConfigParam[] parameters)
        {
            if (board == null)
            {
                return Array.Empty<iMonitor>();
            }

            ISensor[] allSensors = OHMMonitor.BoardSensors(board)
                .Where(s => s != null && s.Identifier != null)
                .ToArray();

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<ISensor> fanSensors = new List<ISensor>();

            foreach (ISensor sensor in allSensors)
            {
                if (sensor.SensorType != SensorType.Fan)
                {
                    continue;
                }

                string id = sensor.Identifier.ToString();
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                {
                    continue;
                }

                fanSensors.Add(sensor);
            }

            if (fanSensors.Count == 0)
            {
                return Array.Empty<iMonitor>();
            }

            ISensor[] controlSensors = allSensors.Where(s => s.SensorType == SensorType.Control).ToArray();

            bool showName = parameters != null && parameters.GetValue<bool>(ParamKey.HardwareNames);
            bool roundAll = parameters != null && parameters.GetValue<bool>(ParamKey.RoundAll);
            bool hideStopped = parameters == null || !parameters.Any(p => p.Key == ParamKey.HideStoppedFans) || parameters.GetValue<bool>(ParamKey.HideStoppedFans);
            bool showControl = parameters != null && parameters.Any(p => p.Key == ParamKey.ShowFanControl) && parameters.GetValue<bool>(ParamKey.ShowFanControl);

            bool firstEnable = hardwareConfig == null || hardwareConfig.Length == 0;
            Dictionary<string, HardwareConfig> savedById = new Dictionary<string, HardwareConfig>(StringComparer.Ordinal);

            if (hardwareConfig != null)
            {
                foreach (HardwareConfig hc in hardwareConfig)
                {
                    if (hc != null && !string.IsNullOrEmpty(hc.ID) && !savedById.ContainsKey(hc.ID))
                    {
                        savedById[hc.ID] = hc;
                    }
                }
            }

            var selected = new List<(ISensor Sensor, string DisplayName, byte Order)>();

            foreach (ISensor fan in fanSensors)
            {
                string id = fan.Identifier.ToString();
                bool enabled;
                byte order = 0;
                string displayName = fan.Name;

                if (savedById.TryGetValue(id, out HardwareConfig saved))
                {
                    enabled = saved.Enabled;
                    order = saved.Order;
                    displayName = string.IsNullOrEmpty(saved.Name) ? fan.Name : saved.Name;
                }
                else
                {
                    // New fans discovered later default to disabled; on very first enable,
                    // enable every fan that currently reads > 0 RPM.
                    enabled = firstEnable && fan.Value.HasValue && fan.Value.Value > 0f;
                }

                if (enabled)
                {
                    selected.Add((fan, displayName, order));
                }
            }

            if (selected.Count == 0)
            {
                return Array.Empty<iMonitor>();
            }

            iMetric[] metrics = selected
                .OrderByDescending(x => x.Order)
                .ThenBy(x => x.DisplayName, StringComparer.CurrentCulture)
                .Select(x => (iMetric)new FanMetric(
                    x.Sensor,
                    showControl ? FanMetric.MatchControlSensor(x.Sensor, controlSensors) : null,
                    x.DisplayName,
                    roundAll,
                    hideStopped,
                    showControl))
                .ToArray();

            string boardId = board.Identifier?.ToString() ?? "Fans";
            string boardName = string.IsNullOrEmpty(board.Name) ? Resources.Fans : board.Name;

            return new iMonitor[]
            {
                new FanMonitor(boardId, boardName, showName, metrics)
            };
        }
    }
}
