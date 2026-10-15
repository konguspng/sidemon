with open('SidebarDiagnostics/SidebarModel.cs', 'r', encoding='utf-8') as f:
    content = f.read()

target = '''        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            UpdateClock();
        }'''

replacement = '''        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            UpdateClock();
            
            if (_clockTimer != null)
            {
                if (!Framework.Settings.Instance.ShowClock)
                {
                    if (Framework.Settings.Instance.DateSetting == Framework.DateSetting.Disabled)
                    {
                        _clockTimer.Stop();
                    }
                    else
                    {
                        var now = DateTime.Now;
                        var nextMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
                        _clockTimer.Interval = nextMinute - now;
                    }
                }
                else
                {
                    _clockTimer.Interval = TimeSpan.FromSeconds(1);
                }
            }
        }'''

if target in content:
    content = content.replace(target, replacement)
    with open('SidebarDiagnostics/SidebarModel.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print('Patched clock timer')
else:
    print('Target not found')
