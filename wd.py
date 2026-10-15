import ctypes, time; [ctypes.windll.user32.keybd_event(k, 0, f, 0) for i in range(3) for k, f in [(0x5B, 0), (0x44, 0), (0x44, 2), (0x5B, 2)]]
