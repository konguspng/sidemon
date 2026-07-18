<h1><img src="icon.png" width="48" height="48" align="top" /> SideMon</h1>

A lightweight hardware monitor sidebar for Windows 10/11. SideMon docks to the edge of your screen and shows your PC's vitals at a glance, without getting in the way and without eating the resources it's supposed to be measuring.

## Download

Grab the installer from the [releases page](https://github.com/konguspng/sidemon/releases) and run it. No prerequisites needed, the .NET runtime is bundled. On first run, SideMon offers to install the [PawnIO](https://pawnio.eu) driver, which is required for CPU/GPU clock, temperature, and voltage sensors on modern systems.

## Features

* Monitors CPU (clocks, temps, load), GPU, RAM, network throughput, and drives.
* **Frosted-glass background** with adjustable blur, tint, and edge fade.
* **Bundled fonts**: pick from Titillium Web, Rajdhani, Chakra Petch, or Share Tech Mono. No font installation needed.
* **Game-friendly**: monitoring and rendering pause automatically while a fullscreen app runs on the sidebar's screen.
* Graphs for all metrics, configurable alerts with blink, global hotkeys, clock and date display.
* Per-monitor DPI aware; works across multi-monitor setups.
* Minimalist icon set and extensive customization (width, colors, opacity, text alignment, ordering).

## Built to be light

* Sensors poll on a background thread; the UI only updates values that actually changed.
* The glass blur is rendered **once** and cached. No live shader effects.
* No installed services, no telemetry, no auto-updater running in the background.

## Requirements

* Windows 10 or Windows 11 (x64)
* Administrator rights (required for hardware sensor access)

## Credits

* Based on [Sidebar Diagnostics](https://github.com/ArcadeRenegade/SidebarDiagnostics) by ArcadeRenegade. Thank you for the original project!
* Hardware data provided by [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).
* Kernel driver by [PawnIO](https://pawnio.eu).
* Bundled fonts are licensed under the SIL Open Font License (see `SidebarDiagnostics/Fonts/FONT-LICENSES.txt`).

## License

GNU General Public License. Please link back to this repository if you redistribute.
