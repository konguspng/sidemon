using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SidebarDiagnostics.Monitoring;

namespace SidebarDiagnostics.Monitoring.Providers
{
    public static class ProviderHelper
    {
        public static string ResolvePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : Environment.ExpandEnvironmentVariables(path);
        }

        public static bool FileExists(string path)
        {
            return File.Exists(ResolvePath(path));
        }

        // The user's "Custom path" as an existing file, or null. When it names a folder the
        // given file name(s) are looked up inside it (e.g. the folder holding auth.json).
        // Empty / whitespace / missing targets are ignored so the defaults still apply.
        public static string ResolveCustomFile(string custom, params string[] fileNames)
        {
            if (string.IsNullOrWhiteSpace(custom))
            {
                return null;
            }

            try
            {
                string path = ResolvePath(custom.Trim().Trim('"'));

                if (File.Exists(path))
                {
                    if (fileNames == null || fileNames.Length == 0) return path;
                    string fileName = Path.GetFileName(path);
                    foreach (var fn in fileNames)
                    {
                        if (string.Equals(fn, fileName, StringComparison.OrdinalIgnoreCase)) return path;
                    }
                }

                if (Directory.Exists(path))
                {
                    foreach (var name in fileNames)
                    {
                        string candidate = Path.Combine(path, name);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }

            return null;
        }

        public static bool IsOnPath(string exeName)
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
            if (paths == null) return false;
            foreach (var path in paths)
            {
                if (File.Exists(Path.Combine(path, exeName)) || File.Exists(Path.Combine(path, exeName + ".exe")) || File.Exists(Path.Combine(path, exeName + ".cmd")))
                    return true;
            }
            return false;
        }

        public static HttpClient Client { get; set; } = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // "time|size" of a file, or "missing"; used as a credential fingerprint
        public static string FileSignature(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "missing";
                FileInfo _info = new FileInfo(path);
                return _info.Exists ? _info.LastWriteTimeUtc.Ticks + "|" + _info.Length : "missing";
            }
            catch
            {
                return "error";
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [System.Runtime.InteropServices.DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

        // Generic Credential Manager entries whose target matches filter (e.g. "gh:github.com:*"),
        // newest first. Secrets stay in memory for the caller only.
        public static List<(string Target, string Secret, long Written)> CredentialManagerEntries(string filter)
        {
            var _list = new List<(string, string, long)>();
            IntPtr _ptr = IntPtr.Zero;
            try
            {
                if (!CredEnumerate(filter, 0, out int _count, out _ptr) || _ptr == IntPtr.Zero) return _list;
                for (int i = 0; i < _count; i++)
                {
                    IntPtr _item = System.Runtime.InteropServices.Marshal.ReadIntPtr(_ptr, i * IntPtr.Size);
                    CREDENTIAL _c = System.Runtime.InteropServices.Marshal.PtrToStructure<CREDENTIAL>(_item);
                    if (_c.Type != 1 || _c.CredentialBlobSize <= 0 || _c.CredentialBlob == IntPtr.Zero) continue;
                    byte[] _blob = new byte[_c.CredentialBlobSize];
                    System.Runtime.InteropServices.Marshal.Copy(_c.CredentialBlob, _blob, 0, _blob.Length);
                    // gh/copilot store UTF-8; older tools UTF-16
                    string _s = _blob.Length >= 2 && _blob[1] == 0 ? System.Text.Encoding.Unicode.GetString(_blob) : System.Text.Encoding.UTF8.GetString(_blob);
                    Array.Clear(_blob, 0, _blob.Length);
                    long _w = ((long)_c.LastWritten.dwHighDateTime << 32) | (uint)_c.LastWritten.dwLowDateTime;
                    _list.Add((System.Runtime.InteropServices.Marshal.PtrToStringUni(_c.TargetName), _s.Trim(), _w));
                }
                _list.Sort((a, b) => b.Item3.CompareTo(a.Item3));
            }
            catch { }
            finally
            {
                if (_ptr != IntPtr.Zero) CredFree(_ptr);
            }
            return _list;
        }

        // Fingerprint (write time + size + hash) of a Windows Credential Manager generic entry.
        // The secret itself is hashed in memory and never kept, logged or returned.
        public static string CredentialManagerSignature(string target)
        {
            IntPtr _ptr = IntPtr.Zero;
            try
            {
                if (!CredRead(target, 1, 0, out _ptr) || _ptr == IntPtr.Zero) return "missing";
                CREDENTIAL _cred = System.Runtime.InteropServices.Marshal.PtrToStructure<CREDENTIAL>(_ptr);
                long _written = ((long)_cred.LastWritten.dwHighDateTime << 32) | (uint)_cred.LastWritten.dwLowDateTime;
                string _hash = "0";
                if (_cred.CredentialBlobSize > 0 && _cred.CredentialBlob != IntPtr.Zero)
                {
                    byte[] _blob = new byte[_cred.CredentialBlobSize];
                    System.Runtime.InteropServices.Marshal.Copy(_cred.CredentialBlob, _blob, 0, _blob.Length);
                    _hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_blob), 0, 8);
                    Array.Clear(_blob, 0, _blob.Length);
                }
                return _written + "|" + _cred.CredentialBlobSize + "|" + _hash;
            }
            catch
            {
                return "error";
            }
            finally
            {
                if (_ptr != IntPtr.Zero) CredFree(_ptr);
            }
        }
    }

    public class ClaudeProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_Claude";
        public string DisplayName => "Claude Code";

        public string CustomPath { get; set; }

        public string GetExePath()
        {
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "claude.cmd", "claude.exe", "claude");
            if (custom != null) return custom;
            if (ProviderHelper.IsOnPath("claude")) return "claude";
            if (ProviderHelper.IsOnPath("claude.cmd")) return "claude.cmd";
            return null;
        }

        public async Task<bool> RunCmdAsync(string args, int timeoutSec)
        {
            var exe = GetExePath();
            if (exe == null) return false;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    // neutral folder: never let the CLI pick up a project in SideMon's directory
                    WorkingDirectory = Path.GetTempPath()
                };
                psi.Environment.Remove("CLAUDECODE");
                psi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");

                Process process = null;
                try {
                    process = Process.Start(psi);
                } catch { return false; }
                if (process == null) return false;

                try
                {
                    process.StandardInput.Close();
                    // Drain output to prevent deadlock
                    var outTask = process.StandardOutput.ReadToEndAsync();
                    var errTask = process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync(cts.Token);
                    return process.ExitCode == 0;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                finally
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                }
            }
            catch
            {
                return false;
            }
        }
        
        public long LastSeenExpiredMs = 0;
        public int AutoRenewAttempts = 0;
        public DateTimeOffset LastAutoRenewTime = DateTimeOffset.MinValue;

        // Custom path first, then CLAUDE_CONFIG_DIR (where Claude Code keeps its state when
        // it is set), then the default %USERPROFILE%\.claude. First existing file wins.
        private string FindTokenPath()
        {
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, ".credentials.json");
            if (custom != null) return custom;

            try
            {
                var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var file = Path.Combine(ProviderHelper.ResolvePath(dir.Trim().Trim('"')), ".credentials.json");
                    if (File.Exists(file)) return file;
                }
            }
            catch { }

            var def = ProviderHelper.ResolvePath(@"%USERPROFILE%\.claude\.credentials.json");
            return File.Exists(def) ? def : null;
        }

        public bool IsInstalled()
        {
            return FindTokenPath() != null;
        }

        public UsageDetection Detect()
        {
            var tokenPath = FindTokenPath();
            if (tokenPath == null) return UsageDetection.NotInstalled;
            try
            {
                var json = File.ReadAllText(tokenPath);
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) && oauth.TryGetProperty("accessToken", out var at) && !string.IsNullOrEmpty(at.GetString()))
                    {
                        return UsageDetection.Detected;
                    }
                }
            }
            catch { }
            return UsageDetection.NotLoggedIn;
        }

        private string FindConfigJsonPath()
        {
            try
            {
                var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var file = Path.Combine(ProviderHelper.ResolvePath(dir.Trim().Trim('"')), ".claude.json");
                    if (File.Exists(file)) return file;
                }
            }
            catch { }

            var def = ProviderHelper.ResolvePath(@"%USERPROFILE%\.claude.json");
            return File.Exists(def) ? def : null;
        }

        public string ReadAccount()
        {
            try
            {
                var path = FindConfigJsonPath();
                if (path == null) return null;
                var json = File.ReadAllText(path);
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty("oauthAccount", out var oauth)
                        && oauth.ValueKind == JsonValueKind.Object)
                    {
                        var email = UsageParsers.GetString(oauth, "emailAddress")?.Trim();
                        return string.IsNullOrEmpty(email) ? null : email;
                    }
                }
            }
            catch { }
            return null;
        }

        public string CredentialSignature()
        {
            return ProviderHelper.FileSignature(FindTokenPath()) + ";" + ProviderHelper.FileSignature(FindConfigJsonPath());
        }

        // when on, an expired token is renewed by running the Claude CLI hidden (it refreshes
        // its own OAuth token and rewrites .credentials.json); SideMon never writes the file
        public bool AutoRefresh { get; set; } = true;

        private static readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);

        // reads the access token; expMs = expiresAt (ms) or null. Returns null when absent.
        private string ReadToken(string tokenPath, out long? expMs)
        {
            expMs = null;
            var json = File.ReadAllText(tokenPath);
            using (var doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || !oauth.TryGetProperty("accessToken", out var at))
                {
                    return null;
                }

                if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out long _ms))
                {
                    expMs = _ms;
                }

                return at.GetString();
            }
        }

        private static bool IsExpired(long? expMs)
        {
            return expMs.HasValue && DateTimeOffset.FromUnixTimeMilliseconds(expMs.Value) <= DateTimeOffset.UtcNow.AddSeconds(30);
        }

        // Same approach as other usage monitors: let the CLI renew its token. First the
        // no-model `claude auth status`; only if the token is still stale a minimal `claude -p .`
        // (one tiny request). Limited to 3 attempts per expired token, 10 minutes apart.
        private async Task<bool> TryCliRefreshAsync(string tokenPath)
        {
            if (!AutoRefresh || AutoRenewAttempts >= 3 || DateTimeOffset.Now - LastAutoRenewTime < TimeSpan.FromMinutes(10))
            {
                return false;
            }

            if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
            {
                return false;
            }

            try
            {
                LastAutoRenewTime = DateTimeOffset.Now;
                AutoRenewAttempts++;

                await RunCmdAsync("auth status", 30).ConfigureAwait(false);
                ReadToken(tokenPath, out long? _exp1);
                if (!IsExpired(_exp1)) return true;

                await RunCmdAsync("-p \".\" --no-session-persistence", 90).ConfigureAwait(false);
                ReadToken(tokenPath, out long? _exp2);
                return !IsExpired(_exp2);
            }
            catch
            {
                return false;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public async Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            var tokenPath = FindTokenPath();
            if (tokenPath == null) return UsageResult.Fail(UsageStatus.NotInstalled);

            string token;
            try
            {
                token = ReadToken(tokenPath, out long? expMs);
                if (token == null) return UsageResult.Fail(UsageStatus.NotLoggedIn);

                if (IsExpired(expMs))
                {
                    if (expMs.Value != LastSeenExpiredMs) { LastSeenExpiredMs = expMs.Value; AutoRenewAttempts = 0; }

                    if (await TryCliRefreshAsync(tokenPath).ConfigureAwait(false))
                    {
                        token = ReadToken(tokenPath, out expMs);
                    }

                    if (token == null || IsExpired(expMs))
                    {
                        // keep the last values (dimmed) and look at the file again shortly
                        var stale = UsageResult.Fail(UsageStatus.AuthExpired);
                        stale.RetryIn = TimeSpan.FromSeconds(30);
                        return stale;
                    }
                }
            }
            catch
            {
                return UsageResult.Fail(UsageStatus.Error);
            }

            if (string.IsNullOrEmpty(token)) return UsageResult.Fail(UsageStatus.NotLoggedIn);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.TryAddWithoutValidation("User-Agent", "claude-cli/2.1.123 (external, cli)");
                req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");

                using var res = await ProviderHelper.Client.SendAsync(req, ct);
                if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return UsageResult.Fail(UsageStatus.RateLimited, res.Headers.RetryAfter?.Delta);
                }
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // rejected before its expiry (revoked/rotated): let the CLI renew it, then retry soon
                    var _rejected = UsageResult.Fail(UsageStatus.AuthExpired);
                    if (await TryCliRefreshAsync(tokenPath).ConfigureAwait(false)) _rejected.RetryIn = TimeSpan.FromSeconds(5);
                    return _rejected;
                }
                res.EnsureSuccessStatusCode();

                var resJson = await res.Content.ReadAsStringAsync();
                var windows = UsageParsers.ParseClaude(resJson);

                string plan = null;
                var tp = FindTokenPath();
                if (tp != null)
                {
                    try
                    {
                        var credJson = System.IO.File.ReadAllText(tp);
                        using (var doc = System.Text.Json.JsonDocument.Parse(credJson))
                        {
                            // the plan lives inside the claudeAiOauth object (a plain label like "pro")
                            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauthProp)
                                && oauthProp.ValueKind == System.Text.Json.JsonValueKind.Object
                                && oauthProp.TryGetProperty("subscriptionType", out var subProp)
                                && subProp.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                var p = subProp.GetString();
                                if (!string.IsNullOrEmpty(p)) plan = char.ToUpper(p[0]) + p.Substring(1);
                            }
                        }
                    }
                    catch { }
                }

                return UsageResult.Ok(windows, plan);
            }
            catch
            {
                return UsageResult.Fail(UsageStatus.Error);
            }
        }
    }

    public class AgyProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_Agy";

        private const int MaxLogReadBytes = 2 * 1024 * 1024;
        private static readonly Regex EmailAfterKeyRegex = new Regex(
            @"(?i)\bemail(?:_?address)?[""'\\]*\s*[:=]\s*[""'\\]*([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));

        // agy (Antigravity) keeps its login in Windows Credential Manager
        public string CredentialSignature()
        {
            return ProviderHelper.CredentialManagerSignature("gemini:antigravity");
        }

        private static string FindLatestLogPath()
        {
            try
            {
                string logDir = ProviderHelper.ResolvePath(@"%USERPROFILE%\.gemini\antigravity-cli\log");
                if (!Directory.Exists(logDir)) return null;

                FileInfo best = null;
                foreach (string file in Directory.EnumerateFiles(logDir, "cli-*.log"))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Exists && (best == null || info.LastWriteTimeUtc > best.LastWriteTimeUtc))
                        {
                            best = info;
                        }
                    }
                    catch { }
                }

                string cliLog = Path.Combine(logDir, "cli.log");
                if (File.Exists(cliLog))
                {
                    try
                    {
                        var linkInfo = new FileInfo(cliLog);
                        FileSystemInfo resolved = linkInfo.ResolveLinkTarget(true);
                        FileInfo targetInfo = resolved as FileInfo ?? linkInfo;
                        if (targetInfo.Exists && (best == null || targetInfo.LastWriteTimeUtc > best.LastWriteTimeUtc))
                        {
                            best = targetInfo;
                        }
                    }
                    catch { }
                }

                return best?.FullName;
            }
            catch
            {
                return null;
            }
        }

        public string ReadAccount()
        {
            try
            {
                string path = FindLatestLogPath();
                if (string.IsNullOrEmpty(path)) return null;

                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long length = fs.Length;
                    int toRead = (int)Math.Min(length, MaxLogReadBytes);
                    if (toRead <= 0) return null;
                    if (length > MaxLogReadBytes)
                    {
                        fs.Seek(-MaxLogReadBytes, SeekOrigin.End);
                    }
                    byte[] buffer = new byte[toRead];
                    int totalRead = 0;
                    while (totalRead < toRead)
                    {
                        int n = fs.Read(buffer, totalRead, toRead - totalRead);
                        if (n <= 0) break;
                        totalRead += n;
                    }
                    text = Encoding.UTF8.GetString(buffer, 0, totalRead);
                }

                string lastEmail = null;
                foreach (Match m in EmailAfterKeyRegex.Matches(text))
                {
                    if (m.Success && m.Groups.Count > 1)
                    {
                        string val = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(val)) lastEmail = val;
                    }
                }
                return lastEmail;
            }
            catch
            {
                return null;
            }
        }
        public string DisplayName => "Gemini (agy)";

        public string CustomPath { get; set; }

        private string GetExePath()
        {
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "agy.exe", "agy.cmd");
            if (custom != null) return custom;
            var localPath = ProviderHelper.ResolvePath(@"%LOCALAPPDATA%\agy\bin\agy.exe");
            if (File.Exists(localPath)) return localPath;
            if (ProviderHelper.IsOnPath("agy")) return "agy";
            return null;
        }

        public bool IsInstalled()
        {
            return GetExePath() != null;
        }

        public UsageDetection Detect()
        {
            return IsInstalled() ? UsageDetection.Detected : UsageDetection.NotInstalled;
        }

        public async Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            var exe = GetExePath();
            if (exe == null) return UsageResult.Fail(UsageStatus.NotInstalled);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-p \"/usage\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                Process process;
                try {
                    process = Process.Start(psi);
                } catch { return UsageResult.Fail(UsageStatus.Error); }
                if (process == null) return UsageResult.Fail(UsageStatus.Error);

                string output;
                try
                {
                    process.StandardInput.Close();
                    var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
                    var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                    await process.WaitForExitAsync(cts.Token);
                    output = await outputTask;
                    await errorTask;
                }
                catch (OperationCanceledException)
                {
                    return UsageResult.Fail(UsageStatus.Error);
                }
                finally
                {
                    // Dispose() only drops our handle; never leave agy running behind us
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                }

                var windows = UsageParsers.ParseAgy(output);
                return UsageResult.Ok(windows);
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
    }

    public class CodexProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_Codex";
        public string DisplayName => "Codex CLI";

        public string CustomPath { get; set; }

        private List<string> AuthPaths()
        {
            var paths = new List<string>();
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "auth.json");
            if (custom != null) paths.Add(custom);
            paths.Add(ProviderHelper.ResolvePath(@"%CODEX_HOME%\auth.json"));
            paths.Add(ProviderHelper.ResolvePath(@"%USERPROFILE%\.codex\auth.json"));
            return paths;
        }

        private (string token, string id) GetCreds()
        {
            foreach (var path in AuthPaths())
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("tokens", out var t))
                        {
                            var token = UsageParsers.GetString(t, "access_token");
                            var id = UsageParsers.GetString(t, "account_id");
                            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(id))
                                return (token, id);
                        }
                    }
                    catch { }
                }
            }
            return (null, null);
        }

        public string ReadAccount()
        {
            foreach (var path in AuthPaths())
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("tokens", out var t) && t.ValueKind == JsonValueKind.Object)
                        {
                            string idToken = UsageParsers.GetString(t, "id_token");
                            if (string.IsNullOrEmpty(idToken)) continue;
                            using var payload = ExtraHelper.JwtPayload(idToken);
                            if (payload != null)
                            {
                                string email = UsageParsers.GetString(payload.RootElement, "email")?.Trim();
                                if (!string.IsNullOrEmpty(email)) return email;
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        public string CredentialSignature()
        {
            foreach (var path in AuthPaths()) if (File.Exists(path)) return ProviderHelper.FileSignature(path);
            return "missing";
        }

        private bool CredsFileExists()
        {
            foreach (var path in AuthPaths()) if (File.Exists(path)) return true;
            return false;
        }

        public bool IsInstalled()
        {
            return CredsFileExists();
        }

        public UsageDetection Detect()
        {
            if (!IsInstalled()) return UsageDetection.NotInstalled;
            return GetCreds().token != null ? UsageDetection.Detected : UsageDetection.NotLoggedIn;
        }

        public async Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            var creds = GetCreds();
            if (creds.token == null)
            {
                return IsInstalled() ? UsageResult.Fail(UsageStatus.NotLoggedIn) : UsageResult.Fail(UsageStatus.NotInstalled);
            }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.token);
                req.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", creds.id);
                req.Headers.TryAddWithoutValidation("OpenAI-Beta", "codex-1");
                req.Headers.TryAddWithoutValidation("originator", "codex_cli_rs");
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var res = await ProviderHelper.Client.SendAsync(req, ct);
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return UsageResult.Fail(UsageStatus.AuthExpired);
                }
                if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return UsageResult.Fail(UsageStatus.RateLimited, res.Headers.RetryAfter?.Delta);
                }
                res.EnsureSuccessStatusCode();

                var json = await res.Content.ReadAsStringAsync();
                List<UsageWindow> windows;
                string plan;
                UsageParsers.ParseCodex(json, DateTimeOffset.UtcNow, out windows, out plan);
                return UsageResult.Ok(windows, plan);
            }
            catch
            {
                return UsageResult.Fail(UsageStatus.Error);
            }
        }
    }

    public class CopilotProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_Copilot";
        public string DisplayName => "GitHub Copilot";

        private static readonly TimeSpan GhTimeout = TimeSpan.FromSeconds(10);

        // result of the last background poll's token lookup.
        // Detect() runs on the Settings UI thread and must stay purely local (files and
        // PATH), so `gh auth token` is only ever started from FetchAsync.
        private volatile int _tokenState; // 0 = not tried yet, 1 = token found, 2 = none

        private volatile string _customPath;

        public string CustomPath
        {
            get { return _customPath; }
            set { _customPath = value; }
        }

        // hosts.json / apps.json the user pointed at (a file, or a folder holding them),
        // followed by the default github-copilot ones
        private List<string> ConfigFiles()
        {
            var files = new List<string>();
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "apps.json", "hosts.json");
            if (custom != null) files.Add(custom);
            files.Add(ProviderHelper.ResolvePath(@"%LOCALAPPDATA%\github-copilot\apps.json"));
            files.Add(ProviderHelper.ResolvePath(@"%LOCALAPPDATA%\github-copilot\hosts.json"));
            return files;
        }

        private bool HasConfigFile()
        {
            foreach (var path in ConfigFiles()) if (File.Exists(path)) return true;
            return false;
        }

        private string ReadFileToken()
        {
            foreach (var path in ConfigFiles())
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("oauth_token", out var t))
                            {
                                var token = t.GetString();
                                if (!string.IsNullOrEmpty(token)) return token;
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        // `gh auth token`: local only, hidden, stdin/stderr closed, killed after GhTimeout
        private static string RunGhAuthToken()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                Process proc;
                try {
                    proc = Process.Start(psi);
                } catch { return null; }
                if (proc == null) return null;

                try { proc.StandardInput.Close(); } catch { }
                var errTask = proc.StandardError.ReadToEndAsync();
                var outTask = proc.StandardOutput.ReadToEndAsync();

                if (!proc.WaitForExit((int)GhTimeout.TotalMilliseconds))
                {
                    try { proc.Kill(true); } catch { }
                    return null;
                }

                if (!outTask.Wait(2000)) return null;
                var output = outTask.Result;
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    return output.Trim();
            }
            catch { }
            return null;
        }

        // never blocks longer than GhTimeout (+ a moment to drain the pipe)
        private static readonly string[] TokenEnv = { "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN" };

        // Copilot CLI ("copilot" login) keeps its GitHub token in Windows Credential Manager
        private static string ReadCopilotCliToken()
        {
            foreach (string _filter in new[] { "https://github.com:*.copilot-cli", "copilot-cli/https://github.com:*" })
            {
                foreach (var _e in ProviderHelper.CredentialManagerEntries(_filter))
                {
                    if (!string.IsNullOrEmpty(_e.Secret) && _e.Secret.IndexOf('\n') < 0) return _e.Secret;
                }
            }
            return null;
        }

        private readonly object _loginCacheLock = new object();
        private string _cachedLoginSig;
        private string _cachedLogin;
        private bool _hasCachedLogin;

        private static string HostsYmlPath()
        {
            return ProviderHelper.ResolvePath(@"%APPDATA%\GitHub CLI\hosts.yml");
        }

        public static string ReadHostsYmlUser()
        {
            try
            {
                string path = HostsYmlPath();
                if (!File.Exists(path)) return null;

                bool inGithub = false;
                foreach (string rawLine in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string trimmedStart = rawLine.TrimStart(' ', '\t');
                    if (trimmedStart.StartsWith("#", StringComparison.Ordinal)) continue;

                    int indent = rawLine.Length - trimmedStart.Length;
                    if (indent == 0)
                    {
                        string key = trimmedStart.TrimEnd();
                        inGithub = key.StartsWith("github.com:", StringComparison.OrdinalIgnoreCase)
                            || key.StartsWith("\"github.com\":", StringComparison.OrdinalIgnoreCase)
                            || key.StartsWith("'github.com':", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (inGithub && trimmedStart.StartsWith("user:", StringComparison.Ordinal))
                    {
                        string val = trimmedStart.Substring(5);
                        int hash = val.IndexOf('#');
                        if (hash >= 0) val = val.Substring(0, hash);
                        val = val.Trim().Trim('"', '\'').Trim();
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
            }
            catch { }
            return null;
        }

        public string ReadAccount()
        {
            try
            {
                string sig = CredentialSignature();
                lock (_loginCacheLock)
                {
                    if (_hasCachedLogin && string.Equals(_cachedLoginSig, sig, StringComparison.Ordinal))
                    {
                        return _cachedLogin;
                    }
                }
                return ReadHostsYmlUser();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetOrFetchLoginAsync(string token, string sig, CancellationToken ct)
        {
            lock (_loginCacheLock)
            {
                if (_hasCachedLogin && string.Equals(_cachedLoginSig, sig, StringComparison.Ordinal))
                {
                    return _cachedLogin;
                }
            }

            string login = null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                req.Headers.Authorization = new AuthenticationHeaderValue("token", token);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.96.2");
                req.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.26.7");
                req.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/0.26.7");
                req.Headers.TryAddWithoutValidation("X-Github-Api-Version", "2025-04-01");

                using var res = await ProviderHelper.Client.SendAsync(req, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    login = UsageParsers.GetString(doc.RootElement, "login")?.Trim();
                    if (string.IsNullOrEmpty(login)) login = null;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(login))
            {
                login = ReadHostsYmlUser();
            }

            lock (_loginCacheLock)
            {
                _cachedLoginSig = sig;
                _cachedLogin = login;
                _hasCachedLogin = true;
            }

            return login;
        }

        public string CredentialSignature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var _e in ProviderHelper.CredentialManagerEntries("https://github.com:*.copilot-cli")) sb.Append(_e.Target).Append(_e.Written).Append(';');
            foreach (var _e in ProviderHelper.CredentialManagerEntries("gh:github.com*")) sb.Append(_e.Target).Append(_e.Written).Append(';');
            foreach (var _f in ConfigFiles()) sb.Append(ProviderHelper.FileSignature(_f)).Append(';');
            sb.Append(ProviderHelper.FileSignature(HostsYmlPath())).Append(';');
            foreach (string _name in TokenEnv)
            {
                string _env = Environment.GetEnvironmentVariable(_name);
                if (!string.IsNullOrWhiteSpace(_env))
                {
                    byte[] _bytes = Encoding.UTF8.GetBytes(_env.Trim());
                    sb.Append(_name).Append('=').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_bytes), 0, 8)).Append(';');
                    Array.Clear(_bytes, 0, _bytes.Length);
                    break;
                }
            }
            return sb.ToString();
        }

        private string ReadToken()
        {
            foreach (string _name in TokenEnv)
            {
                string _env = Environment.GetEnvironmentVariable(_name);
                if (!string.IsNullOrWhiteSpace(_env)) return _env.Trim();
            }
            var token = ReadFileToken();
            if (!string.IsNullOrEmpty(token)) return token;
            token = ReadCopilotCliToken();
            if (!string.IsNullOrEmpty(token)) return token;
            if (ProviderHelper.IsOnPath("gh")) return RunGhAuthToken();
            return null;
        }

        public bool IsInstalled()
        {
            return ProviderHelper.IsOnPath("gh") || ProviderHelper.IsOnPath("copilot") || HasConfigFile()
                || ProviderHelper.CredentialManagerEntries("https://github.com:*.copilot-cli").Count > 0;
        }

        public UsageDetection Detect()
        {
            try
            {
                if (!IsInstalled()) return UsageDetection.NotInstalled;

                if (!string.IsNullOrEmpty(ReadFileToken())) return UsageDetection.Detected;

                // only `gh` is left: whether it is signed in is learned by the first poll
                return _tokenState == 2 ? UsageDetection.NotLoggedIn : UsageDetection.Detected;
            }
            catch
            {
                return UsageDetection.NotLoggedIn;
            }
        }

        public async Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            if (!IsInstalled()) return UsageResult.Fail(UsageStatus.NotInstalled);

            // FetchAsync already runs on a pool thread (UsageRegistry.PollAsync)
            var token = await Task.Run(() => ReadToken(), ct);
            _tokenState = string.IsNullOrEmpty(token) ? 2 : 1;
            if (string.IsNullOrEmpty(token)) return UsageResult.Fail(UsageStatus.NotLoggedIn);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/user");
                req.Headers.Authorization = new AuthenticationHeaderValue("token", token);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.96.2");
                req.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.26.7");
                req.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/0.26.7");
                req.Headers.TryAddWithoutValidation("X-Github-Api-Version", "2025-04-01");

                using var res = await ProviderHelper.Client.SendAsync(req, ct);
                if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return UsageResult.Fail(UsageStatus.AuthExpired);
                }
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    string _noPlanLogin = await GetOrFetchLoginAsync(token, CredentialSignature(), ct).ConfigureAwait(false);
                    return UsageResult.Fail(UsageStatus.NoPlan, null, _noPlanLogin);
                }
                if (res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    return UsageResult.Fail(UsageStatus.RateLimited, res.Headers.RetryAfter?.Delta);
                }
                res.EnsureSuccessStatusCode();

                var json = await res.Content.ReadAsStringAsync();
                var windows = UsageParsers.ParseCopilot(json);
                var login = await GetOrFetchLoginAsync(token, CredentialSignature(), ct).ConfigureAwait(false);

                // signed in, but the account has no Copilot plan that reports a quota
                if (windows.Count == 0) return UsageResult.Fail(UsageStatus.NoPlan, null, login);

                return UsageResult.Ok(windows, null, login);
            }
            catch
            {
                return UsageResult.Fail(UsageStatus.Error);
            }
        }
    }
    public class GeminiProvider : IUsageProvider, ICredentialWatch, IAccountReader
    {
        public string Id => "AIUsage_Gemini";
        public string DisplayName => "Gemini CLI";

        public string CustomPath { get; set; }

        private string AccountsPath()
        {
            var custom = ProviderHelper.ResolveCustomFile(CustomPath, "google_accounts.json");
            if (custom != null) return custom;
            return ProviderHelper.ResolvePath(@"%USERPROFILE%\.gemini\google_accounts.json");
        }

        public string ReadAccount()
        {
            try
            {
                string path = AccountsPath();
                if (!File.Exists(path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                string active = UsageParsers.GetString(doc.RootElement, "active")?.Trim();
                return string.IsNullOrEmpty(active) ? null : active;
            }
            catch
            {
                return null;
            }
        }

        public string CredentialSignature()
        {
            string _path = ProviderHelper.ResolveCustomFile(CustomPath, "oauth_creds.json") ?? ProviderHelper.ResolvePath(@"%USERPROFILE%\.gemini\oauth_creds.json");
            return ProviderHelper.FileSignature(_path) + ";" + ProviderHelper.FileSignature(AccountsPath());
        }

        public bool IsInstalled()
        {
            var credsExist = ProviderHelper.ResolveCustomFile(CustomPath, "oauth_creds.json") != null
                || File.Exists(ProviderHelper.ResolvePath(@"%USERPROFILE%\.gemini\oauth_creds.json"));
            return credsExist && ProviderHelper.IsOnPath("gemini");
        }

        public UsageDetection Detect()
        {
            return IsInstalled() ? UsageDetection.DetectedNoUsageApi : UsageDetection.NotInstalled;
        }

        public Task<UsageResult> FetchAsync(CancellationToken ct)
        {
            return Task.FromResult(UsageResult.Fail(UsageStatus.NoUsageApi));
        }
    }

    public interface ILoginLauncher
    {
        ProcessStartInfo BuildProcessStartInfo(string providerId);
        Process Launch(string providerId);
    }

    public class DefaultLoginLauncher : ILoginLauncher
    {
        public static Func<string, ProcessStartInfo> CustomBuilder { get; set; }
        public static Func<ProcessStartInfo, Process> ProcessStarter { get; set; } = psi => Process.Start(psi);

        public static bool IsValidExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string resolved = ProviderHelper.ResolvePath(path.Trim().Trim('"'));
                if (!File.Exists(resolved)) return false;

                // the path ends up inside a `cmd /k` / Windows Terminal command line of an
                // elevated process: refuse anything cmd or wt would interpret as syntax
                if (resolved.IndexOfAny(new[] { '&', '|', '<', '>', '^', '%', '"', ';', '!', '`', '\r', '\n' }) >= 0) return false;

                string ext = Path.GetExtension(resolved);
                return string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveExe(string customPath, string defaultExe, params string[] fileNames)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                if (IsValidExecutable(customPath))
                {
                    return ProviderHelper.ResolvePath(customPath.Trim().Trim('"'));
                }
                var customFile = ProviderHelper.ResolveCustomFile(customPath, fileNames);
                if (IsValidExecutable(customFile)) return customFile;
            }

            if (ProviderHelper.IsOnPath(defaultExe)) return defaultExe;
            if (ProviderHelper.IsOnPath(defaultExe + ".cmd")) return defaultExe + ".cmd";
            if (ProviderHelper.IsOnPath(defaultExe + ".exe")) return defaultExe + ".exe";
            return defaultExe;
        }

        public ProcessStartInfo BuildProcessStartInfo(string providerId)
        {
            if (CustomBuilder != null)
            {
                var custom = CustomBuilder(providerId);
                if (custom != null) return custom;
            }

            var provider = UsageRegistry.Find(providerId);
            if (provider == null) return null;

            string exe = null;
            string cliArgs = null;
            string title = null;
            string banner = null;
            var env = new Dictionary<string, string>();

            switch (providerId)
            {
                case "AIUsage_Claude":
                    {
                        var claude = provider as ClaudeProvider;
                        string cand = claude != null ? claude.GetExePath() : null;
                        if (IsValidExecutable(cand))
                        {
                            exe = cand;
                        }
                        else
                        {
                            exe = ResolveExe(provider.CustomPath, "claude", "claude.cmd", "claude.exe");
                        }
                        cliArgs = "auth login";
                        title = "SideMon - Claude sign-in";

                        var claudeConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                        if (!string.IsNullOrWhiteSpace(claudeConfigDir))
                        {
                            env["CLAUDE_CONFIG_DIR"] = claudeConfigDir;
                        }
                    }
                    break;

                case "AIUsage_Agy":
                    {
                        if (IsValidExecutable(provider.CustomPath))
                        {
                            exe = ProviderHelper.ResolvePath(provider.CustomPath.Trim().Trim('"'));
                        }
                        else
                        {
                            var customAgy = ProviderHelper.ResolveCustomFile(provider.CustomPath, "agy.exe", "agy.cmd");
                            if (IsValidExecutable(customAgy))
                            {
                                exe = customAgy;
                            }
                            else
                            {
                                string localAgy = ProviderHelper.ResolvePath(@"%LOCALAPPDATA%\agy\bin\agy.exe");
                                if (File.Exists(localAgy)) exe = localAgy;
                                else exe = ResolveExe(null, "agy", "agy.exe", "agy.cmd");
                            }
                        }
                        cliArgs = string.Empty;
                        title = "SideMon - agy sign-in";
                    }
                    break;

                case "AIUsage_Gemini":
                    {
                        if (IsValidExecutable(provider.CustomPath))
                        {
                            exe = ProviderHelper.ResolvePath(provider.CustomPath.Trim().Trim('"'));
                        }
                        else
                        {
                            var customGemini = ProviderHelper.ResolveCustomFile(provider.CustomPath, "gemini.exe", "gemini.cmd");
                            if (IsValidExecutable(customGemini))
                            {
                                exe = customGemini;
                            }
                            else
                            {
                                exe = ResolveExe(null, "gemini", "gemini.exe", "gemini.cmd");
                            }
                        }
                        cliArgs = string.Empty;
                        title = "SideMon - Gemini sign-in";
                        banner = "Please choose 'Login with Google' in Gemini CLI";
                    }
                    break;

                case "AIUsage_Codex":
                    {
                        if (IsValidExecutable(provider.CustomPath))
                        {
                            exe = ProviderHelper.ResolvePath(provider.CustomPath.Trim().Trim('"'));
                        }
                        else
                        {
                            exe = ResolveExe(null, "codex", "codex.exe", "codex.cmd");
                        }
                        cliArgs = "login";
                        title = "SideMon - Codex sign-in";
                    }
                    break;

                case "AIUsage_Copilot":
                    {
                        if (IsValidExecutable(provider.CustomPath))
                        {
                            exe = ProviderHelper.ResolvePath(provider.CustomPath.Trim().Trim('"'));
                        }
                        else
                        {
                            exe = ResolveExe(null, "gh", "gh.exe", "gh.cmd");
                        }
                        cliArgs = "auth login";
                        title = "SideMon - GitHub Copilot sign-in";
                    }
                    break;

                case "AIUsage_Grok":
                    {
                        if (IsValidExecutable(provider.CustomPath))
                        {
                            exe = ProviderHelper.ResolvePath(provider.CustomPath.Trim().Trim('"'));
                        }
                        else
                        {
                            exe = ResolveExe(null, "grok", "grok.exe", "grok.cmd");
                        }
                        cliArgs = "login";
                        title = "SideMon - Grok sign-in";
                    }
                    break;

                default:
                    return null;
            }

            if (string.IsNullOrWhiteSpace(exe)) return null;

            string target = exe.Contains(" ") ? $"\"{exe}\"" : exe;
            string cliFull = string.IsNullOrWhiteSpace(cliArgs) ? target : $"{target} {cliArgs}";
            string cmdBody = string.IsNullOrEmpty(banner) ? cliFull : $"echo {banner} & echo. & {cliFull}";

            string wtPath = FindWindowsTerminal();
            var psi = new ProcessStartInfo();
            psi.UseShellExecute = false;

            foreach (var kvp in env)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }

            if (!string.IsNullOrEmpty(wtPath))
            {
                psi.FileName = wtPath;
                psi.Arguments = $"--title \"{title}\" cmd.exe /k \"{cmdBody}\"";
            }
            else
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/k \"title {title} & {cmdBody}\"";
            }

            return psi;
        }

        public Process Launch(string providerId)
        {
            var psi = BuildProcessStartInfo(providerId);
            if (psi == null) return null;
            try
            {
                return ProcessStarter != null ? ProcessStarter(psi) : Process.Start(psi);
            }
            catch
            {
                return null;
            }
        }

        private static string FindWindowsTerminal()
        {
            if (ProviderHelper.IsOnPath("wt")) return "wt.exe";
            string wtApp = ProviderHelper.ResolvePath(@"%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe");
            if (File.Exists(wtApp)) return wtApp;
            return null;
        }
    }
}
