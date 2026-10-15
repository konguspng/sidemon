import re
import json

def get_bindings(xaml_path):
    with open(xaml_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # regex to find Path=... or just {Binding PropertyName
    # usually Path=PropertyName, Mode=...
    paths = set()
    # Find all {Binding ...}
    bindings = re.findall(r'\{Binding\s+([^\}]+)\}', content)
    for b in bindings:
        if 'ElementName=' in b or 'RelativeSource=' in b:
            continue # Element bindings
        
        path_match = re.search(r'Path=([a-zA-Z0-9_]+)', b)
        if path_match:
            paths.add(path_match.group(1))
        else:
            # check if it's the first positional arg
            first_arg = b.split(',')[0].strip()
            if '=' not in first_arg and first_arg:
                paths.add(first_arg)
    
    return sorted(list(paths))

after_bindings = get_bindings("SidebarDiagnostics/Settings.xaml")

with open(r"C:\Users\kong\AppData\Local\Temp\settings_bindings_before.json", 'r', encoding='utf-8') as f:
    before_bindings = json.load(f)

print("Before bindings:", len(before_bindings))
print("After bindings:", len(after_bindings))

missing = set(before_bindings) - set(after_bindings)
extra = set(after_bindings) - set(before_bindings)

print("Missing (should be empty):", missing)
print("Extra:", extra)
