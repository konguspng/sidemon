import re

with open('SidebarDiagnostics/Settings.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('global::SidebarDiagnostics.Properties.Resources', 'Framework.Resources')

with open('SidebarDiagnostics/Settings.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
