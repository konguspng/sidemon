import re

with open('SidebarDiagnostics/Settings.xaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

handler = '''
        private async void HardwareClaudeRenew_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfig _hardware = (sender as FrameworkElement).DataContext as HardwareConfig;
            if (_hardware == null || _hardware.ID != "AIUsage_Claude") return;

            var claude = UsageRegistry.Providers.OfType<SidebarDiagnostics.Monitoring.Providers.ClaudeProvider>().FirstOrDefault();
            if (claude == null) return;
            
            var btn = sender as Button;
            btn.IsEnabled = false;
            _hardware.RenewStatus = "Renewing...";

            bool step1Ok = await claude.RunCmdAsync("auth status", 30);
            
            UsageRegistry.SetCustomPath(claude.Id, _hardware.CustomPath);
            await claude.FetchAsync(System.Threading.CancellationToken.None);
            UsageRegistry.PollIfDue(claude);
            
            var snapshot = UsageRegistry.GetSnapshot(claude.Id);
            if (snapshot.Status != UsageStatus.AuthExpired)
            {
                _hardware.RenewStatus = Resources.SettingsUsageClaudeRenewed;
                btn.IsEnabled = true;
                return;
            }

            _hardware.RenewStatus = Resources.SettingsUsageClaudeStillExpired;
            var result = MessageBox.Show(this, Resources.SettingsUsageClaudeRenewConfirm, "SideMon", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _hardware.RenewStatus = "Renewing (Step 2)...";
                await claude.RunCmdAsync("-p \\"ok\\" --safe-mode --tools \\"\\"", 60);
                await claude.FetchAsync(System.Threading.CancellationToken.None);
                UsageRegistry.PollIfDue(claude);
                snapshot = UsageRegistry.GetSnapshot(claude.Id);
                if (snapshot.Status != UsageStatus.AuthExpired)
                {
                    _hardware.RenewStatus = Resources.SettingsUsageClaudeRenewed;
                }
                else
                {
                    _hardware.RenewStatus = Resources.SettingsUsageClaudeStillExpired;
                }
            }
            btn.IsEnabled = true;
        }
'''

# insert before NumberBox_PreviewTextInput
content = content.replace('        private void NumberBox_PreviewTextInput', handler + '\\n        private void NumberBox_PreviewTextInput')

with open('SidebarDiagnostics/Settings.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
