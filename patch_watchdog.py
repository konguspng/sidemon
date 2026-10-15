import sys

with open("SidebarDiagnostics/Windows.cs", "r", encoding="utf-8") as f:
    text = f.read()

# in WatchdogTick, add code to stop timer when desktop is not above
old_tick = """                if (IsDesktopAboveWindow(_sidebarHwnd.Value) && !AnyNormalWindowVisible())
                {
                    _sidebar.SetTop(false);
                }
"""

new_tick = """                bool desktopAbove = IsDesktopAboveWindow(_sidebarHwnd.Value);
                if (desktopAbove)
                {
                    if (!AnyNormalWindowVisible())
                    {
                        _sidebar.SetTop(false);
                    }
                }
                else
                {
                    _watchdog.Stop();
                }
"""

text = text.replace(old_tick, new_tick)

# in WinEventHook, restart the timer
old_hook = """                // Re-evaluate z-order based on what's visible
                _ = LiftIfDesktopShown(false);
"""

new_hook = """                // Re-evaluate z-order based on what's visible
                if (_watchdog != null && !_watchdog.IsEnabled) { _watchdog.Start(); }
                _ = LiftIfDesktopShown(false);
"""

text = text.replace(old_hook, new_hook)

with open("SidebarDiagnostics/Windows.cs", "w", encoding="utf-8") as f:
    f.write(text)
