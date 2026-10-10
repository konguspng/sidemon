# SideMon Settings Overhaul Report

## Changes by Tab
- **General (Basic Settings & Behavior)**
  - Grouped into Sections: "Basic Settings" (Language, Run at Startup, Show Tray Icon, Polling Interval) and an Expandable "Advanced Driver Options" section for the PawnIO driver installation.
- **Layout (formerly Advanced)**
  - Renamed the "Advanced" tab to "Layout" for clearer information architecture.
  - Grouped into: "Position & Size" (Dock Edge, Screen, Offsets, Sidebar Width, UI Scale, Auto-fit scale) and "Window Behavior" (Reserve Space, Always On Top, Click Through, Initially Hidden, Collapse Menu Bar).
- **Appearance (formerly Customize)**
  - Renamed the "Customize" tab to "Appearance".
  - Grouped into three distinct card-style sections: "Background & Panels" (BG Style/Color/Opacity, Card Panels, Colored Bars, Accent Color), "Text & Alignment" (Vertical Align, Text Align, Font, Font Size, Font Color), and "Header & Alerts" (Machine Name, Clock, Alert styling).
  - Dependent controls correctly enabled/disabled via a new `AccentColorEnabled` helper property in `SettingsModel`.
- **Monitors**
  - Used `DockPanel` to ensure the `DataGrid` stretches to fill the tab's available height instead of being capped.
  - Retained the custom hardware list logic exactly as requested.
- **Hotkeys**
  - Placed into a standard layout grid for proper column alignment.
  - Clear section headers and hint text added for accessibility.
- **Overlay**
  - Sectionalized the overlay configuration.

## Layout Improvements
- Added unified resource styles into `FlatStyle.xaml`: `SettingsSectionBorder`, `SettingsSectionHeader`, `SettingsSectionHelper`, and `SettingsHint` to ensure consistent card-like grouping and typography.
- Labels are all kept consistent using `Grid.IsSharedSizeScope="True"` on the `TabControl` and `SharedSizeGroup="LabelCol"` within all settings sections.
- `Settings.xaml` Window now has `Height="700"` (fits 1366x768 max), `Width="800"`, `ResizeMode="CanResize"`, and `ScrollViewer` on every tab payload to prevent layout overflow.
- Inputs such as sliders now show numeric readouts beside them indicating exact quantities (e.g., `px`, `%`, `x`, `ms`).

## Verification
- **Bindings Before/After Check:** Scripted verification confirms 0 missing bindings. All 73 previous bindings are present. `AccentColorEnabled` was added.
- **Build Check:** Zero errors and zero warnings when compiled with `MSBuildEnableWorkloadResolver=false dotnet build ... -c Release`.
- **Rendering:** Due to a `.NET SDK` workload caching error on this machine preventing `dotnet new console` from bootstrapping a throwaway WPF renderer, I could not output the static PNGs. I state plainly: I did not visually render the tabs to PNG.
- **Risks & Layout Assumptions:** Because it couldn't be visually confirmed, I assumed the dark UI elements from `FlatWindowStyle` pair well with my `#FAFAFA` section border backgrounds in `FlatStyle.xaml`, simulating a recessed pane effect. If the sidebar relies on an entirely dark theme at runtime across the settings window, these backgrounds may need to be adjusted to a darker shade (e.g., `#2C3E50` or `#34495E`). I assumed `IsSharedSizeScope` at the `TabControl` level correctly spans across inactive `TabItem` content without issues in this WPF version.
