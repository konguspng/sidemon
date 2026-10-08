using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SidebarDiagnostics.Monitoring
{
    // Pure parsers for each provider's usage payload. No I/O, no WPF, no resources:
    // they take the raw response text and return windows (or throw JsonException /
    // return an empty list for unexpected shapes).
    internal static class UsageParsers
    {
        public const int FiveHourMaxSeconds = 6 * 3600;
        public const int WeeklyMinSeconds = 6 * 86400;
        public const int MonthlyMinSeconds = 25 * 86400;

        // ---- Claude Code: GET https://api.anthropic.com/api/oauth/usage -------------
        // { "five_hour": { "utilization": 33.0, "resets_at": "2026-10-06T21:29:59+00:00" },
        //   "seven_day": { ... },
        //   "limits": [ { "kind": "session"|"weekly_all", "percent": 33, "resets_at": "..." } ] }
        public static List<UsageWindow> ParseClaude(string json)
        {
            List<UsageWindow> _list = new List<UsageWindow>();

            using (JsonDocument _doc = JsonDocument.Parse(json))
            {
                JsonElement _root = _doc.RootElement;

                if (_root.ValueKind != JsonValueKind.Object)
                {
                    return _list;
                }

                AddClaudeWindow(_list, _root, "five_hour", UsageKind.FiveHour);
                AddClaudeWindow(_list, _root, "seven_day", UsageKind.Weekly);

                // newer payloads also carry a flat "limits" array; only use it if the
                // named windows were absent
                if (_list.Count == 0 && _root.TryGetProperty("limits", out JsonElement _limits) && _limits.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement _limit in _limits.EnumerateArray())
                    {
                        if (_limit.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        string _kind = GetString(_limit, "kind");
                        UsageKind? _usageKind = null;

                        if (string.Equals(_kind, "session", StringComparison.OrdinalIgnoreCase))
                        {
                            _usageKind = UsageKind.FiveHour;
                        }
                        else if (string.Equals(_kind, "weekly_all", StringComparison.OrdinalIgnoreCase))
                        {
                            _usageKind = UsageKind.Weekly;
                        }

                        if (_usageKind.HasValue && TryGetNumber(_limit, "percent", out double _percent) && !_list.Any(w => w.Kind == _usageKind.Value))
                        {
                            _list.Add(new UsageWindow()
                            {
                                Kind = _usageKind.Value,
                                UsedPercent = Clamp(_percent),
                                ResetsAt = ParseDate(GetString(_limit, "resets_at"))
                            });
                        }
                    }
                }
            }

            return _list;
        }

        private static void AddClaudeWindow(List<UsageWindow> list, JsonElement root, string name, UsageKind kind)
        {
            if (!root.TryGetProperty(name, out JsonElement _window) || _window.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!TryGetNumber(_window, "utilization", out double _utilization))
            {
                return;
            }

            list.Add(new UsageWindow()
            {
                Kind = kind,
                UsedPercent = Clamp(_utilization),
                ResetsAt = ParseDate(GetString(_window, "resets_at"))
            });
        }

        // ---- agy / Antigravity: `agy -p "/usage"` -----------------------------------
        // tab separated, one line per window:
        //   Gemini Models<TAB>Five Hour Limit Remaining<TAB>73%<TAB>2026-10-06T22:48:06Z
        // Every quota group agy reports is kept: "Gemini Models" and "Claude and GPT models"
        // are separate pools (a Claude/GPT-only agy user would otherwise see nothing).
        // With a single group the rows stay plain "5-hour"/"Weekly"; with several, each
        // window carries Slot = "grp:<group name>" so the monitor can prefix the group.
        private static readonly Regex AnsiEscape = new Regex("\u001B\\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

        public static List<UsageWindow> ParseAgy(string text)
        {
            List<UsageWindow> _list = new List<UsageWindow>();

            if (string.IsNullOrEmpty(text))
            {
                return _list;
            }

            text = AnsiEscape.Replace(text, string.Empty);

            List<KeyValuePair<string, UsageWindow>> _found = new List<KeyValuePair<string, UsageWindow>>();

            foreach (string _rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string _line = _rawLine.Trim();

                if (_line.IndexOf("Limit Remaining", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string[] _parts = _line.Split('\t');

                if (_parts.Length < 3)
                {
                    continue;
                }

                string _group = _parts[0].Trim();
                string _what = _parts[1].Trim();

                UsageKind _kind;

                if (_what.IndexOf("Weekly", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _kind = UsageKind.Weekly;
                }
                else if (_what.IndexOf("Hour", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _kind = UsageKind.FiveHour;
                }
                else if (_what.IndexOf("Month", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _kind = UsageKind.Monthly;
                }
                else
                {
                    continue;
                }

                string _pct = _parts[2].Trim().TrimEnd('%').Trim();

                if (!double.TryParse(_pct, NumberStyles.Float, CultureInfo.InvariantCulture, out double _remaining))
                {
                    continue;
                }

                UsageWindow _window = new UsageWindow()
                {
                    Kind = _kind,
                    UsedPercent = Clamp(100d - _remaining),
                    ResetsAt = _parts.Length > 3 ? ParseDate(_parts[3].Trim()) : null
                };

                _found.Add(new KeyValuePair<string, UsageWindow>(_group, _window));
            }

            bool _multiGroup = _found.Select(f => f.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

            // one row per (group, kind): a repeated line for the same window is ignored
            foreach (var _item in _found)
            {
                string _slot = _multiGroup ? "grp:" + _item.Key : null;

                if (!_list.Any(w => w.Kind == _item.Value.Kind && string.Equals(w.Slot, _slot, StringComparison.Ordinal)))
                {
                    _item.Value.Slot = _slot;
                    _list.Add(_item.Value);
                }
            }

            return _list.OrderBy(w => w.Slot, StringComparer.Ordinal).ThenBy(w => w.Kind).ToList();
        }

        // ---- OpenAI Codex CLI: GET https://chatgpt.com/backend-api/wham/usage --------
        // { "plan_type": "plus",
        //   "rate_limit": {
        //     "primary_window":   { "used_percent": 12, "limit_window_seconds": 18000,  "reset_at": 1759999999 },
        //     "secondary_window": { "used_percent":  4, "limit_window_seconds": 604800, "reset_at": 1760500000 } },
        //   "credits": { "has_credits": true, "unlimited": false, "balance": "5.00" } }
        public static void ParseCodex(string json, DateTimeOffset now, out List<UsageWindow> list, out string plan)
        {
            list = new List<UsageWindow>();
            plan = null;

            using (JsonDocument _doc = JsonDocument.Parse(json))
            {
                JsonElement _root = _doc.RootElement;
                if (_root.ValueKind != JsonValueKind.Object) return;

                if (_root.TryGetProperty("plan_type", out JsonElement _plan) && _plan.ValueKind == JsonValueKind.String)
                {
                    string p = _plan.GetString();
                    if (!string.IsNullOrEmpty(p)) plan = char.ToUpper(p[0]) + p.Substring(1);
                }

                if (!_root.TryGetProperty("rate_limit", out JsonElement _rate) || _rate.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                AddCodexWindow(list, _rate, "primary_window", UsageKind.FiveHour, now);
                AddCodexWindow(list, _rate, "secondary_window", UsageKind.Weekly, now);
            }

            list = list.OrderBy(w => w.Kind).ToList();
        }

        private static void AddCodexWindow(List<UsageWindow> list, JsonElement rate, string name, UsageKind fallbackKind, DateTimeOffset now)
        {
            if (!rate.TryGetProperty(name, out JsonElement _window) || _window.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!TryGetNumber(_window, "used_percent", out double _used))
            {
                return;
            }

            // label windows by their real size, not by primary/secondary
            UsageKind? _kind = fallbackKind;

            if (TryGetNumber(_window, "limit_window_seconds", out double _seconds) && _seconds > 0)
            {
                _kind = ClassifySeconds(_seconds);
            }

            if (!_kind.HasValue || list.Any(w => w.Kind == _kind.Value))
            {
                return;
            }

            DateTimeOffset? _resets = null;

            if (_window.TryGetProperty("reset_at", out JsonElement _resetAt))
            {
                _resets = ParseUnixOrDate(_resetAt);
            }

            if (!_resets.HasValue && TryGetNumber(_window, "reset_after_seconds", out double _after) && _after >= 0)
            {
                _resets = now.AddSeconds(_after);
            }

            list.Add(new UsageWindow()
            {
                Kind = _kind.Value,
                UsedPercent = Clamp(_used),
                ResetsAt = _resets
            });
        }

        // <=6h => 5-hour, >=6d => weekly, >=25d => monthly, anything in between is not a
        // window SideMon has a row for
        public static UsageKind? ClassifySeconds(double seconds)
        {
            if (seconds <= FiveHourMaxSeconds)
            {
                return UsageKind.FiveHour;
            }

            if (seconds >= MonthlyMinSeconds)
            {
                return UsageKind.Monthly;
            }

            if (seconds >= WeeklyMinSeconds)
            {
                return UsageKind.Weekly;
            }

            return null;
        }

        // ---- GitHub Copilot: GET https://api.github.com/copilot_internal/user --------
        // paid:  quota_snapshots.{premium_interactions,chat,completions}
        //          { percent_remaining, entitlement, remaining, unlimited }
        // free:  limited_user_quotas {chat,completions} (remaining) vs monthly_quotas (total)
        // reset: quota_reset_date_utc | quota_reset_date | limited_user_reset_date
        public static List<UsageWindow> ParseCopilot(string json)
        {
            List<UsageWindow> _list = new List<UsageWindow>();

            using (JsonDocument _doc = JsonDocument.Parse(json))
            {
                JsonElement _root = _doc.RootElement;

                if (_root.ValueKind != JsonValueKind.Object)
                {
                    return _list;
                }

                DateTimeOffset? _reset =
                    GetDate(_root, "quota_reset_date_utc") ??
                    GetDate(_root, "quota_reset_date") ??
                    GetDate(_root, "limited_user_reset_date");

                if (_root.TryGetProperty("quota_snapshots", out JsonElement _snapshots) && _snapshots.ValueKind == JsonValueKind.Object)
                {
                    AddCopilotSnapshot(_list, _snapshots, "premium_interactions", "premium", _reset);
                    AddCopilotSnapshot(_list, _snapshots, "chat", "chat", _reset);
                    AddCopilotSnapshot(_list, _snapshots, "completions", "completions", _reset);
                }

                if (_root.TryGetProperty("limited_user_quotas", out JsonElement _limited) && _limited.ValueKind == JsonValueKind.Object &&
                    _root.TryGetProperty("monthly_quotas", out JsonElement _monthly) && _monthly.ValueKind == JsonValueKind.Object)
                {
                    AddCopilotFreeQuota(_list, _limited, _monthly, "chat", _reset);
                    AddCopilotFreeQuota(_list, _limited, _monthly, "completions", _reset);
                }
            }

            return _list;
        }

        private static void AddCopilotSnapshot(List<UsageWindow> list, JsonElement snapshots, string name, string slot, DateTimeOffset? reset)
        {
            if (list.Any(w => w.Slot == slot) || !snapshots.TryGetProperty(name, out JsonElement _snap) || _snap.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (_snap.TryGetProperty("unlimited", out JsonElement _unlimited) && _unlimited.ValueKind == JsonValueKind.True)
            {
                return;
            }

            bool _hasEntitlement = TryGetNumber(_snap, "entitlement", out double _entitlement);

            // entitlement -1 / 0 means "no metered quota"
            if (_hasEntitlement && _entitlement <= 0)
            {
                return;
            }

            double _used;

            if (TryGetNumber(_snap, "percent_remaining", out double _remainingPercent))
            {
                _used = 100d - _remainingPercent;
            }
            else if (_hasEntitlement && TryGetNumber(_snap, "remaining", out double _remaining))
            {
                _used = (_entitlement - _remaining) / _entitlement * 100d;
            }
            else
            {
                return;
            }

            list.Add(new UsageWindow()
            {
                Kind = UsageKind.Monthly,
                Slot = slot,
                UsedPercent = Clamp(_used),
                ResetsAt = reset
            });
        }

        private static void AddCopilotFreeQuota(List<UsageWindow> list, JsonElement limited, JsonElement monthly, string name, DateTimeOffset? reset)
        {
            if (list.Any(w => w.Slot == name))
            {
                return;
            }

            if (!TryGetNumber(limited, name, out double _left) || !TryGetNumber(monthly, name, out double _total) || _total <= 0)
            {
                return;
            }

            list.Add(new UsageWindow()
            {
                Kind = UsageKind.Monthly,
                Slot = name,
                UsedPercent = Clamp((_total - _left) / _total * 100d),
                ResetsAt = reset
            });
        }

        // ---- shared helpers ---------------------------------------------------------

        public static double Clamp(double percent)
        {
            if (double.IsNaN(percent))
            {
                return 0d;
            }

            return Math.Max(0d, Math.Min(100d, percent));
        }

        // a date given as an ISO string or as unix seconds / milliseconds
        public static DateTimeOffset? GetDate(JsonElement obj, string name)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement _value))
            {
                return ParseUnixOrDate(_value);
            }

            return null;
        }

        public static string GetString(JsonElement obj, string name)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement _value) && _value.ValueKind == JsonValueKind.String)
            {
                return _value.GetString();
            }

            return null;
        }

        public static bool TryGetNumber(JsonElement obj, string name, out double value)
        {
            value = 0d;

            if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out JsonElement _value))
            {
                return false;
            }

            if (_value.ValueKind == JsonValueKind.Number)
            {
                return _value.TryGetDouble(out value);
            }

            if (_value.ValueKind == JsonValueKind.String)
            {
                return double.TryParse(_value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            return false;
        }

        public static DateTimeOffset? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset _date))
            {
                return _date;
            }

            return null;
        }

        // unix seconds (or milliseconds, if it is implausibly large) or an ISO string
        public static DateTimeOffset? ParseUnixOrDate(JsonElement value)
        {
            try
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double _number))
                {
                    if (_number <= 0)
                    {
                        return null;
                    }

                    return _number > 1e11 ? DateTimeOffset.FromUnixTimeMilliseconds((long)_number) : DateTimeOffset.FromUnixTimeSeconds((long)_number);
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    return ParseDate(value.GetString());
                }
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            return null;
        }
    }
}
