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
        Error,
        // seeded from disk cache at startup while the first fresh poll is running
        Cached
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

        public string Plan { get; set; }

        public string Account { get; set; }

        public static UsageResult Ok(IReadOnlyList<UsageWindow> windows, string plan = null, string account = null)
        {
            return new UsageResult() { Status = UsageStatus.Ok, Windows = windows, Plan = plan, Account = account };
        }

        public static UsageResult Fail(UsageStatus status, TimeSpan? retryAfter = null, string account = null)
        {
            return new UsageResult() { Status = status, RetryAfter = retryAfter, Account = account };
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

    // Optional: a cheap local fingerprint of the provider's stored login (file time/size or a
    // hash of the credential blob). When it changes - the CLI refreshed its token, the user
    // signed in again or switched accounts - the registry re-polls at once instead of showing
    // an expired/old-account state until the next 5-minute poll. Never returns the secret.
    public interface ICredentialWatch
    {
        string CredentialSignature();
    }

    // Optional: read-only local lookup of the signed-in account (email or username), or null.
    // Called only during background polls and cached per credential signature; must never throw
    // or log the returned value.
    public interface IAccountReader
    {
        string ReadAccount();
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
        public string Plan { get; set; }
        public string Account { get; set; }

        public DateTimeOffset? LastGoodAt { get; set; }

        // the most recent poll failed and we are still showing the last good value
        public bool IsStale
        {
            get { return HasAttempt && Status != UsageStatus.Ok && Windows.Count > 0; }
        }
    }

    public sealed class LoginSession
    {
        public string ProviderId { get; set; }
        public System.Diagnostics.Process Process { get; set; }
        public DateTime StartedUtc { get; set; }
        public bool Active { get; set; } = true;
    }

    // Owns the provider list and ALL polling state, shared by every UsageMonitor
    // instance: a Settings save rebuilds the MonitorManager (and every monitor), and
    // without this the rebuild would immediately re-hit the endpoints (Claude's returns
    // HTTP 429 intermittently). Only providers that have a monitor (= are enabled)
    // are ever asked to poll; polling never blocks the caller.
    public static class UsageRegistry
    {
        public static Providers.ILoginLauncher LoginLauncher { get; set; } = new Providers.DefaultLoginLauncher();

        private static readonly Dictionary<string, LoginSession> _loginSessions = new Dictionary<string, LoginSession>();

        public static bool IsSigningIn(string id)
        {
            lock (_lock)
            {
                return _loginSessions.TryGetValue(id, out var session) && session.Active;
            }
        }

        public static LoginSession GetLoginSession(string id)
        {
            lock (_lock)
            {
                return _loginSessions.TryGetValue(id, out var session) ? session : null;
            }
        }

        public static void ResetLoginSessions()
        {
            lock (_lock)
            {
                _loginSessions.Clear();
            }
        }

        public static void ForceRepoll(string id)
        {
            lock (_lock)
            {
                if (_states.TryGetValue(id, out State state))
                {
                    state.NextPollUtc = DateTime.MinValue;
                    state.Failures = 0;
                }
            }
        }

        public static void OnPowerResume()
        {
            lock (_lock)
            {
                _fastWindowStartUtc = UtcNow();
                foreach (var state in _states.Values)
                {
                    state.Fetching = false;
                    state.NextPollUtc = DateTime.MinValue;
                    state.Failures = 0;
                }
            }

            foreach (IUsageProvider _provider in Providers)
            {
                if (_provider is Providers.AgyProvider _agy)
                {
                    _agy.NotifyPowerResume();
                }
            }
        }

        public static bool LaunchLogin(string id)
        {
            IUsageProvider provider = Find(id);
            if (provider == null) return false;

            lock (_lock)
            {
                if (_loginSessions.TryGetValue(id, out var existing) && existing.Active)
                {
                    if (existing.Process != null && !existing.Process.HasExited)
                    {
                        FocusProcess(existing.Process);
                        return false;
                    }
                }
            }

            var launcher = LoginLauncher;
            if (launcher == null) return false;

            System.Diagnostics.Process proc = launcher.Launch(id);
            if (proc == null) return false;

            lock (_lock)
            {
                var session = new LoginSession
                {
                    ProviderId = id,
                    Process = proc,
                    StartedUtc = DateTime.UtcNow,
                    Active = true
                };
                _loginSessions[id] = session;

                if (!_states.TryGetValue(id, out State state))
                {
                    state = new State();
                    _states[id] = state;
                }

                state.NextPollUtc = UtcNow() + TimeSpan.FromSeconds(5);
            }

            return true;
        }

        private static void FocusProcess(System.Diagnostics.Process proc)
        {
            try
            {
                if (proc != null && !proc.HasExited && proc.MainWindowHandle != IntPtr.Zero)
                {
                    SidebarDiagnostics.Windows.NativeMethods.ShowWindow(proc.MainWindowHandle, 9);
                    SidebarDiagnostics.Windows.NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
                }
            }
            catch { }
        }

        // hard floor between two polls of the same provider
        public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(60);

        // cap a honoured HTTP Retry-After so one 429 does not freeze the row for hours
        public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(15);

        // fast retry window after app start or power resume
        public static readonly TimeSpan FastRetryWindow = TimeSpan.FromMinutes(10);

        private static readonly TimeSpan[] FastRetryDelays = new TimeSpan[]
        {
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(40),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120)
        };

        internal static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        private static DateTime _fastWindowStartUtc = DateTime.UtcNow;

        // a single poll (process + network) may never run longer than this;
        // cold-start/post-resume agy runs get extra headroom above their 120 s timeout
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan ColdStartPollTimeout = TimeSpan.FromSeconds(135);

        private sealed class State
        {
            public UsageSnapshot Snapshot = UsageSnapshot.Empty;
            public DateTime NextPollUtc = DateTime.MinValue;
            public bool Fetching;
            public int Failures;
            public bool HasGoodPollInSession;
            public string CredentialSig;
            public DateTime NextWatchUtc = DateTime.MinValue;
            public bool RepollAfterFetch;
            public string CachedAccountSig;
            public string CachedAccount;
            public bool HasCachedAccount;
        }

        private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(5);

        // called under _lock
        private static void CheckCredentials(IUsageProvider provider, State state)
        {
            if (!(provider is ICredentialWatch _watch) || UtcNow() < state.NextWatchUtc)
            {
                return;
            }

            state.NextWatchUtc = UtcNow() + WatchInterval;

            string _sig;
            try { _sig = _watch.CredentialSignature(); }
            catch { _sig = null; }

            if (state.CredentialSig != null && !string.Equals(state.CredentialSig, _sig, StringComparison.Ordinal))
            {
                state.Failures = 0;
                state.NextPollUtc = DateTime.MinValue;
                state.HasCachedAccount = false;
                state.CachedAccountSig = null;
                state.CachedAccount = null;
                if (state.Fetching) state.RepollAfterFetch = true;
            }

            state.CredentialSig = _sig;
        }

        private static readonly object _lock = new object();

        private static readonly Dictionary<string, State> _states = new Dictionary<string, State>();

        public static readonly IReadOnlyList<IUsageProvider> Providers = new IUsageProvider[]
        {
            new Providers.ClaudeProvider(),
            new Providers.AgyProvider(),
            new Providers.CodexProvider(),
            new Providers.CopilotProvider(),
            new Providers.GeminiProvider(),
            new Providers.CursorProvider(),
            new Providers.GrokProvider(),
            new Providers.OpenCodeGoProvider()
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
                    _state.HasCachedAccount = false;
                    _state.CachedAccountSig = null;
                    _state.CachedAccount = null;
                }
            }
        }

        // Providers as settings "hardware" rows. Disabled by default: nothing is polled
        // until the user ticks a provider.
        // Settings: one toggle per bar the provider showed in its last successful poll, plus any
        // bar that is hidden right now (so it can be switched back on while the provider is offline)
        public static System.Collections.ObjectModel.ObservableCollection<UsageBarToggle> GetBarToggles(HardwareConfig config)
        {
            var _bars = new System.Collections.ObjectModel.ObservableCollection<UsageBarToggle>();
            var _seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (UsageWindow _w in GetSnapshot(config.ID).Windows)
            {
                string _key = UsageMonitor.BarKey(_w);
                if (_seen.Add(_key)) _bars.Add(new UsageBarToggle(config, _key, UsageMonitor.BarLabel(_w)));
            }

            foreach (string _key in config.HiddenBars ?? new string[0])
            {
                if (_seen.Add(_key))
                {
                    int _bar = _key.IndexOf('|');
                    string _slot = _bar >= 0 ? _key.Substring(_bar + 1) : string.Empty;
                    _bars.Add(new UsageBarToggle(config, _key, _slot.StartsWith("model:") ? _slot.Substring(6) : _key.Replace("|", " ")));
                }
            }

            return _bars;
        }

        public static string GetAccountText(string id)
        {
            string _account = GetSnapshot(id).Account;
            return string.IsNullOrWhiteSpace(_account) ? null : string.Format(Resources.SettingsUsageSignedInAs, _account);
        }

        public static HardwareConfig[] GetHardware()
        {
            return Providers.Select(p => new HardwareConfig() { ID = p.Id, Name = p.DisplayName, ActualName = p.DisplayName, Enabled = false, CustomPath = p.CustomPath, Status = GetStatusText(p.Id), AccountStatus = GetAccountText(p.Id) }).ToArray();
        }

        public static UsageSnapshot GetSnapshot(string id)
        {
            lock (_lock)
            {
                return _states.TryGetValue(id, out State _state) ? _state.Snapshot : UsageSnapshot.Empty;
            }
        }

        // Seeds an enabled provider's initial state from the disk cache before its first poll.
        // Disabled providers never call this or PollIfDue, so they are never seeded.
        public static void EnsureSeeded(IUsageProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            lock (_lock)
            {
                GetOrCreateStateLocked(provider);
            }
        }

        // caller must hold _lock
        private static State GetOrCreateStateLocked(IUsageProvider provider)
        {
            if (_states.TryGetValue(provider.Id, out State _state))
            {
                return _state;
            }

            _state = new State();
            DateTime _nowUtc = UtcNow();
            UsageSnapshot _seeded = UsageCache.TrySeed(provider, new DateTimeOffset(_nowUtc, TimeSpan.Zero), out string _sig);
            if (_seeded != null)
            {
                _state.Snapshot = _seeded;
            }

            if (provider is ICredentialWatch)
            {
                _state.CredentialSig = _sig;
                _state.NextWatchUtc = _nowUtc + WatchInterval;
            }

            // Always run the first real poll immediately on startup.
            _state.NextPollUtc = DateTime.MinValue;
            _states[provider.Id] = _state;
            return _state;
        }

        internal static void ResetStatesForTests(Func<DateTime> utcNow = null)
        {
            lock (_lock)
            {
                UtcNow = utcNow ?? (() => DateTime.UtcNow);
                _fastWindowStartUtc = UtcNow();
                _states.Clear();
                _loginSessions.Clear();
            }
        }

        internal static DateTime? GetNextPollUtcForTests(string id)
        {
            lock (_lock)
            {
                return _states.TryGetValue(id, out State _state) ? _state.NextPollUtc : (DateTime?)null;
            }
        }

        internal static TimeSpan ComputeFailureDelay(int failures, bool useFastLadder, UsageResult result)
        {
            if (failures < 1) failures = 1;

            TimeSpan _delay;
            if (useFastLadder && failures <= FastRetryDelays.Length)
            {
                _delay = FastRetryDelays[failures - 1];
            }
            else
            {
                int _normalFailures = useFastLadder ? failures - FastRetryDelays.Length : failures;
                _delay = TimeSpan.FromTicks(MinInterval.Ticks << Math.Min(_normalFailures - 1, 4));
                if (_delay > MaxBackoff)
                {
                    _delay = MaxBackoff;
                }
            }

            // the server's own Retry-After wins over our cap (but not beyond a day)
            // capped at MaxRetryAfter (15 minutes) so a single 429 does not freeze the row
            if (result != null && result.RetryAfter.HasValue)
            {
                TimeSpan _retryAfter = result.RetryAfter.Value > MaxRetryAfter ? MaxRetryAfter : result.RetryAfter.Value;
                if (_retryAfter > _delay)
                {
                    _delay = _retryAfter;
                }
            }

            // an expired login is fixed by using the CLI once, which can happen any
            // time: keep looking every MinInterval instead of backing off for an hour
            if (result != null && result.Status == UsageStatus.AuthExpired && !result.RetryIn.HasValue)
            {
                _delay = MinInterval;
            }

            if (result != null && result.RetryIn.HasValue)
            {
                _delay = result.RetryIn.Value < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : result.RetryIn.Value;
            }

            return _delay;
        }

        // Starts a background poll when one is due. Returns immediately.
        public static void PollIfDue(IUsageProvider provider)
        {
            lock (_lock)
            {
                State _state = GetOrCreateStateLocked(provider);

                if (_loginSessions.TryGetValue(provider.Id, out var session) && session.Active)
                {
                    if (UtcNow() - session.StartedUtc > TimeSpan.FromMinutes(10))
                    {
                        session.Active = false;
                    }
                    else if (session.Process != null)
                    {
                        bool exited = false;
                        bool isWt = false;
                        try
                        {
                            exited = session.Process.HasExited;
                            isWt = string.Equals(session.Process.ProcessName, "wt", StringComparison.OrdinalIgnoreCase);
                        }
                        catch { }

                        if (exited && !isWt)
                        {
                            session.Active = false;
                            _state.NextPollUtc = DateTime.MinValue;
                        }
                    }
                }

                CheckCredentials(provider, _state);

                if (_state.Fetching || UtcNow() < _state.NextPollUtc)
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
                TimeSpan _pollTimeout = (provider is Providers.AgyProvider _agy && _agy.IsColdStartPending)
                    ? ColdStartPollTimeout
                    : PollTimeout;
                using (CancellationTokenSource _cts = new CancellationTokenSource(_pollTimeout))
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
                _result = UsageResult.Fail(UsageStatus.Error, null, _result.Account);
            }

            string _sig = null;
            if (provider is ICredentialWatch _watch)
            {
                try { _sig = _watch.CredentialSignature(); }
                catch { _sig = null; }
            }

            bool _useAccountCache = false;
            string _account = null;

            lock (_lock)
            {
                if (_states.TryGetValue(provider.Id, out State _cachedState)
                    && _cachedState.HasCachedAccount
                    && _sig != null
                    && string.Equals(_cachedState.CachedAccountSig, _sig, StringComparison.Ordinal))
                {
                    _useAccountCache = true;
                    _account = _cachedState.CachedAccount;
                }
            }

            if (_result.Status == UsageStatus.NotInstalled || _result.Status == UsageStatus.NotLoggedIn)
            {
                _account = null;
            }
            else if (!string.IsNullOrWhiteSpace(_result.Account))
            {
                _account = _result.Account.Trim();
            }
            else if (!_useAccountCache && provider is IAccountReader _accountReader)
            {
                try { _account = _accountReader.ReadAccount(); }
                catch { _account = null; }
                _account = string.IsNullOrWhiteSpace(_account) ? null : _account.Trim();
            }

            UsageSnapshot _snapshotToCache = null;

            lock (_lock)
            {
                State _state = _states[provider.Id];

                _state.Fetching = false;
                if (_sig != null)
                {
                    _state.CachedAccountSig = _sig;
                    _state.CachedAccount = _account;
                    _state.HasCachedAccount = true;
                }

                bool _repollNow = _state.RepollAfterFetch;
                _state.RepollAfterFetch = false;

                UsageSnapshot _previous = _state.Snapshot;

                bool isSigningIn = _loginSessions.TryGetValue(provider.Id, out var session) && session.Active;

                if (_result.Status == UsageStatus.Ok)
                {
                    if (session != null) session.Active = false;
                    _state.Failures = 0;
                    _state.HasGoodPollInSession = true;
                    _state.NextPollUtc = _repollNow ? DateTime.MinValue : UtcNow() + MinInterval;

                    _state.Snapshot = new UsageSnapshot()
                    {
                        HasAttempt = true,
                        Status = UsageStatus.Ok,
                        Windows = _result.Windows,
                        Plan = _result.Plan,
                        Account = _account,
                        LastGoodAt = DateTimeOffset.Now
                    };
                    _snapshotToCache = _state.Snapshot;
                }
                else
                {
                    if (isSigningIn)
                    {
                        _state.NextPollUtc = UtcNow() + TimeSpan.FromSeconds(5);

                        _state.Snapshot = new UsageSnapshot()
                        {
                            HasAttempt = true,
                            Status = _result.Status,
                            Windows = _previous.Windows,
                            Plan = _previous.Plan,
                            Account = _account,
                            LastGoodAt = _previous.LastGoodAt
                        };
                    }
                    else
                    {
                        // fast retry ladder (20s, 40s, 60s, 120s) before the first good poll or within 10 minutes
                        // of start/resume; then 5, 10, 20, 40 minutes ... capped at one hour; honour Retry-After
                        _state.Failures = Math.Min(_state.Failures + 1, 12);

                        DateTime _nowUtc = UtcNow();
                        bool _useFastLadder = !_state.HasGoodPollInSession || (_nowUtc - _fastWindowStartUtc) < FastRetryWindow;
                        TimeSpan _delay = ComputeFailureDelay(_state.Failures, _useFastLadder, _result);

                        _state.NextPollUtc = _repollNow ? DateTime.MinValue : _nowUtc + _delay;

                        _state.Snapshot = new UsageSnapshot()
                        {
                            HasAttempt = true,
                            Status = _result.Status,
                            Windows = _previous.Windows,
                            Plan = _previous.Plan,
                            Account = _account,
                            LastGoodAt = _previous.LastGoodAt
                        };
                    }
                }
            }

            if (_snapshotToCache != null)
            {
                UsageCache.Save(provider.Id, _snapshotToCache, UsageCache.HashSignature(_sig));
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

                case UsageStatus.Cached:
                    return Resources.UsageLoading;

                default:
                    return Resources.UsageNA;
            }
        }

        // Live status line for the Settings provider list: local detection plus, once the
        // provider has been polled, the problem the last poll ran into. Local checks only.
        public static string GetStatusText(string id)
        {
            if (IsSigningIn(id))
            {
                return Resources.UsageWaitingForSignIn;
            }

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

            if (_snapshot.HasAttempt && _snapshot.Status != UsageStatus.Ok && _snapshot.Status != UsageStatus.Cached)
            {
                if (IsSigningIn(id))
                {
                    _text = string.Format("{0} - {1}", _text, Resources.UsageWaitingForSignIn);
                }
                else
                {
                    _text = string.Format("{0} - {1}", _text, StatusToText(_snapshot.Status));
                }
            }

            return _text;
        }
    }
}
