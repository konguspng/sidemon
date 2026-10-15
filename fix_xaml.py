file_path = "SidebarDiagnostics/Settings.xaml"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

bad_str = '<TextBlock Text="{x:Static frame:Resources.SettingsNoExtraOptions}" Style="{StaticResource SettingsSectionHelper}" Margin="0,5,0,0">'
good_str = '<TextBlock Text="{x:Static frame:Resources.SettingsNoExtraOptions}" Margin="0,5,0,0">'

content = content.replace(bad_str, good_str)
with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
