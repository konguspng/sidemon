import re

with open('SidebarDiagnostics/Monitoring.cs', 'r', encoding='utf-8') as f:
    content = f.read()

find_str = '''                bool enableBattery = config.Any(c => c.Enabled && c.Type == MonitorType.Battery);

                _computer = new Computer()'''

replace_str = '''                bool enableBattery = config.Any(c => c.Enabled && c.Type == MonitorType.Battery);
                if (TestHookForceHardwareFail != null && TestHookForceHardwareFail()) throw new ArgumentNullException("identity");
                _computer = new Computer()'''

content = content.replace(find_str, replace_str)

props = '''
        public bool HardwareLimited { get; private set; }
        public static Func<bool> TestHookForceHardwareFail { get; set; }
'''

content = content.replace('        public bool HardwareLimited { get; private set; }', props)

with open('SidebarDiagnostics/Monitoring.cs', 'w', encoding='utf-8') as f:
    f.write(content)
