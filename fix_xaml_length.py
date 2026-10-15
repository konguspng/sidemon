file_path = "SidebarDiagnostics/Settings.xaml"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

content = content.replace('Binding="{Binding Path=Metrics.Count}"', 'Binding="{Binding Path=Metrics.Length}"')
content = content.replace('Binding="{Binding Path=Params.Count}"', 'Binding="{Binding Path=Params.Length}"')

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
