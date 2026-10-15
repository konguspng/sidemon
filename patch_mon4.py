import re

with open('SidebarDiagnostics/Monitoring.cs', 'r', encoding='utf-8') as f:
    content = f.read()

find_str = '''            catch (Exception e)
            {
                // a fresh machine may not have the PawnIO driver loaded yet, or the
                // hardware library can throw on unusual configurations; degrade to no
                // panels rather than crashing the whole app on first run
                Utilities.ErrorLog.Write(e);

                MonitorPanels = Array.Empty<MonitorPanel>();
            }'''

replace_str = '''            catch (Exception e)
            {
                Utilities.ErrorLog.Write(e);

                if (e is ArgumentNullException) // ArgumentNullException("identity")
                {
                    HardwareLimited = true;
                    try
                    {
                        _computer = new Computer()
                        {
                            IsCpuEnabled = false,
                            IsControllerEnabled = false,
                            IsGpuEnabled = false,
                            IsStorageEnabled = false,
                            IsMotherboardEnabled = false,
                            IsMemoryEnabled = true,
                            IsNetworkEnabled = false,
                            IsBatteryEnabled = true
                        };
                        _computer.Open();
                        _board = null;
                        
                        MonitorPanels = config.Where(c => c.Enabled).OrderByDescending(c => c.Order).Select(c => NewPanel(c)).Where(p => p != null).ToArray();
                    }
                    catch
                    {
                        MonitorPanels = Array.Empty<MonitorPanel>();
                    }
                }
                else
                {
                    MonitorPanels = Array.Empty<MonitorPanel>();
                }
            }'''

content = content.replace(find_str, replace_str)

props = '''
        public bool HardwareLimited { get; private set; }
        public string HardwareLimitedReason { get { return global::SidebarDiagnostics.Framework.Resources.HardwareLimitedWarning; } }

        public void Dispose()
'''

content = content.replace('        public void Dispose()', props)

with open('SidebarDiagnostics/Monitoring.cs', 'w', encoding='utf-8') as f:
    f.write(content)
