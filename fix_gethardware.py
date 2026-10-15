import re

with open('SidebarDiagnostics/Monitoring.cs', 'r', encoding='utf-8') as f:
    content = f.read()

replacement = '''        private Dictionary<MonitorType, HardwareConfig[]> _cachedOfflineHardware = new Dictionary<MonitorType, HardwareConfig[]>();

        public HardwareConfig[] GetHardware(MonitorType type)
        {
            switch (type)
            {
                case MonitorType.CPU:
                case MonitorType.RAM:
                case MonitorType.GPU:
                case MonitorType.Motherboard:
                case MonitorType.Battery:
                    bool isEnabled = false;
                    if (_computer != null)
                    {
                        switch (type)
                        {
                            case MonitorType.CPU: isEnabled = _computer.IsCpuEnabled; break;
                            case MonitorType.RAM: isEnabled = _computer.IsMemoryEnabled; break;
                            case MonitorType.GPU: isEnabled = _computer.IsGpuEnabled; break;
                            case MonitorType.Motherboard: isEnabled = _computer.IsMotherboardEnabled; break;
                            case MonitorType.Battery: isEnabled = _computer.IsBatteryEnabled; break;
                        }
                    }

                    if (isEnabled)
                    {
                        return GetHardware(type.GetHardwareTypes()).Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name }).ToArray();
                    }
                    else
                    {
                        if (!_cachedOfflineHardware.TryGetValue(type, out var cached))
                        {
                            using (var probe = new Computer())
                            {
                                switch (type)
                                {
                                    case MonitorType.CPU: probe.IsCpuEnabled = true; break;
                                    case MonitorType.RAM: probe.IsMemoryEnabled = true; break;
                                    case MonitorType.GPU: probe.IsGpuEnabled = true; break;
                                    case MonitorType.Motherboard: probe.IsMotherboardEnabled = true; probe.IsControllerEnabled = true; break;
                                    case MonitorType.Battery: probe.IsBatteryEnabled = true; break;
                                }
                                probe.Open();
                                var types = type.GetHardwareTypes();
                                cached = probe.Hardware.Where(h => types.Contains(h.HardwareType)).Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name }).ToArray();
                                probe.Close();
                            }
                            _cachedOfflineHardware[type] = cached;
                        }
                        return cached;
                    }

                case MonitorType.HD:
                    return DriveMonitor.GetHardware().ToArray();

                case MonitorType.Network:
                    return NetworkMonitor.GetHardware().ToArray();

                case MonitorType.AIUsage:
                    return UsageRegistry.GetHardware();

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }'''

target = '''        public HardwareConfig[] GetHardware(MonitorType type)
        {
            switch (type)
            {
                case MonitorType.CPU:
                case MonitorType.RAM:
                case MonitorType.GPU:
                case MonitorType.Motherboard:
                case MonitorType.Battery:
                    return GetHardware(type.GetHardwareTypes()).Select(h => new HardwareConfig() { ID = h.Identifier.ToString(), Name = h.Name, ActualName = h.Name }).ToArray();

                case MonitorType.HD:
                    return DriveMonitor.GetHardware().ToArray();

                case MonitorType.Network:
                    return NetworkMonitor.GetHardware().ToArray();

                case MonitorType.AIUsage:
                    return UsageRegistry.GetHardware();

                default:
                    throw new ArgumentException("Invalid MonitorType.");
            }
        }'''

if target in content:
    content = content.replace(target, replacement)
    with open('SidebarDiagnostics/Monitoring.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print('Replaced')
else:
    print('Target not found')
