import re

with open('SidebarDiagnostics/Sidebar.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

find_str = '''            <Border Margin="5,15" VerticalAlignment="{Binding Source={x:Static frame:Settings.Instance}, Path=ContentVerticalAlign, Mode=OneWay}">
                <StackPanel x:Name="ContentStack" Style="{StaticResource ContentPanel}">
                    <ContentControl>'''

replace_str = '''            <Border Margin="5,15" VerticalAlignment="{Binding Source={x:Static frame:Settings.Instance}, Path=ContentVerticalAlign, Mode=OneWay}">
                <StackPanel x:Name="ContentStack" Style="{StaticResource ContentPanel}">
                    <TextBlock Text="{Binding Path=MonitorManager.HardwareLimitedReason}" Visibility="{Binding Path=MonitorManager.HardwareLimited, Converter={StaticResource BoolToVisibility}}" Opacity="0.5" TextWrapping="Wrap" TextAlignment="Center" Margin="0,0,0,10" Style="{StaticResource DataText}" />
                    <ContentControl>'''

content = content.replace(find_str, replace_str)

with open('SidebarDiagnostics/Sidebar.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
