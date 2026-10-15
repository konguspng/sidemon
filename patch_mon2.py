import re

with open('SidebarDiagnostics/UsageMonitor.cs', 'r', encoding='utf-8') as f:
    content = f.read()

find_str = '''            if (_statusRow)
            {
                _currentRows[0].SetNote(_snapshot.HasAttempt ? UsageRegistry.StatusToText(_snapshot.Status) : Resources.UsageLoading);

                return;
            }'''

replace_str = '''            if (_snapshot.Status == UsageStatus.AuthExpired && _provider.Id == "AIUsage_Claude" && _hardwareConfig != null && _hardwareConfig.AutoRenew)
            {
                var claude = (Providers.ClaudeProvider)_provider;
                if (claude.AutoRenewAttempts < 3 && (DateTimeOffset.Now - claude.LastAutoRenewTime).TotalMinutes >= 30)
                {
                    claude.LastAutoRenewTime = DateTimeOffset.Now;
                    claude.AutoRenewAttempts++;
                    Task.Run(async () => {
                        await claude.RunCmdAsync("auth status", 30);
                        UsageRegistry.SetCustomPath(claude.Id, claude.CustomPath); // trigger immediate poll logic
                        UsageRegistry.PollIfDue(claude);
                    });
                }
            }

            if (_statusRow)
            {
                _currentRows[0].SetNote(_snapshot.HasAttempt ? UsageRegistry.StatusToText(_snapshot.Status) : Resources.UsageLoading);

                return;
            }'''

content = content.replace(find_str, replace_str)

with open('SidebarDiagnostics/UsageMonitor.cs', 'w', encoding='utf-8') as f:
    f.write(content)
