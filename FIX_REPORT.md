# SideMon Fix Report

## Task A: Crash Prevention and Defensive Coding
Extensive defensive programming techniques were applied across the application to prevent startup, polling, and initialization crashes on diverse hardware configurations. The changes were made to degrade gracefully rather than throwing unhandled exceptions.

*   **`SidebarDiagnostics/Utilities.cs`**: Improved `ErrorLog.Write` to properly serialize and log inner exceptions. Added a one-time startup diagnostic logging feature to record OS version, .NET version, culture, and monitor count to make future crash reports actionable.
*   **`SidebarDiagnostics/App.xaml.cs`**: Wrapped critical startup steps in `try/catch` blocks (`OnStartup`, `StartApp`, `CheckForUpdates`, tray icon creation). Added a global exception handler (`DispatcherUnhandledException`) to prevent fatal application crashes on non-critical UI errors.
*   **`SidebarDiagnostics/Windows.cs`**: Hardened monitor detection logic (`Monitor.GetMonitors`, `GetMonitorFromIndex`). Handled edge cases such as missing monitors, negative coordinates, and zero-sized work areas. Added guards around DWM and AppBar registration APIs which can fail or throw in VM/RDP or no-GPU environments.
*   **`SidebarDiagnostics/Monitoring.cs`**: Replaced risky `.Single()` and `.First()` LINQ calls with `.FirstOrDefault()` and added null checks across the board. Guarded `LibreHardwareMonitor` and `PerformanceCounter` initialization, handling missing sensors, divide-by-zero occurrences, missing performance counter categories, and culture-specific parsing anomalies.
*   **`SidebarDiagnostics/UsageProviders.cs`**: Guarded `Process.Start` calls for AI CLI monitoring to prevent crashes when the CLI is missing or unexecutable.

## Task B: UI Scale Auto-Fit and DPI Math

*   **`SidebarDiagnostics/Sidebar.xaml.cs` (`AutoFitUIScale`)**: Modified the logic to only apply auto-scaling if the new explicit `AutoFitScale` setting is enabled. Addressed the "slider pinned below 1.00" bug by removing the aggressive logic that hard-capped `MaxUIScale` and forced `UIScale = 1.0` when content didn't overflow. The slider is now uncapped up to 3.0.
*   **`SidebarDiagnostics/Settings.xaml` & `Settings.xaml.cs`**: Added the new "Shrink automatically to fit the screen" checkbox and bound the UI Scale slider to a fixed maximum of 3.0. Added a formatted warning message that only displays when `AutoFitScale` actually reduces the UI scale to prevent overflow. Added a custom `IWin32Window` wrapper for legacy dialogs.
*   **`SidebarDiagnostics/Settings.cs` & `SettingsModel.cs`**: Added the `AutoFitScale` configuration property.
*   **`SidebarDiagnostics/Properties/Resources.resx` & `Designer.cs`**: Registered new localized strings for the Auto-Fit feature.

### Task B.4: DPI Math Investigation Result
The underlying DPI math in `AutoFitUIScale` (`WorkArea * InverseScaleY`) was determined to be **correct**.
Since the application uses `PerMonitorV2` awareness (defined in `app.manifest`), the Win32 `GetMonitorInfo` API returns values in pure physical pixels. `InverseScaleY` (which relies on `GetDpiForMonitor`) correctly converts those physical pixels back into WPF Device Independent Pixels (DIPs), perfectly matching the scale factor WPF uses for layout. 
The actual cause of the bug preventing users from reaching 1.00 on smaller screens was a logic flaw in `AutoFitUIScale`: it used to hard-cap `MaxUIScale` to the reduced scale when overflowing, and forced `UIScale` to 1.0 when not overflowing. By decoupling the slider's `Maximum` from `MaxUIScale` and introducing an explicit auto-fit toggle, the slider operates freely and the math operates accurately.

## Task C: Settings Window

*   **`SidebarDiagnostics/Settings.xaml.cs`**: Removed the `Owner = sidebar` relationship to decouple the Settings window from the Sidebar's strict bottom-pinned Z-order policy. Added `ShowInTaskbar = true` and implemented a temporary `Topmost` toggle hack upon window load to ensure it consistently appears above other windows.
*   **Settings Naming and Icons**: Updated window titles from "Sidebar Settings" to "SideMon Settings" across all `.xaml` definitions. Replaced references to the outdated `Settings.ico` with `Sidebar.ico` to unify the application icon.

## Verification
*   **Build**: Successfully compiled using the requested `MSBuildEnableWorkloadResolver=false dotnet build ...` command with 0 errors and 0 warnings.
*   **No File Deletions/Commit/Push**: Adhered strictly to instructions.
*   **Application Launch**: The GUI was explicitly avoided during execution as per instructions.
