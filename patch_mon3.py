import re

with open('SidebarDiagnostics/UsageMonitor.cs', 'r', encoding='utf-8') as f:
    content = f.read()

find_str = '''                        if (_snapshot.IsStale)
                        {
                            string ageText = FormatAge(_snapshot.LastGoodAt);
                            string staleText = string.Format(Resources.SettingsUsageStaleNote, ageText);
                            _suffix = string.IsNullOrEmpty(_suffix) ? staleText : string.Format("{0} ({1})", _suffix, staleText);
                        }'''

replace_str = '''                        if (_snapshot.IsStale)
                        {
                            string ageText = FormatAge(_snapshot.LastGoodAt);
                            if (_snapshot.Status == UsageStatus.AuthExpired && _provider.Id == "AIUsage_Claude")
                            {
                                _suffix = string.Format(Resources.UsageClaudeExpiredStaleNote, ageText);
                            }
                            else
                            {
                                string staleText = string.Format(Resources.SettingsUsageStaleNote, ageText);
                                _suffix = string.IsNullOrEmpty(_suffix) ? staleText : string.Format("{0} ({1})", _suffix, staleText);
                            }
                        }'''

content = content.replace(find_str, replace_str)

with open('SidebarDiagnostics/UsageMonitor.cs', 'w', encoding='utf-8') as f:
    f.write(content)
