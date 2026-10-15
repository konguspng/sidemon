with open('SidebarDiagnostics/Sidebar.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

target = '''        private void Window_Closed(object sender, EventArgs e)
        {'''

replacement = '''        private void Window_Closed(object sender, EventArgs e)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;'''

if target in content:
    content = content.replace(target, replacement)
    with open('SidebarDiagnostics/Sidebar.xaml.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print('Patched Window_Closed')
else:
    print('Target not found')
