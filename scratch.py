import os
import re

# Update SettingsModel.cs
with open("SidebarDiagnostics/SettingsModel.cs", "r", encoding="utf-8") as f:
    text = f.read()

# Add AccentColorEnabled
text = re.sub(r'public bool ShowAccentBars\s*{\s*get\s*{\s*return _showAccentBars;\s*}\s*set\s*{\s*_showAccentBars = value;\s*NotifyPropertyChanged\("ShowAccentBars"\);', 
              r'public bool ShowAccentBars { get { return _showAccentBars; } set { _showAccentBars = value; NotifyPropertyChanged("ShowAccentBars"); NotifyPropertyChanged("AccentColorEnabled");', text)

text = re.sub(r'public bool UseCardStyle\s*{\s*get\s*{\s*return _useCardStyle;\s*}\s*set\s*{\s*_useCardStyle = value;\s*NotifyPropertyChanged\("UseCardStyle"\);',
              r'public bool UseCardStyle { get { return _useCardStyle; } set { _useCardStyle = value; NotifyPropertyChanged("UseCardStyle"); NotifyPropertyChanged("AccentColorEnabled");', text)

# Add AccentColorEnabled property
if "public bool AccentColorEnabled" not in text:
    text = re.sub(r'(public bool ShowAccentBars)', r'public bool AccentColorEnabled => ShowAccentBars || UseCardStyle;\n\n        \1', text)

with open("SidebarDiagnostics/SettingsModel.cs", "w", encoding="utf-8") as f:
    f.write(text)

# Update FlatStyle.xaml
with open("SidebarDiagnostics/FlatStyle.xaml", "r", encoding="utf-8") as f:
    flat = f.read()

if "SettingsSectionBorder" not in flat:
    styles = """
    <Style x:Key="SettingsSectionBorder" TargetType="{x:Type Border}">
        <Setter Property="BorderBrush" Value="#E0E0E0" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="4" />
        <Setter Property="Background" Value="#FAFAFA" />
        <Setter Property="Margin" Value="0,0,0,16" />
        <Setter Property="Padding" Value="12,12,12,12" />
    </Style>
    <Style x:Key="SettingsSectionHeader" TargetType="{x:Type TextBlock}">
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="Foreground" Value="#333333" />
        <Setter Property="Margin" Value="0,0,0,4" />
    </Style>
    <Style x:Key="SettingsSectionHelper" TargetType="{x:Type TextBlock}">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="#666666" />
        <Setter Property="TextWrapping" Value="Wrap" />
        <Setter Property="Margin" Value="0,0,0,12" />
    </Style>
    <Style x:Key="SettingsHint" TargetType="{x:Type TextBlock}">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Foreground" Value="#888888" />
        <Setter Property="TextWrapping" Value="Wrap" />
        <Setter Property="Margin" Value="0,2,0,4" />
        <Setter Property="FontStyle" Value="Italic" />
    </Style>
"""
    flat = flat.replace("</ResourceDictionary>", styles + "</ResourceDictionary>")
    with open("SidebarDiagnostics/FlatStyle.xaml", "w", encoding="utf-8") as f:
        f.write(flat)
print("Updated Models and Styles.")
