import re

with open('SidebarDiagnostics/UsageMonitor.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(r'\n        private HardwareConfig _hardwareConfig;', '\n        private HardwareConfig _hardwareConfig;')
content = content.replace(r'\n            _hardwareConfig = config;', '\n            _hardwareConfig = config;')

with open('SidebarDiagnostics/UsageMonitor.cs', 'w', encoding='utf-8') as f:
    f.write(content)

with open('SidebarDiagnostics/Settings.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(r'\n        private void NumberBox_PreviewTextInput', '\n        private void NumberBox_PreviewTextInput')

with open('SidebarDiagnostics/Settings.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
