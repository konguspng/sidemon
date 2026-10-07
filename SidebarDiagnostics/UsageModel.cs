using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Monitoring
{
    // The bucket a quota window belongs to. Each kind maps to one metric toggle in
    // Settings > Monitors > AI CLI Usage (5-hour / Weekly / Monthly).
    public enum UsageKind : byte
    {
        FiveHour,
        Weekly,
        Monthly
    }

    // One quota window reported by a provider (e.g. Claude's 5-hour session).
    public sealed class UsageWindow
    {
        public UsageKind Kind { get; set; }

        // Optional sub-quota inside a kind (Copilot: "premium", "chat", "completions").
        // Null for the plain 5-hour / weekly windows.
        public string Slot { get; set; }

        // 0..100; null when the provider only has a textual note for this window.
        public double? UsedPercent { get; set; }

        public DateTimeOffset? ResetsAt { get; set; }

        public string Note { get; set; }
    }

    public enum UsageStatus : byte
    {
        Ok,
        NotInstalled,
        NotLoggedIn,
        // the stored token was rejected (401/403); we never refresh tokens ourselves
        AuthExpired,
        RateLimited,
        // provider is installed but has no read-only quota source
        NoUsageApi,
        // the account is signed in but has no plan that carries a quota (Copilot)
        NoPlan,
        Error
    }

    // Result of a cheap local check (files / PATH only, never the network).
    public enum UsageDetection : byte
    {
        NotInstalled,
        NotLoggedIn,
        Detected,
        DetectedNoUsageApi
    }

    public sealed class UsageResult
    {
        public UsageStatus Status { get; set; }

        public IReadOnlyList<UsageWindow> Windows { get; set; } = Array.Empty<UsageWindow>();

        // honoured on RateLimited (HTTP Retry-After)
        public TimeSpan? RetryAfter { get; set; }

        // overrides the next-poll delay outright (may be shorter than MinInterval); only
        // for failures that were detected locally without any network call
        public TimeSpan? RetryIn { get; set; }

        public static UsageResult Ok(IReadOnlyList<UsageWindow> windows)
        {
            return new UsageResult() { Status = UsageStatus.Ok, Windows = windows };
        }

        public static UsageResult Fail(UsageStatus status, TimeSpan? retryAfter = null)
        {
            return new UsageResult() { Status = status, RetryAfter = retryAfter };
        }
    }

    public interface IUsageProvider
    {
        // stable key stored in settings (HardwareConfig.ID)
        string Id { get; }

        string DisplayName { get; }

        // optional user supplied credentials file / executable, tried before the default
        // locations; null, empty or whitespace is ignored, environment variables are expanded
        string CustomPath { get; set; }

        // the tool (or its credential store) exists on this machine
        bool IsInstalled();

        // installed + signed in, determined locally without any network call
        UsageDetection Detect();

        // read-only fetch; must never throw for expected failures, must honour ct
        // and must never log, return or display a credential
        Task<UsageResult> FetchAsync(CancellationToken ct);
    }

    // Immutable view of what the registry currently knows about one provider.
    public sealed class UsageSnapshot
    {
        public static readonly UsageSnapshot Empty = new UsageSnapshot();

        // false until the first poll of this provider has completed
        public bool HasAttempt { get; set; }

        // outcome of the most recent poll
        public UsageStatus Status { get; set; }

        // last good windows; kept across failed polls
        public IReadOnlyList<UsageWindow> Windows { get; set; } = Array.Empty<UsageWindow>();

        public DateTimeOffset? LastGoodAt { get; set; }

        // the most recent poll failed and we are still showing the last good value
        public bool IsStale
        {
            get { return HasAttempt && Status != UsageStatus.Ok && Windows.Count > 0; }
        }
    }

    // Owns the provider list and ALL polling state, shared by every UsageMonitor
    // instance: a Settings save rebuilds the MonitorManager (and every monitor), and
    // without this the rebuild would immediately re-hit the endpoints (Claude's returns
    // HTTP 429 intermittently). Only providers that have a monitor (= are enabled)
    // are ever asked to poll; polling never blocks the caller.
    public static class UsageRegistry
    {
        // hard floor between two polls of the same provider
        public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(60);

        // a single poll (process + network) may never run longer than this
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(90);

        private sealed class State
        {
            public UsageSnapshot Snapshot = UsageSnapshot.Empty;
            public DateTime NextPollUtc = DateTime.MinValue;
            public bool Fetching;
            public int Failures;
        }

        private static readonly object _lock = new object();

        private static readonly Dictionary<string, State> _states = new Dictionary<string, State>();

        public static readonly IReadOnlyList<IUsageProvider> Providers = new IUsageProvider[]
        {
            new Providers.ClaudeProvider(),
            new Providers.AgyProvider(),
            new Providers.CodexProvider(),
            new Providers.CopilotProvider(),
            new Providers.GeminiProvider()
        };

        public static IUsageProvider Find(string id)
        {
            foreach (IUsageProvider _provider in Providers)
            {
                if (string.Equals(_provider.Id, id, StringComparison.Ordinal))
                {
                    return _provider;
                }
            }

            return null;
        }

        // Hands the user's "Custom path" to the provider so Detect() and FetchAsync() use
        // it. A changed path makes the provider due for polling again right away.
        public static void SetCustomPath(string id, string path)
        {
            IUsageProvider _provider = Find(id);

            if (_provider == null)
            {
                return;
            }

            string _new = string.IsNullOrWhiteSpace(path) ? null : path.Trim();

            if (string.Equals(_provider.CustomPath, _new, StringComparison.Ordinal))
            {
                return;
            }

            _provider.CustomPath = _new;

            lock (_lock)
            {
                if (_states.TryGetValue(id, out State _state))
                {
                    _state.NextPollUtc = DateTime.MinValue;
                    _state.Failures = 0;
                }
            }
        }

        // Providers as settings "hardware" rows. Disabled by default: nothing is polled
        // until the user ticks a provider.
        public static HardwareConfig[] GetHardware()
        {
            return Providers.Select(p => new HardwareConfig() { ID = p.Id, Name = p.DisplayName, ActualName = p.DisplayName, Enabled = false, CustomPath = p.CustomPath, Status = GetStatusText(p.Id) }).ToArray();
        }

        public static UsageSnapshot GetSnapshot(string id)
        {
            lock (_lock)
            {
                return _states.TryGetValue(id, out State _state) ? _state.Snapshot : UsageSnapshot.Empty;
            }
        }

        // Starts a background poll when one is due. Returns immediately.
        public static void PollIfDue(IUsageProvider provider)
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(provider.Id, out State _state))
                {
                    _state = new State();
                    _states[provider.Id] = _state;
                }

                if (_state.Fetching || DateTime.UtcNow < _state.NextPollUtc)
                {
                    return;
                }

                _state.Fetching = true;
            }

            Task.Run(() => PollAsync(provider));
        }

        private static async Task PollAsync(IUsageProvider provider)
        {
            UsageResult _result;

            try
            {
                using (CancellationTokenSource _cts = new CancellationTokenSource(PollTimeout))
                {
                    _result = await provider.FetchAsync(_cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                _result = UsageResult.Fail(UsageStatus.Error);
            }

            if (_result == null)
            {
                _result = UsageResult.Fail(UsageStatus.Error);
            }

            // a successful call that yielded no windows is "nothing to show", not a good value
            if (_result.Status == UsageStatus.Ok && _result.Windows.Count == 0)
            {
                _result = UsageResult.Fail(UsageStatus.Error);
            }

            lock (_lock)
            {
                State _state = _states[provider.Id];

                _state.Fetching = false;

                UsageSnapshot _previous = _state.Snapshot;

                if (_result.Status == UsageStatus.Ok)
                {
                    _state.Failures = 0;
                    _state.NextPollUtc = DateTime.UtcNow + MinInterval;

                    _state.Snapshot = new UsageSnapshot()
                    {
                        HasAttempt = true,
                        Status = UsageStatus.Ok,
                        Windows = _result.Windows,
                        LastGoodAt = DateTimeOffset.Now
                    };
                }
                else
                {
                    // 5, 10, 20, 40 minutes ... capped at one hour; honour Retry-After
                    _state.Failures = Math.Min(_state.Failures + 1, 8);

                    TimeSpan _delay = TimeSpan.FromTicks(MinInterval.Ticks << Math.Min(_state.Failures - 1, 4));

                    if (_delay > MaxBackoff)
                    {
                        _delay = MaxBackoff;
                    }

                    // the server's own Retry-After wins over our cap (but not beyond a day)
                    if (_result.RetryAfter.HasValue && _result.RetryAfter.Value > _delay)
                    {
                        _delay = _result.RetryAfter.Value > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : _result.RetryAfter.Value;
                    }

                    // an expired login is fixed by using the CLI once, which can happen any
                    // time: keep looking every MinInterval instead of backing off for an hour
                    if (_result.Status == UsageStatus.AuthExpired && !_result.RetryIn.HasValue)
                    {
                        _delay = MinInterval;
                    }

                    if (_result.RetryIn.HasValue)
                    {
                        _delay = _result.RetryIn.Value < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : _result.RetryIn.Value;
                    }

                    _state.NextPollUtc = DateTime.UtcNow + _delay;

                    _state.Snapshot = new UsageSnapshot()
                    {
                        HasAttempt = true,
                        Status = _result.Status,
                        Windows = _previous.Windows,
                        LastGoodAt = _previous.LastGoodAt
                    };
                }
            }
        }

        public static string StatusToText(UsageStatus status)
        {
            switch (status)
            {
                case UsageStatus.NotInstalled:
                    return Resources.UsageNotInstalled;

                case UsageStatus.NotLoggedIn:
                    return Resources.UsageNotLoggedIn;

                case UsageStatus.AuthExpired:
                    return Resources.UsageAuthExpired;

                case UsageStatus.RateLimited:
                    return Resources.UsageRateLimited;

                case UsageStatus.NoUsageApi:
                    return Resources.UsageDetectedNoApi;

                case UsageStatus.NoPlan:
                    return Resources.UsageNoPlan;

                case UsageStatus.Error:
                    return Resources.UsageError;

                default:
                    return Resources.UsageNA;
            }
        }

        // Live status line for the Settings provider list: local detection plus, once the
        // provider has been polled, the problem the last poll ran into. Local checks only.
        public static string GetStatusText(string id)
        {
            IUsageProvider _provider = Find(id);

            if (_provider == null)
            {
                return null;
            }

            UsageDetection _detection;

            try
            {
                _detection = _provider.Detect();
            }
            catch
            {
                _detection = UsageDetection.NotInstalled;
            }

            string _text;

            switch (_detection)
            {
                case UsageDetection.Detected:
                    _text = Resources.UsageDetected;
                    break;

                case UsageDetection.DetectedNoUsageApi:
                    return Resources.UsageDetectedNoApi;

                case UsageDetection.NotLoggedIn:
                    return Resources.UsageNotLoggedIn;

                default:
                    return Resources.UsageNotInstalled;
            }

            UsageSnapshot _snapshot = GetSnapshot(id);

            if (_snapshot.HasAttempt && _snapshot.Status != UsageStatus.Ok)
            {
                _text = string.Format("{0} - {1}", _text, StatusToText(_snapshot.Status));
            }

            return _text;
        }
    }
}
