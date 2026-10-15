import re

with open('SidebarDiagnostics/UsageProviders.cs', 'r', encoding='utf-8') as f:
    content = f.read()

get_exe_path = '''
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
'''

content = content.replace('        public string CustomPath { get; set; }', '        public string CustomPath { get; set; }\n' + get_exe_path)

with open('SidebarDiagnostics/UsageProviders.cs', 'w', encoding='utf-8') as f:
    f.write(content)
