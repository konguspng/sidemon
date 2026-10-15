import re

with open('SidebarDiagnostics/UsageMonitor.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('private bool? _showExtra;', 'private bool? _showExtra;\\n        private HardwareConfig _hardwareConfig;')
content = content.replace('public UsageMonitor(IUsageProvider provider, string name, bool? showExtra, MetricConfig[] metrics, bool showName, bool roundAll, double alertValue) : base(provider.Id, name, showName)', 'public UsageMonitor(IUsageProvider provider, HardwareConfig config, string name, bool? showExtra, MetricConfig[] metrics, bool showName, bool roundAll, double alertValue) : base(provider.Id, name, showName)')
content = content.replace('_originalName = name;', '_originalName = name;\\n            _hardwareConfig = config;')
content = content.replace('_list.Add(new UsageMonitor(_item.provider, _name, _item.c.ShowExtra, metrics, _showName, _roundAll, _alert));', '_list.Add(new UsageMonitor(_item.provider, _item.c, _name, _item.c.ShowExtra, metrics, _showName, _roundAll, _alert));')

with open('SidebarDiagnostics/UsageMonitor.cs', 'w', encoding='utf-8') as f:
    f.write(content)
