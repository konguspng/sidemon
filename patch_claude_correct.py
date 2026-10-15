import re

with open('SidebarDiagnostics/UsageProviders.cs', 'r', encoding='utf-8') as f:
    content = f.read()

find_str = '''        public string CustomPath { get; set; }

        // Custom path first, then CLAUDE_CONFIG_DIR'''

replace_str = '''        public string CustomPath { get; set; }

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
                    CreateNoWindow = true
                };

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

        // Custom path first, then CLAUDE_CONFIG_DIR'''

content = content.replace(find_str, replace_str)

# also fix FetchAsync
find_str_fetch = '''
                        if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out long expMs)
                            && DateTimeOffset.FromUnixTimeMilliseconds(expMs) <= DateTimeOffset.UtcNow.AddSeconds(30))
                        {
                            var stale = UsageResult.Fail(UsageStatus.AuthExpired);
                            stale.RetryIn = TimeSpan.FromSeconds(30);
                            return stale;
                        }'''

replace_str_fetch = '''
                        if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out long expMs)
                            && DateTimeOffset.FromUnixTimeMilliseconds(expMs) <= DateTimeOffset.UtcNow.AddSeconds(30))
                        {
                            if (expMs != LastSeenExpiredMs) { LastSeenExpiredMs = expMs; AutoRenewAttempts = 0; }
                            var stale = UsageResult.Fail(UsageStatus.AuthExpired);
                            stale.RetryIn = TimeSpan.FromSeconds(30);
                            return stale;
                        }'''

content = content.replace(find_str_fetch, replace_str_fetch)

with open('SidebarDiagnostics/UsageProviders.cs', 'w', encoding='utf-8') as f:
    f.write(content)
