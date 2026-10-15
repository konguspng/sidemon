import re

with open('SidebarDiagnostics/Settings.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('Properties.Resources', 'global::SidebarDiagnostics.Properties.Resources')

with open('SidebarDiagnostics/Settings.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

