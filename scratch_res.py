import os

def replace_in_file(path, old, new):
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()
    content = content.replace(old, new)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)

resx = "SidebarDiagnostics/Properties/Resources.resx"
replace_in_file(resx, '<value>Advanced</value>', '<value>Layout</value>')
replace_in_file(resx, '<value>Customize</value>', '<value>Appearance</value>')

print("Resources updated.")
