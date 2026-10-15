import os

def replace_in_file(path, old, new):
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()
    content = content.replace(old, new)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)

resx_designer = "SidebarDiagnostics/Properties/Resources.Designer.cs"
replace_in_file(resx_designer, 'return ResourceManager.GetString("SettingsAdvancedTab", resourceCulture);', 'return "Layout";')
replace_in_file(resx_designer, 'return ResourceManager.GetString("SettingsCustomizeTab", resourceCulture);', 'return "Appearance";')
print("Designer updated.")
