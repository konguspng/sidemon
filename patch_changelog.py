import re

file_path = "CHANGELOG.md"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

new_bullets = """- **Crash Prevention**: Improved defensive coding across startup paths, monitor detection, performance counters, and settings loading to prevent crashes on diverse hardware configurations. Unhandled exceptions are now safely logged.
- **UI Scale Auto-Fit**: Added a toggle for "Shrink automatically to fit the screen" (enabled by default). The UI Scale slider is no longer hard-capped below 1.00 when auto-fit reduces the scale, allowing you to freely adjust it up to 3.00.
- **Settings Window Improvements**: The Settings window now appears in the taskbar, opens correctly above other windows, displays the app's proper icon, and is titled "SideMon Settings". Renamed user-facing product references from "Sidebar Diagnostics" to "SideMon"."""

content = content.replace("- **Thread Safety**:", new_bullets + "\n- **Thread Safety**:")

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
