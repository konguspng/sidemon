with open('SidebarDiagnostics/Monitoring.cs', 'r', encoding='utf-8') as f:
    content = f.read()

target = '''                        if (!_cachedOfflineHardware.TryGetValue(type, out var cached))
                        {
                            using (var probe = new Computer())
                            {
                                switch (type)'''

replacement = '''                        if (!_cachedOfflineHardware.TryGetValue(type, out var cached))
                        {
                            var probe = new Computer();
                            try
                            {
                                switch (type)'''

content = content.replace(target, replacement)
content = content.replace('''                                probe.Close();
                            }
                            _cachedOfflineHardware[type] = cached;''', '''                                probe.Close();
                            }
                            finally
                            {
                            }
                            _cachedOfflineHardware[type] = cached;''')

with open('SidebarDiagnostics/Monitoring.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print('Fixed')
