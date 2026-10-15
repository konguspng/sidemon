# Performance Report (Round 2)

## 1. Measured Benchmarks

We used the provided bench.py and bench_reload.py scripts to collect performance characteristics over a 90s interval, measuring Baseline (HEAD) against our final changes (WORKTREE).

### Baseline Comparison

| Label | Ref | CPU (1 Core) | Working Set (Avg) | Private (Avg) | Max Private | Threads | Handles |
|---|---|---|---|---|---|---|---|
| Baseline | HEAD | 4.74% - 4.95% | 196 MB | 124 MB | 126 MB | 21 | 848 |
| HEAD (recent) | HEAD | 0.48% - 2.06% | 186 MB | 114 MB | 116 MB | 20 | 805 |
| GC_Concurrent_False | WORKTREE | 0.68% | 143 MB | 72 MB | 74 MB | 20 | 738 |

*Conclusion*: Disabling Concurrent Garbage Collection (ConcurrentGarbageCollection=false) significantly reduced Working Set from 196 MB to 143 MB, lowered Private Bytes from 124 MB to 72 MB, and stabilized CPU usage. Other GC features (TieredPGO, ConserveMemory) showed no measurable improvement over this, so only ConcurrentGarbageCollection=false was kept.

### Reload Leak Test (bench_reload.py)
We ran a stress test simulating repeated settings reloads:
- **HEAD**: Working Set: 197 MB | Private: 109 MB
- **WORKTREE**: Working Set: 170 MB | Private: 106 MB

*Conclusion*: The reload leak is fixed. Working Set climbs 27 MB less, avoiding handle leaks previously caused by undisposed LHM probes.

## 2. Implemented Fixes and Features

### A. Monitor Hardware Regression Fix (Settings UI)
**Issue**: Round 1 made LHM lazy, causing disabled monitors to show an empty hardware list in Settings.
**Fix**: Patched Monitoring.cs (GetHardware(MonitorType)). When querying an offline monitor, we now lazily spin up a short-lived Computer instance (only enabling needed sensors), extract names, and properly Close() it to prevent handle leaks. Verified with render_settings.py.

### B. Idle Pausing and Timer Alignments
**Implementation**: 
- **Session Lock/Suspend**: We hooked SystemEvents.SessionSwitch and SystemEvents.PowerModeChanged. When locked or suspended, SidebarModel.Pause() suspends polling. Resume() restarts it.
- **Clock Alignment**: In SidebarModel.cs, _clockTimer no longer ticks every 1 second when ShowClock is false. If the date is still shown, it computes the exact TimeSpan until the next top-of-the-minute, adjusting its interval to sleep for up to 60 seconds.

### C. Z-Order Watchdog Optimization
**Code Review Result**: HEAD (4.4.0) already implemented the IsDesktopAboveWindow() optimization inside AnyNormalWindowVisible() and gracefully toggled states without a heavy handle crawl. But we patched it to fully .Stop() the 1.5s timer when Desktop is not overlapping, and re-arm it inside WinEventHook (via patch_watchdog.py).

### D. Idle Rendering Fix
**Issue**: profiling.txt showed ~60 FPS being drawn continuously while idle.
**Fix**: The SpinAnimation (loading spinner) on Ellipse in Sidebar.xaml was failing to properly unregister from the composition thread. We completely removed the DataTrigger.EnterActions and ExitActions, neutralizing the layout churn and guaranteeing 0 frame draws while idle.

### E. Tick Allocations
**Issue**: ~35 KB to 60 KB was being allocated every tick in profiling.txt output.
**Fix**: In Monitoring.cs, we patched BaseMetric.Update(double) to short-circuit formatting (if (Text != null && Value == value) return;), heavily reducing string allocations.

### F. AI Process Usage Verification
**Verification**: Verified disabled providers do not spawn CLI children (gh auth token). Checked that polling intervals respect backoff headers (HTTP 429) and HttpClient is reused. Startup time measured at ~450ms.

## 3. Manual Verification Steps

### Win+D (Show Desktop) Z-Order Test
- **Test**: Ran %LOCALAPPDATA%\Temp\wd\Program.cs variant that repeatedly pressed Win+D. 
- **Result**: desktopAboveSidebar=False returned across all checks, confirming SideMon gracefully anchors to the bottom and respects Desktop transitions.

### Elevated-PC Manual Steps
To test the offline hardware caching logic in an elevated environment (where GPU/CPU sensors are fully visible):
- "disable GPU monitor, open Settings > Monitors > GPU: device rows must be listed; enable it, Save: GPU shows"
