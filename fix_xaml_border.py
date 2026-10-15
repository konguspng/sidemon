file_path = "SidebarDiagnostics/Settings.xaml"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

bad_str = '<Border Style="{StaticResource SettingsSectionBorder}">'
good_str = '<Border>'

content = content.replace(bad_str, good_str)
with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
