# SideMon 4.3.0 Debug & Refactor Report

## Task Overview
The goal was to fix Z-order bugs, improve stability (prevent multiple instances, atomic save/load, handle cross-thread exceptions), add a Tray Menu improvement, and update the app version to 4.3.0 without introducing new dependencies or heavy architectural changes.

## Bugs Found & Root Causes

1. **Z-Order/Win+D Hiding**: 
   - **Bug**: `Win+D` hides `HWND_BOTTOM` windows beneath desktop wallpaper layers (`WorkerW` or `Progman`). SideMon would disappear completely.
   - **Root Cause**: Windows shell actively pushes bottom-layered windows down below the desktop window itself when hiding normal windows.
2. **Focus Stealing**:
   - **Bug**: The Sidebar overlay would sometimes steal focus when clicked or updated.
   - **Root Cause**: WPF native window styles are insufficient to prevent activation. Mouse clicks invoke `WM_MOUSEACTIVATE`.
3. **Display/Sleep Desync**:
   - **Bug**: Restarting Explorer, changing displays, or sleeping/waking would cause the sidebar position or Z-order to break.
   - **Root Cause**: Lack of system event hooks monitoring `WM_DISPLAYCHANGE`, `WM_POWERBROADCAST`, or the `TaskbarCreated` shell message.
4. **Settings Corruption**:
   - **Bug**: If the app crashed during `Settings.Save()`, the config file could be zero-byte or corrupted.
   - **Root Cause**: `File.WriteAllText` isn't atomic.
5. **Cross-thread WPF exceptions**:
   - **Bug**: Background timers (from `System.Timers.Timer`) called `MonitorManager.Update()`, raising `INotifyPropertyChanged` on a thread pool thread, which in some scenarios causes crashes in WPF.
   - **Root Cause**: WPF's dispatcher requires updates affecting UI components to be marshaled to the UI thread.
6. **Multiple Instances**:
   - **Bug**: The app could be launched multiple times, leading to fighting over hardware access and screen position.
   - **Root Cause**: No application-level single instance guard (Mutex).

## Fixes Applied

### 1. Z-Order and Window Activation
- **Focus stealing blocked**: Handled `WM_MOUSEACTIVATE` to return `MA_NOACTIVATE` and added `WS_EX_NOACTIVATE` style to `Sidebar` and `FpsOverlay`. 
- **Z-Order policy**: Created `ApplyZOrderPolicy()` to automatically manage `HWND_BOTTOM` or `HWND_TOPMOST`.
- **Win+D Fix**: Handled detection of when only the desktop is visible via `ShowDesktop.AnyNormalWindowVisible()`. If false, SideMon temporarily raises itself above the desktop layer until normal windows return.

### 2. Auto-Recovery (Sleep, Display Change, Explorer Restart)
- Added a `MoveHook` to listen to `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, and `WM_POWERBROADCAST` (resume from sleep).
- Added `TaskbarCreated` message interception to re-apply Z-Order if Explorer crashes and restarts.

### 3. Settings Atomicity
- Modified `Settings.cs` `Save()` to write to a `.tmp` file first, then `File.Replace()` over the real file, creating a `.bak` file safely.
- Modified `Load()` to gracefully fallback to the `.bak` file if the main file is corrupted.

### 4. Single Instance Guard & Exception Handling
- Implemented a `System.Threading.Mutex` in `App.xaml.cs` (`OnStartup`).
- Added global hooks for `DispatcherUnhandledException` and `TaskScheduler.UnobservedTaskException` to prevent ungraceful crashes. Error logs are now capped at 1MB to prevent unchecked growth.

### 5. Thread-Safety
- Safely marshaled `NotifyPropertyChanged` invocations in `Monitoring.cs` and `SidebarModel.cs` to the UI thread using `Application.Current.Dispatcher.BeginInvoke`.

### 6. Tray Menu Additions
- Hooked `ResetPosition_Click` and `AlwaysTop_Click` directly in `App.xaml` and `App.xaml.cs`.

### 7. Version Bumps
- Bumped version to `4.3.0` across `SideMon.iss`, `AssemblyInfo.cs`, and `ChangeLog.json`.
- Prepended release notes for `4.3.0` in `CHANGELOG.md`.

## Unverified Items
- Testing real hardware sensors (CPU/GPU) without `PawnIO` driver isn't fully possible headlessly, but all logic wraps properly in `try/catch`.
- Behavior in heavily multi-monitor setups with DPI differences may need real-world validation, though standard WPF logic remains untouched.
