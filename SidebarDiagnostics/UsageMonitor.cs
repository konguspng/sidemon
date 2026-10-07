using System;
using System.Collections.Generic;
using System.Linq;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Monitoring
{
    // One row in the sidebar: a quota window ("5-hour 33% - 2h 10m") or a status line.
    // Rendered by the same metric template (label, value, accent bar) as every other
    // monitor; numeric rows also feed the bar and the alert color.
    public class UsageMetric : BaseMetric
    {
        public UsageMetric(MetricKey key, string label, bool round, double alertValue) : base(key, DataType.Percent, label, round, alertValue)
        {
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~UsageMetric()
        {
            Dispose(false);
        }

        // quota rows are polled every few minutes: a history graph of them is meaningless,
        // and rows are rebuilt when the provider's window set changes
        public override bool IsNumeric
        {
            get { return false; }
        }

        // used percentage (0..100) plus an optional "resets in ..." suffix
        public void SetUsage(double percent, string suffix)
        {
            if (_hasValue && _lastPercent == percent && string.Equals(_lastSuffix, suffix, StringComparison.Ordinal))
            {
                return;
            }

            _hasValue = true;
            _lastPercent = percent;
            _lastSuffix = suffix;
            _lastNote = null;

            Update(percent);

            string _text = string.Format("{0:#,##0.##}%", percent.Round(_round));

            if (!string.IsNullOrEmpty(suffix))
            {
                _text = string.Format("{0} \u00B7 {1}", _text, suffix);
            }

            Text = _text;
        }

        // textual state ("Loading...", "N/A", "Not logged in"): no bar, no alert
        public void SetNote(string text)
        {
            if (!_hasValue && string.Equals(_lastNote, text, StringComparison.Ordinal))
            {
                return;
            }

            _hasValue = false;
            _lastNote = text;

            Update(0d);

            Text = text;
        }

        private bool _hasValue;

        private double _lastPercent;

        private string _lastSuffix;

        private string _lastNote;

    }

    // One AI CLI provider (Claude Code, agy, Codex, Copilot...). Never talks to the
    // network itself: UsageRegistry polls enabled providers on a background task and
    // this monitor only turns the latest snapshot into rows during the normal sensor
    // update tick.
    public class UsageMonitor : BaseMonitor
    {
        public UsageMonitor(IUsageProvider provider, string name, MetricConfig[] metrics, bool showName, bool roundAll, double alertValue) : base(provider.Id, name, showName)
        {
            _provider = provider;
            _metricConfig = metrics;
            _roundAll = roundAll;
            _alertValue = alertValue;

            Metrics = new iMetric[0];
        }

        public static iMonitor[] GetInstances(HardwareConfig[] hardwareConfig, MetricConfig[] metrics, ConfigParam[] parameters)
        {
            bool _showName = parameters.GetValue<bool>(ParamKey.HardwareNames);
            bool _roundAll = parameters.GetValue<bool>(ParamKey.RoundAll);
            int _alert = parameters.GetValue<int>(ParamKey.UsageAlert);

            List<iMonitor> _list = new List<iMonitor>();

            // a provider is only polled when the user explicitly ticked it: an entry
            // that was never saved counts as disabled
            var _enabled = (
                from provider in UsageRegistry.Providers
                join c in hardwareConfig on provider.Id equals c.ID
                where c.Enabled
                orderby c.Order descending, provider.DisplayName ascending
                select new { provider, c }
                ).ToArray();

            // push every saved path to its provider (also for providers that are off, so the
            // Settings status column reflects it)
            foreach (HardwareConfig _c in hardwareConfig)
            {
                UsageRegistry.SetCustomPath(_c.ID, _c.CustomPath);
            }

            foreach (var _item in _enabled)
            {
                string _name = string.IsNullOrEmpty(_item.c.Name) ? _item.provider.DisplayName : _item.c.Name;

                _list.Add(new UsageMonitor(_item.provider, _name, metrics, _showName, _roundAll, _alert));
            }

            return _list.ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        ~UsageMonitor()
        {
            Dispose(false);
        }

        public override void Update()
        {
            UsageRegistry.PollIfDue(_provider);

            UsageSnapshot _snapshot = UsageRegistry.GetSnapshot(_provider.Id);

            List<UsageWindow> _windows = _snapshot.Windows
                .Where(w => IsKindEnabled(w.Kind))
                .OrderBy(w => SlotOrder(w.Slot))
                .ThenBy(w => w.Kind)
                .ToList();

            bool _statusRow = _snapshot.Windows.Count == 0;

            string _signature = _statusRow
                ? "status"
                : string.Join("|", _windows.Select(w => string.Format("{0}:{1}", w.Kind, w.Slot)));

            if (!string.Equals(_signature, _currentSignature, StringComparison.Ordinal))
            {
                iMetric[] _old = Metrics;

                List<UsageMetric> _rows = new List<UsageMetric>();

                if (_statusRow)
                {
                    _rows.Add(new UsageMetric(MetricKey.UsageStatus, Resources.UsageStatusLabel, _roundAll, 0d));
                }
                else
                {
                    foreach (UsageWindow _window in _windows)
                    {
                        _rows.Add(new UsageMetric(KindToKey(_window.Kind), GetLabel(_window), _roundAll, _alertValue));
                    }
                }

                _currentRows = _rows.ToArray();
                _currentWindows = _windows;
                _currentSignature = _signature;

                Metrics = _currentRows.Cast<iMetric>().ToArray();

                // WPF may still be unbinding from the old rows: release them on the UI
                // thread once it is idle instead of from this polling thread
                DisposeLater(_old);
            }

            if (_statusRow)
            {
                _currentRows[0].SetNote(_snapshot.HasAttempt ? UsageRegistry.StatusToText(_snapshot.Status) : Resources.UsageLoading);

                return;
            }

            for (int i = 0; i < _currentRows.Length; i++)
            {
                UsageWindow _window = _windows[i];

                if (_window.UsedPercent.HasValue)
                {
                    string _suffix = FormatReset(_window.ResetsAt);

                    if (_snapshot.IsStale)
                    {
                        _suffix = string.IsNullOrEmpty(_suffix) ? Resources.UsageStale : string.Format("{0} ({1})", _suffix, Resources.UsageStale);
                    }

                    _currentRows[i].SetUsage(_window.UsedPercent.Value, _suffix);
                }
                else
                {
                    _currentRows[i].SetNote(string.IsNullOrEmpty(_window.Note) ? Resources.UsageNA : _window.Note);
                }
            }
        }

        private static void DisposeLater(iMetric[] metrics)
        {
            if (metrics == null || metrics.Length == 0)
            {
                return;
            }

            System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Application.Current?.Dispatcher;

            Action _dispose = () =>
            {
                foreach (iMetric _metric in metrics)
                {
                    _metric.Dispose();
                }
            };

            if (_dispatcher == null)
            {
                _dispose();
            }
            else
            {
                _dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, _dispose);
            }
        }

        private bool IsKindEnabled(UsageKind kind)
        {
            return _metricConfig.IsEnabled(KindToKey(kind));
        }

        private static MetricKey KindToKey(UsageKind kind)
        {
            switch (kind)
            {
                case UsageKind.Weekly:
                    return MetricKey.UsageWeekly;

                case UsageKind.Monthly:
                    return MetricKey.UsageMonthly;

                default:
                    return MetricKey.UsageFiveHour;
            }
        }

        private static int SlotOrder(string slot)
        {
            switch (slot)
            {
                case null:
                    return 0;

                case "premium":
                    return 1;

                case "chat":
                    return 2;

                case "completions":
                    return 3;

                default:
                    // agy quota groups: Gemini first, then Claude/GPT, then anything else
                    if (IsGroup(slot, "Gemini")) return 10;
                    if (IsGroup(slot, "Claude") || IsGroup(slot, "GPT")) return 11;
                    return slot != null && slot.StartsWith("grp:", StringComparison.Ordinal) ? 12 : 4;
            }
        }

        private static bool IsGroup(string slot, string name)
        {
            return slot != null && slot.StartsWith("grp:", StringComparison.Ordinal) && slot.IndexOf(name, 4, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetLabel(UsageWindow window)
        {
            switch (window.Slot)
            {
                case null:
                    return KindToKey(window.Kind).GetLabel();

                case "premium":
                    return Resources.UsageSlotPremium;

                case "chat":
                    return Resources.UsageSlotChat;

                case "completions":
                    return Resources.UsageSlotCompletions;

                default:
                    if (window.Slot != null && window.Slot.StartsWith("grp:", StringComparison.Ordinal))
                    {
                        string _group = IsGroup(window.Slot, "Gemini") ? Resources.UsageGroupGemini
                            : (IsGroup(window.Slot, "Claude") || IsGroup(window.Slot, "GPT")) ? Resources.UsageGroupClaudeGpt
                            : window.Slot.Substring(4);

                        return string.Format("{0} {1}", _group, KindToKey(window.Kind).GetLabel());
                    }

                    return window.Slot;
            }
        }

        // "2d 4h", "2h 10m", "35m"; empty when unknown or already past
        private static string FormatReset(DateTimeOffset? resetsAt)
        {
            if (!resetsAt.HasValue)
            {
                return null;
            }

            TimeSpan _left = resetsAt.Value - DateTimeOffset.Now;

            if (_left <= TimeSpan.Zero)
            {
                return null;
            }

            string _text;

            if (_left.TotalDays >= 1d)
            {
                _text = string.Format("{0}d {1}h", (int)_left.TotalDays, _left.Hours);
            }
            else if (_left.TotalHours >= 1d)
            {
                _text = string.Format("{0}h {1}m", (int)_left.TotalHours, _left.Minutes);
            }
            else
            {
                _text = string.Format("{0}m", Math.Max(1, (int)Math.Ceiling(_left.TotalMinutes)));
            }

            return string.Format(Resources.UsageResetsIn, _text);
        }

        private readonly IUsageProvider _provider;

        private readonly MetricConfig[] _metricConfig;

        private readonly bool _roundAll;

        private readonly double _alertValue;

        private string _currentSignature;

        private UsageMetric[] _currentRows = new UsageMetric[0];

        private List<UsageWindow> _currentWindows = new List<UsageWindow>();
    }
}
