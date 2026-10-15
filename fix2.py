import re

with open('SidebarDiagnostics/Settings.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('Resources.SettingsUsageClaudeRenewed', 'Properties.Resources.SettingsUsageClaudeRenewed')
content = content.replace('Resources.SettingsUsageClaudeStillExpired', 'Properties.Resources.SettingsUsageClaudeStillExpired')
content = content.replace('Resources.SettingsUsageClaudeRenewConfirm', 'Properties.Resources.SettingsUsageClaudeRenewConfirm')

with open('SidebarDiagnostics/Settings.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

with open('SidebarDiagnostics/UsageMonitor.cs', 'r', encoding='utf-8') as f:
    content = f.read()
    
content = content.replace('Task.Run(async () => {', 'System.Threading.Tasks.Task.Run(async () => {')

with open('SidebarDiagnostics/UsageMonitor.cs', 'w', encoding='utf-8') as f:
    f.write(content)

