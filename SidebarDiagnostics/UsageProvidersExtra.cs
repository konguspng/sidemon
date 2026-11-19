using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// Extra AI usage providers (OpenCode Go, Cursor, Grok Build). Sources and request shapes follow
// rozsazoltan/quota-usage-monitor (AGPL; behaviour re-implemented here, no code copied).
// All are read-only toward credentials: tokens/cookies are read locally, sent only to the
// provider's own HTTPS endpoint, never logged, stored or shown.
namespace SidebarDiagnostics.Monitoring.Providers
{
    // Minimal read-only access to SQLite files through winsqlite3.dll (ships with Windows 10+).
    internal static class WinSqlite
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_OPEN_READONLY = 0x00000001;

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int nBytes, IntPtr destructor);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_close_v2(IntPtr db);

        private static byte[] Utf8Z(string s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }

        // first column of the first row, or null; retries on a temp copy when the file is locked
        public static string QueryText(string path, string sql, string arg = null)
        {
            string _value;
            if (TryQuery(path, sql, arg, out _value)) return _value;

            string _copy = Path.Combine(Path.GetTempPath(), "sidemon-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                File.Copy(path, _copy, true);
                TryQuery(_copy, sql, arg, out _value);
                return _value;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(_copy); } catch { }
            }
        }

        private static bool TryQuery(string path, string sql, string arg, out string value)
        {
            value = null;
            IntPtr _db = IntPtr.Zero, _stmt = IntPtr.Zero;
            try
            {
                if (sqlite3_open_v2(Utf8Z(path), out _db, SQLITE_OPEN_READONLY, IntPtr.Zero) != SQLITE_OK) return false;
                if (sqlite3_prepare_v2(_db, Utf8Z(sql), -1, out _stmt, IntPtr.Zero) != SQLITE_OK) return false;
                if (arg != null && sqlite3_bind_text(_stmt, 1, Utf8Z(arg), -1, new IntPtr(-1)) != SQLITE_OK) return false;

                int _rc = sqlite3_step(_stmt);
                if (_rc == SQLITE_ROW)
                {
                    IntPtr _text = sqlite3_column_text(_stmt, 0);
                    int _len = sqlite3_column_bytes(_stmt, 0);
                    if (_text != IntPtr.Zero && _len > 0)
                    {
                        byte[] _bytes = new byte[_len];
                        Marshal.Copy(_text, _bytes, 0, _len);
                        value = Encoding.UTF8.GetString(_bytes);
                    }
                    return true;
                }

                return _rc == 101; // SQLITE_DONE: no row, but the query itself worked
            }
            catch
            {
                return false;
            }
            finally
            {
                if (_stmt != IntPtr.Zero) sqlite3_finalize(_stmt);
                if (_db != IntPtr.Zero) sqlite3_close_v2(_db);
            }
        }
    }

    internal static class ExtraHelper
    {
        public const string BrowserUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

        public static string Env(string name)
        {
            string _v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(_v) ? null : _v.Trim();
        }

        public static bool HasNewline(string s)
        {
            return s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0;
        }

        public static UsageResult FromStatus(HttpResponseMessage res)
        {
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return UsageResult.Fail(UsageStatus.AuthExpired);
            }

            if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return UsageResult.Fail(UsageStatus.RateLimited, res.Headers.RetryAfter?.Delta);
            }

            return null;
        }

        // payload of a JWT as JSON (no signature check: only used to read the user id / expiry)
        public static JsonDocument JwtPayload(string jwt)
        {
            try
            {
                string[] _parts = jwt.Split('.');
                if (_parts.Length < 2) return null;
                string _p = _parts[1].Replace('-', '+').Replace('_', '/');
                switch (_p.Length % 4) { case 2: _p += "=="; break; case 3: _p += "="; break; }
                return JsonDocument.Parse(Convert.FromBase64String(_p));
            }
            catch
            {
                return null;
            }
        }

        // runs a CLI hidden (no window, neutral folder), drains output, kills it on timeout
        public static async Task<string> RunHiddenAsync(string exe, string args, int timeoutSec)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
                bool _cmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
                var psi = new ProcessStartInfo
                {
                    FileName = _cmd ? "cmd.exe" : exe,
                    Arguments = _cmd ? "/d /c \"\"" + exe + "\" " + args + "\"" : args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                };

                using var p = Process.Start(psi);
                if (p == null) return null;
                try
                {
                    p.StandardInput.Close();
                    var _out = p.StandardOutput.ReadToEndAsync();
                    var _err = p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                    return await _out.ConfigureAwait(false);
                }
                finally
                {
                    try { if (!p.HasExited) p.Kill(true); } catch { }
                }
            }
            catch
            {
                return null;
            }
        }

        // full path of an executable on PATH (also .cmd/.exe), or null
        public static string FindOnPath(string name)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
            if (paths == null) return null;
            foreach (var dir in paths)
            {
                foreach (var ext in new[] { ".exe", ".cmd", "" })
                {
                    try
                    {
                        string _f = Path.Combine(dir.Trim('"'), name + ext);
                        if (File.Exists(_f)) return _f;
                    }
                    catch { }
                }
            }
            return null;
        }
    }

    // ---------------------------------------------------------------- OpenCode Go
    // Login: workspace ID + opencode.ai console session cookie (env OPENCODE_GO_WORKSPACE_ID /
    // OPENCODE_GO_AUTH_COOKIE, or %APPDATA%\opencode-go\config.json {workspaceId, authCookie}),
    // or the active "opencode" credential in opencode's own opencode.db.
    public class OpenCodeGoProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_OpenCodeGo";
        public string DisplayName => "OpenCode Go";
        public string CustomPath { get; set; }

        private const string StatusUrl = "https://opencode.ai/console/api/go/status";

        private sealed class Creds
        {
            public string Workspace;
            public string Cookie;
            public string Bearer;
        }

        private IEnumerable<string> ConfigPaths()
        {
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "config.json", "opencode-go.json");
            if (custom != null) yield return custom;
            string _env = ExtraHelper.Env("OPENCODE_GO_CONFIG_FILE");
            if (_env != null) yield return _env;
            yield return ProviderHelper.ResolvePath(@"%APPDATA%\opencode-go\config.json");
            string _xdg = ExtraHelper.Env("XDG_CONFIG_HOME");
            if (_xdg != null)
            {
                yield return Path.Combine(_xdg, "opencode-bar", "opencode-go.json");
                yield return Path.Combine(_xdg, "opencode-quota", "opencode-go.json");
            }
            yield return ProviderHelper.ResolvePath(@"%USERPROFILE%\.config\opencode-bar\opencode-go.json");
            yield return ProviderHelper.ResolvePath(@"%USERPROFILE%\.config\opencode-quota\opencode-go.json");
        }

        private string DbPath()
        {
            string _data = ExtraHelper.Env("OPENCODE_DATA_DIR");
            if (_data != null) return Path.Combine(_data, "opencode.db");
            string _xdg = ExtraHelper.Env("XDG_DATA_HOME");
            if (_xdg != null) return Path.Combine(_xdg, "opencode", "opencode.db");
            return ProviderHelper.ResolvePath(@"%USERPROFILE%\.local\share\opencode\opencode.db");
        }

        private static bool ValidWorkspace(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '_' && c != '-') return false;
            return true;
        }

        private Creds ReadCreds(bool allowDb)
        {
            string _ws = ExtraHelper.Env("OPENCODE_GO_WORKSPACE_ID");
            string _ck = ExtraHelper.Env("OPENCODE_GO_AUTH_COOKIE");
            if (ValidWorkspace(_ws) && _ck != null && !ExtraHelper.HasNewline(_ck)) return new Creds { Workspace = _ws, Cookie = _ck };

            foreach (string _path in ConfigPaths())
            {
                try
                {
                    if (!File.Exists(_path)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                    string _w = UsageParsers.GetString(doc.RootElement, "workspaceId") ?? UsageParsers.GetString(doc.RootElement, "workspaceID") ?? UsageParsers.GetString(doc.RootElement, "workspace_id");
                    string _c = UsageParsers.GetString(doc.RootElement, "authCookie") ?? UsageParsers.GetString(doc.RootElement, "cookie") ?? UsageParsers.GetString(doc.RootElement, "auth_cookie");
                    _w = _w?.Trim(); _c = _c?.Trim();
                    if (ValidWorkspace(_w) && !string.IsNullOrEmpty(_c) && !ExtraHelper.HasNewline(_c)) return new Creds { Workspace = _w, Cookie = _c };
                }
                catch { }
            }

            if (!allowDb) return null;

            try
            {
                string _db = DbPath();
                if (!File.Exists(_db)) return null;
                string _json = WinSqlite.QueryText(_db, "SELECT value FROM credential WHERE integration_id = 'opencode' AND active = 1 LIMIT 1");
                if (string.IsNullOrEmpty(_json)) return null;
                using var doc = JsonDocument.Parse(_json);
                string _access = UsageParsers.GetString(doc.RootElement, "access")?.Trim();
                string _org = null;
                if (doc.RootElement.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                {
                    _org = (UsageParsers.GetString(meta, "orgID") ?? UsageParsers.GetString(meta, "orgId"))?.Trim();
                }
                if (UsageParsers.TryGetNumber(doc.RootElement, "expires", out double _exp) && _exp > 0 && DateTimeOffset.FromUnixTimeMilliseconds((long)_exp) <= DateTimeOffset.UtcNow)
                {
                    return null;
                }
                if (ValidWorkspace(_org) && !string.IsNullOrEmpty(_access) && !ExtraHelper.HasNewline(_access)) return new Creds { Workspace = _org, Bearer = _access };
            }
            catch { }

            return null;
        }

        public bool IsInstalled()
        {
            if (ExtraHelper.Env("OPENCODE_GO_WORKSPACE_ID") != null) return true;
            foreach (string _p in ConfigPaths()) if (File.Exists(_p)) return true;
            return File.Exists(DbPath());
        }

        public UsageDetection Detect()
        {
            if (!IsInstalled()) return UsageDetection.NotInstalled;
            // config/env only here (Settings UI thread); the opencode.db lookup runs in the poll
            return ReadCreds(false) != null || File.Exists(DbPath()) ? UsageDetection.Detected : UsageDetection.NotLoggedIn;
        }

        public string ReadAccount()
        {
            foreach (string _path in ConfigPaths())
            {
                try
                {
                    if (!File.Exists(_path)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                    string _acct = (UsageParsers.GetString(doc.RootElement, "email") ?? UsageParsers.GetString(doc.RootElement, "user"))?.Trim();
                    if (!string.IsNullOrEmpty(_acct)) return _acct;
                }
                catch { }
            }

            try
            {
                string _db = DbPath();
                if (!File.Exists(_db)) return null;
                string _json = WinSqlite.QueryText(_db, "SELECT value FROM credential WHERE integration_id = 'opencode' AND active = 1 LIMIT 1");
                if (string.IsNullOrEmpty(_json)) return null;
                using var doc = JsonDocument.Parse(_json);
                string _acct = (UsageParsers.GetString(doc.RootElement, "email") ?? UsageParsers.GetString(doc.RootElement, "user"))?.Trim();
                if (string.IsNullOrEmpty(_acct) && doc.RootElement.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                {
                    _acct = (UsageParsers.GetString(meta, "email") ?? UsageParsers.GetString(meta, "user"))?.Trim();
                }
                if (!string.IsNullOrEmpty(_acct)) return _acct;
            }
            catch { }

            return null;
        }

        public string CredentialSignature()
        {
            var sb = new StringBuilder();
            foreach (string _p in ConfigPaths()) sb.Append(ProviderHelper.FileSignature(_p)).Append(';');
            sb.Append(ProviderHelper.FileSignature(DbPath()));
            return sb.ToString();
        }

        public async Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            if (!IsInstalled()) return UsageResult.Fail(UsageStatus.NotInstalled);

            Creds _creds = ReadCreds(true);
            if (_creds == null) return UsageResult.Fail(UsageStatus.NotLoggedIn);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, StatusUrl);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.TryAddWithoutValidation("x-org-id", _creds.Workspace);
                req.Headers.TryAddWithoutValidation("User-Agent", ExtraHelper.BrowserUA);
                if (_creds.Bearer != null)
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _creds.Bearer);
                }
                else
                {
                    bool _full = false;
                    foreach (string _part in _creds.Cookie.Split(';'))
                    {
                        string _t = _part.TrimStart();
                        if (_t.StartsWith("auth=", StringComparison.Ordinal) || _t.StartsWith("__Host-console_session=", StringComparison.Ordinal)) _full = true;
                    }
                    req.Headers.TryAddWithoutValidation("Cookie", _full ? _creds.Cookie : "auth=" + _creds.Cookie);
                }

                using var res = await ProviderHelper.Client.SendAsync(req, ct).ConfigureAwait(false);
                var _fail = ExtraHelper.FromStatus(res);
                if (_fail != null) return _fail;
                res.EnsureSuccessStatusCode();

                string _json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                var _windows = ParseStatus(_json, out bool _noPlan);
                if (_noPlan) return UsageResult.Fail(UsageStatus.NoPlan);
                return UsageResult.Ok(_windows, "Go");
            }
            catch (OperationCanceledException)
            {
                return UsageResult.Fail(UsageStatus.Error);
            }
            catch
            {
                return UsageResult.Fail(UsageStatus.Error);
            }
        }

        public static List<UsageWindow> ParseStatus(string json, out bool noPlan)
        {
            var _list = new List<UsageWindow>();
            noPlan = false;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("access", out var access) || access.ValueKind != JsonValueKind.Object)
            {
                noPlan = true;
                return _list;
            }

            if (!access.TryGetProperty("meters", out var meters) || meters.ValueKind != JsonValueKind.Object) return _list;

            void Add(string name, UsageKind kind, string resetOverride)
            {
                if (!meters.TryGetProperty(name, out var m) || m.ValueKind != JsonValueKind.Object) return;
                if (!UsageParsers.TryGetNumber(m, "limitMicroCents", out double _limit) || !UsageParsers.TryGetNumber(m, "usedMicroCents", out double _used)) return;
                _list.Add(new UsageWindow
                {
                    Kind = kind,
                    UsedPercent = UsageParsers.Clamp(_limit <= 0 ? 0 : _used / _limit * 100d),
                    ResetsAt = UsageParsers.ParseDate(resetOverride ?? UsageParsers.GetString(m, "resetsAt"))
                });
            }

            Add("fiveHour", UsageKind.FiveHour, null);
            Add("week", UsageKind.Weekly, null);
            Add("month", UsageKind.Monthly, UsageParsers.GetString(access, "endsAt"));
            return _list;
        }
    }

    // ---------------------------------------------------------------- Cursor
    // Login: Cursor's own session (cursorAuth/accessToken in %APPDATA%\Cursor\User\globalStorage\
    // state.vscdb, opened read-only) or env CURSOR_SESSION_TOKEN.
    public class CursorProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_Cursor";
        public string DisplayName => "Cursor";
        public string CustomPath { get; set; }

        private const string SummaryUrl = "https://cursor.com/api/usage-summary";

        private string StatePath()
        {
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "state.vscdb");
            if (custom != null) return custom;
            return ProviderHelper.ResolvePath(@"%APPDATA%\Cursor\User\globalStorage\state.vscdb");
        }

        public bool IsInstalled()
        {
            return ExtraHelper.Env("CURSOR_SESSION_TOKEN") != null || File.Exists(StatePath());
        }

        public UsageDetection Detect()
        {
            if (!IsInstalled()) return UsageDetection.NotInstalled;
            try { return ReadCookie() != null ? UsageDetection.Detected : UsageDetection.NotLoggedIn; }
            catch { return UsageDetection.NotLoggedIn; }
        }

        public string ReadAccount()
        {
            try
            {
                string _db = StatePath();
                if (!File.Exists(_db)) return null;
                string _email = WinSqlite.QueryText(_db, "SELECT value FROM ItemTable WHERE key = ?1", "cursorAuth/cachedEmail");
                if (string.IsNullOrWhiteSpace(_email)) return null;
                _email = _email.Trim().Trim('"').Trim();
                return string.IsNullOrEmpty(_email) ? null : _email;
            }
            catch
            {
                return null;
            }
        }

        public string CredentialSignature()
        {
            return ProviderHelper.FileSignature(StatePath());
        }

        private static string CookieFromAccessToken(string jwt)
        {
            using var doc = ExtraHelper.JwtPayload(jwt);
            string _sub = doc == null ? null : UsageParsers.GetString(doc.RootElement, "sub");
            if (string.IsNullOrEmpty(_sub)) return null;
            int _bar = _sub.LastIndexOf('|');
            string _user = _bar >= 0 ? _sub.Substring(_bar + 1) : _sub;
            return _user + "%3A%3A" + jwt;
        }

        private string ReadCookie()
        {
            string _env = ExtraHelper.Env("CURSOR_SESSION_TOKEN");
            if (_env != null && !ExtraHelper.HasNewline(_env))
            {
                string _t = _env.StartsWith("WorkosCursorSessionToken=", StringComparison.Ordinal) ? _env.Substring(25).Trim() : _env;
                if (_t.Contains("%3A%3A")) return _t;
                if (_t.Contains("::")) return _t.Replace("::", "%3A%3A");
                return CookieFromAccessToken(_t) ?? _t;
            }

            string _db = StatePath();
            if (!File.Exists(_db)) return null;
            string _token = WinSqlite.QueryText(_db, "SELECT value FROM ItemTable WHERE key = ?1", "cursorAuth/accessToken");
            if (string.IsNullOrEmpty(_token)) return null;
            _token = _token.Trim().Trim('"');
            return CookieFromAccessToken(_token);
        }

        public async Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            if (!IsInstalled()) return UsageResult.Fail(UsageStatus.NotInstalled);

            string _cookie = ReadCookie();
            if (string.IsNullOrEmpty(_cookie)) return UsageResult.Fail(UsageStatus.NotLoggedIn);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, SummaryUrl);
                req.Headers.TryAddWithoutValidation("Cookie", "WorkosCursorSessionToken=" + _cookie);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var res = await ProviderHelper.Client.SendAsync(req, ct).ConfigureAwait(false);
                var _fail = ExtraHelper.FromStatus(res);
                if (_fail != null) return _fail;
                res.EnsureSuccessStatusCode();

                string _json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                var _windows = ParseSummary(_json, out string _plan);
                if (_windows.Count == 0) return UsageResult.Fail(UsageStatus.NoPlan);
                return UsageResult.Ok(_windows, _plan);
            }
            catch
            {
                return UsageResult.Fail(UsageStatus.Error);
            }
        }

        public static List<UsageWindow> ParseSummary(string json, out string plan)
        {
            var _list = new List<UsageWindow>();
            plan = null;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return _list;

            DateTimeOffset? _reset = UsageParsers.ParseDate(UsageParsers.GetString(root, "billingCycleEnd"));
            string _type = UsageParsers.GetString(root, "membershipType");
            if (!string.IsNullOrEmpty(_type)) plan = char.ToUpper(_type[0]) + _type.Substring(1);

            if (!root.TryGetProperty("individualUsage", out var ind) || ind.ValueKind != JsonValueKind.Object) return _list;
            if (!ind.TryGetProperty("plan", out var p) || p.ValueKind != JsonValueKind.Object) return _list;

            bool _hasAuto = UsageParsers.TryGetNumber(p, "autoPercentUsed", out double _auto);
            bool _hasApi = UsageParsers.TryGetNumber(p, "apiPercentUsed", out double _api);
            bool _hasTotal = UsageParsers.TryGetNumber(p, "totalPercentUsed", out double _total);

            if (_hasAuto || _hasTotal)
            {
                _list.Add(new UsageWindow { Kind = UsageKind.Monthly, Slot = "auto", UsedPercent = UsageParsers.Clamp(_hasAuto ? _auto : _total), ResetsAt = _reset });
            }
            if (_hasApi)
            {
                _list.Add(new UsageWindow { Kind = UsageKind.Monthly, Slot = "api", UsedPercent = UsageParsers.Clamp(_api), ResetsAt = _reset });
            }
            return _list;
        }
    }

    // ---------------------------------------------------------------- Grok Build
    // Login: `grok login` session in %USERPROFILE%\.grok\auth.json (GROK_HOME overrides). Only
    // xAI sign-in entries are used. An expired session is renewed by the CLI itself (`grok models`).
    public class GrokProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_Grok";
        public string DisplayName => "Grok Build";
        public string CustomPath { get; set; }

        private const string BillingUrl = "https://cli-chat-proxy.grok.com/v1/billing?format=credits";

        private string _clientVersion;
        private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

        private string AuthPath()
        {
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "auth.json");
            if (custom != null) return custom;
            string _home = ExtraHelper.Env("GROK_HOME");
            if (_home != null) return Path.Combine(_home, "auth.json");
            return ProviderHelper.ResolvePath(@"%USERPROFILE%\.grok\auth.json");
        }

        private sealed class Session
        {
            public string Token;
            public string UserId;
            public bool CanRefresh;
        }

        private Session ReadSession()
        {
            try
            {
                string _path = AuthPath();
                if (!File.Exists(_path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                Session _best = null;
                DateTimeOffset _bestExp = DateTimeOffset.MinValue;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string _scope = prop.Name;
                    bool _xai = _scope == "https://accounts.x.ai/sign-in" || (_scope.StartsWith("https://auth.x.ai::", StringComparison.Ordinal) && _scope.Length > "https://auth.x.ai::".Length);
                    if (!_xai || prop.Value.ValueKind != JsonValueKind.Object) continue;

                    string _key = UsageParsers.GetString(prop.Value, "key")?.Trim();
                    if (string.IsNullOrEmpty(_key)) continue;

                    DateTimeOffset _exp = UsageParsers.ParseDate(UsageParsers.GetString(prop.Value, "expires_at")) ?? DateTimeOffset.MinValue;
                    if (_best == null || _exp > _bestExp)
                    {
                        _bestExp = _exp;
                        _best = new Session
                        {
                            Token = _key,
                            UserId = UsageParsers.GetString(prop.Value, "user_id")?.Trim(),
                            CanRefresh = !string.IsNullOrWhiteSpace(UsageParsers.GetString(prop.Value, "refresh_token"))
                        };
                    }
                }
                return _best;
            }
            catch
            {
                return null;
            }
        }

        public bool IsInstalled()
        {
            return File.Exists(AuthPath()) || ProviderHelper.IsOnPath("grok");
        }

        public UsageDetection Detect()
        {
            if (!IsInstalled()) return UsageDetection.NotInstalled;
            return ReadSession() != null ? UsageDetection.Detected : UsageDetection.NotLoggedIn;
        }

        public string ReadAccount()
        {
            try
            {
                string _path = AuthPath();
                if (!File.Exists(_path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                string _bestAcct = null;
                DateTimeOffset _bestExp = DateTimeOffset.MinValue;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string _scope = prop.Name;
                    bool _xai = _scope == "https://accounts.x.ai/sign-in" || (_scope.StartsWith("https://auth.x.ai::", StringComparison.Ordinal) && _scope.Length > "https://auth.x.ai::".Length);
                    if (!_xai || prop.Value.ValueKind != JsonValueKind.Object) continue;

                    string _acct = (UsageParsers.GetString(prop.Value, "email")
                        ?? UsageParsers.GetString(prop.Value, "username")
                        ?? UsageParsers.GetString(prop.Value, "user"))?.Trim();
                    if (string.IsNullOrEmpty(_acct)) continue;

                    DateTimeOffset _exp = UsageParsers.ParseDate(UsageParsers.GetString(prop.Value, "expires_at")) ?? DateTimeOffset.MinValue;
                    if (_bestAcct == null || _exp > _bestExp)
                    {
                        _bestExp = _exp;
                        _bestAcct = _acct;
                    }
                }
                return _bestAcct;
            }
            catch
            {
                return null;
            }
        }

        public string CredentialSignature()
        {
            return ProviderHelper.FileSignature(AuthPath());
        }

        private async Task<string> ClientVersionAsync()
        {
            if (_clientVersion != null) return _clientVersion;
            string _env = ExtraHelper.Env("GROK_CLIENT_VERSION");
            if (_env != null) return _clientVersion = _env;

            string _exe = ExtraHelper.FindOnPath("grok");
            if (_exe != null)
            {
                string _out = await ExtraHelper.RunHiddenAsync(_exe, "--version", 10).ConfigureAwait(false);
                var m = _out == null ? null : System.Text.RegularExpressions.Regex.Match(_out, @"\d+\.\d+\.\d+");
                if (m != null && m.Success) return _clientVersion = m.Value;
            }
            return "1.0.0";
        }

        private async Task<HttpResponseMessage> SendAsync(Session s, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, BillingUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
            req.Headers.TryAddWithoutValidation("X-XAI-Token-Auth", "xai-grok-cli");
            req.Headers.TryAddWithoutValidation("x-grok-client-mode", "cli");
            req.Headers.TryAddWithoutValidation("x-grok-client-version", await ClientVersionAsync().ConfigureAwait(false));
            if (!string.IsNullOrEmpty(s.UserId)) req.Headers.TryAddWithoutValidation("x-userid", s.UserId);
            return await ProviderHelper.Client.SendAsync(req, ct).ConfigureAwait(false);
        }

        public async Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            if (!IsInstalled()) return UsageResult.Fail(UsageStatus.NotInstalled);

            Session _s = ReadSession();
            if (_s == null) return UsageResult.Fail(UsageStatus.NotLoggedIn);

            try
            {
                var res = await SendAsync(_s, ct).ConfigureAwait(false);
                try
                {
                    // expired: let the CLI renew its own session (at most every 10 minutes), retry once
                    if ((res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        && _s.CanRefresh && DateTimeOffset.Now - _lastRefresh > TimeSpan.FromMinutes(10))
                    {
                        _lastRefresh = DateTimeOffset.Now;
                        string _exe = ExtraHelper.FindOnPath("grok");
                        if (_exe != null)
                        {
                            await ExtraHelper.RunHiddenAsync(_exe, "models", 30).ConfigureAwait(false);
                            Session _r = ReadSession();
                            if (_r != null && _r.Token != _s.Token)
                            {
                                res.Dispose();
                                res = await SendAsync(_r, ct).ConfigureAwait(false);
                            }
                        }
                    }

                    var _fail = ExtraHelper.FromStatus(res);
                    if (_fail != null) return _fail;
                    res.EnsureSuccessStatusCode();

                    string _json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var _windows = ParseBilling(_json);
                    if (_windows.Count == 0) return UsageResult.Fail(UsageStatus.NoPlan);
                    return UsageResult.Ok(_windows);
                }
                finally
                {
                    res.Dispose();
                }
            }
            catch
            {
                return UsageResult.Fail(UsageStatus.Error);
            }
        }

        public static List<UsageWindow> ParseBilling(string json)
        {
            var _list = new List<UsageWindow>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("config", out var cfg) || cfg.ValueKind != JsonValueKind.Object) return _list;

            DateTimeOffset? _end = null;
            UsageKind _kind = UsageKind.Monthly;
            if (cfg.TryGetProperty("currentPeriod", out var pt) && pt.ValueKind == JsonValueKind.Object)
            {
                string _type = UsageParsers.GetString(pt, "periodType");
                if (string.Equals(_type, "weekly", StringComparison.OrdinalIgnoreCase)) _kind = UsageKind.Weekly;
            }
            if (cfg.TryGetProperty("currentPeriod", out var period) && period.ValueKind == JsonValueKind.Object && period.TryGetProperty("end", out var end))
            {
                if (end.ValueKind == JsonValueKind.Number && end.TryGetDouble(out double _n) && _n > 0)
                {
                    _end = _n > 100_000_000_000d ? DateTimeOffset.FromUnixTimeMilliseconds((long)_n) : DateTimeOffset.FromUnixTimeSeconds((long)_n);
                }
                else if (end.ValueKind == JsonValueKind.String)
                {
                    _end = UsageParsers.ParseDate(end.GetString());
                }
            }

            double _pct;
            if (!UsageParsers.TryGetNumber(cfg, "creditUsagePercent", out _pct))
            {
                if (_end == null) return _list;
                _pct = 0;
            }

            _list.Add(new UsageWindow { Kind = _kind, Slot = "credits", UsedPercent = UsageParsers.Clamp(_pct), ResetsAt = _end });
            return _list;
        }
    }
}
