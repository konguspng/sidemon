with open('SidebarDiagnostics/Sidebar.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

target_init = '''        public Sidebar(bool openSettings, bool initiallyHidden)
        {'''

replacement_init = '''        public Sidebar(bool openSettings, bool initiallyHidden)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;'''

if target_init in content:
    content = content.replace(target_init, replacement_init)
else:
    print('Target init not found')

target_uninit = '''        protected override void OnClosed(EventArgs e)
        {'''

replacement_uninit = '''        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;'''

if target_uninit in content:
    content = content.replace(target_uninit, replacement_uninit)
else:
    print('Target uninit not found')

target_methods = '''        private bool _pausedForFullscreen = false;'''

replacement_methods = '''        private bool _pausedForFullscreen = false;
        private bool _pausedForSession = false;

        private void SystemEvents_SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
            {
                _pausedForSession = true;
                Model?.Pause();
            }
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            {
                _pausedForSession = false;
                if (Visibility == Visibility.Visible && !_pausedForFullscreen)
                {
                    Model?.Resume();
                }
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
            {
                _pausedForSession = true;
                Model?.Pause();
            }
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                _pausedForSession = false;
                if (Visibility == Visibility.Visible && !_pausedForFullscreen)
                {
                    Model?.Resume();
                }
            }
        }'''

if target_methods in content:
    content = content.replace(target_methods, replacement_methods)
else:
    print('Target methods not found')

with open('SidebarDiagnostics/Sidebar.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print('Patched Sidebar.xaml.cs')
