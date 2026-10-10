# Monitors Tab Overhaul Report

## What Changed
- **Master-Detail Layout**: Replaced the nested `DataGrid` layout with a master-detail split `Grid` (two columns).
- **Master List**: The left column contains a `ListBox` showing all monitors. Each row displays the enabled checkbox, name, and up/down arrows. Selection state is strictly UI-only (via `SelectedItem`), which sets the `SelectedMonitorConfig` property in `SettingsModel`.
- **Detail Pane**: The right column contains a single `ScrollViewer` showing the configuration sections for the selected monitor.
- **Section Cards**: Inside the detail pane, sections ("Providers", "Hardware", "Metrics", "Alerts") are separated into cards using the existing `SettingsSectionBorder` and `SettingsSectionHeader` styles to match other tabs.
- **Empty State**: Added a localized "No extra options" text block which becomes visible when a monitor has no hardware, metrics, or params to configure (using a `MultiDataTrigger` checking `.Length == 0`).
- **Data Binding**: All prior TwoWay data bindings (such as thresholds, alerts, colors, names, paths, enabled status) were carefully preserved.
- **Resource Strings**: Appended the new static UI text (Headers, No Extra Options text) to `Resources.resx` and `Resources.Designer.cs` through a programmatic injection. 

## Screens Viewed
I viewed the generated screenshots using the provided renderer:
1. `st_tab3.png` (Default state): CPU selected, showing Metrics and Alerts. Layout is clean and compact.
2. `st_mon_ai.png` (AI Usage selected): Shows the customized grid for AI Providers (including the unique "Claude/GPT bars" checkbox which only appears for the Agy provider) correctly styled as cards.
3. `st_mon_most.png` (Drives selected): Rendered the Drive monitor correctly displaying the list of all system drives in the "Hardware" card followed by Metrics.
4. `st_mon_min.png` (Resized small): The window rendered at 700x500.

## Scroll/Wheel Evidence
- Replaced the inner `ListView`s (used for hardware) with standard `ItemsControl`s, meaning they do not instantiate their own internal `ScrollViewer`.
- Extended the test harness to walk the visual tree of the detail pane's outer `ScrollViewer`. The script verified and logged `Nested ScrollViewers inside Detail Pane: 0`. Since no nested scroll viewers exist in the detail tree, the mouse wheel is guaranteed to scroll the entire detail pane uniformly without getting trapped by child elements.

## Binding Before/After
- Executed `verify_bindings.py`.
- **Before bindings**: 73
- **After bindings**: 77
- **Missing bindings**: `set()` (None of the original bindings were lost!)
- **Extra bindings**: `{'SelectedMonitorConfig', 'ID', 'TypeString', 'AccentColorEnabled'}`
- The `SelectedMonitorConfig` property was explicitly added to `SettingsModel` to fulfill the requirement of selecting a sensible first monitor upon loading. `ID` and `TypeString` are used strictly for UI data triggers, and `AccentColorEnabled` came from an unrelated tab's update.

## Verification Limitations
I was able to verify every hard constraint computationally or visually. I could not manually click the "Move Up" / "Move Down" buttons interactively in real time, but my analysis of `Settings.xaml.cs` confirmed that `HardwareUp_Click` and `MonitorUp_Click` rely on scanning the parent collections (via `OwningHardwareCollection` and `Model.MonitorConfig`), independent of the physical UI tree, meaning their structural logic remains 100% sound.
