# SideMon Changelog

This file is the source for GitHub release notes. Keep it updated as changes land; each version section becomes the release body when that version ships.

## 4.2.0 (unreleased, in development)

- New Motherboard monitor: chipset/VRM temperature, case fan, and voltage sensors, available from the Monitors tab like any other monitor type.
- New Battery monitor: charge level, time remaining, charge/discharge rate (Watts), and voltage. On a desktop with no battery this monitor simply shows nothing, the same as any other unavailable hardware.
- New FPS overlay (Settings > Overlay tab): a small, minimalist, click-through counter that can sit in any of the 4 screen corners with adjustable opacity, matching the sidebar's accent color/theme. It reads live frame stats from RivaTuner Statistics Server (RTSS) if installed and running, rather than SideMon hooking into games itself - real per-game FPS capture requires intercepting each graphics API's present call, which is exactly what RTSS already does. Without RTSS running, the overlay shows "No RTSS" instead of a number.
- Per-core CPU load and per-core CPU clock breakdowns were already available in the CPU monitor's row details (Monitors tab) - per-core load is enabled by default.
- Disk read/write throughput (Drives monitor) was already available and enabled by default.
- New Vertical Align setting (Top/Middle/Bottom, under Customize > Layout & Text) controlling where content sits when it's shorter than the screen.
- Content that overflows the screen height (many drives, a larger font, a higher UI scale) now shrinks automatically to fit, instead of scrolling or clipping. Scrolling was removed entirely: with Click-Through enabled the sidebar was never actually scrollable in the first place, since click-through makes the whole window mouse-transparent by design. Auto-fit works by measuring the enabled monitors/drives and, if they're taller than the screen, writing the scale needed to fit into the UI Scale setting (Advanced tab) - the same setting you can also set manually. This means UI Scale now reflects the real effective scale at all times, and shrinks the whole sidebar (not just the drive list) when things don't fit; it renders at full size (UI Scale 1.0) whenever everything already fits. The computed value is rounded to 2 decimal places.
- Fixed: the auto-fit check ran before the drive/monitor list had actually finished rendering, so it measured a near-empty layout and never detected real overflow (e.g. enabling more drives in the Monitors tab). It now waits for layout to genuinely go quiet (debounced) before measuring, which is reliable across repeated Monitors tab changes, not just on first launch.
- Hard cap: once your enabled monitors need more room than the screen at full size, UI Scale can no longer be raised past the point where it would overflow again. The Advanced tab shows a note explaining the cap and how to lift it (disable some monitors/drives).
- Fixed: the cap/scale could get stuck too low even after removing enough monitors to fit again. The available-height check was reading the window's own Height property, which gets silently corrupted the first time UI Scale changes (a pre-existing side effect of legacy DPI-scaling code reacting to any UI Scale change, not something introduced by auto-fit). It now reads the monitor's real work area directly, which UI Scale changes can't affect.
- Toggling "Run at Startup" now shows a confirmation dialog on save: success ("SideMon will now start automatically when you log in") or a warning if the scheduled task couldn't be created/removed, instead of failing silently with no feedback at all. This is exactly the kind of failure that hid the 4.0/4.1.0 startup bug for so long.
- Fixed: in Desktop Blur mode, using tray menu Show after Hide brought the sidebar to the front of other windows instead of staying behind them. `Show()` re-activates and raises a window to the top of its z-band regardless of prior state; the bottom-of-stack/non-topmost policy is now reapplied every time the sidebar is shown, not just once at launch.

## 4.1.2 (released)

- New: General settings now shows PawnIO driver status directly, with a manual "Install PawnIO Driver" button and progress indicator when it's missing, so you're not solely dependent on the automatic startup prompt.
- Fixed: the PawnIO driver install prompt could fail to ever appear on startup. It was scheduled at the lowest dispatcher priority (ApplicationIdle), but continuous sensor-polling UI updates could keep the dispatcher queue from ever going fully idle, starving it out indefinitely. It's now scheduled with a short fixed delay instead, so it reliably runs.
- Failures in the PawnIO prompt flow are now caught and written to `error.log` instead of silently disappearing.

## 4.1.1 (released)

- Fixed: opening Settings could crash if a removable/USB drive was monitored and became briefly unavailable (a stale performance-counter instance is now skipped instead of crashing the reload).
- Fixed: the guided PawnIO driver install could fail with "the process cannot access the file" on a retry, because it reused the same temp file path as a prior attempt.

## 4.1.0 (released)

### Card panels
- New card panel style: each monitor group renders as a rounded dashboard card with a subtle border, similar to gaming performance overlays. On by default; switch back to the classic flat look under Customize > Panels.
- Accent color setting (default cyan) used for card icons and load bars, with a color picker.
- Slim accent load bars under every percentage metric in card mode.
- Fixed: GPU VRAM bar showed permanently full when "Show VRAM in GB" was enabled; bars now follow each metric's normalized percentage.

### Background system overhaul
- Background is now one of three exclusive styles: Solid Color, Windows Accent Color, or Desktop Blur (Glass). Picking one disables the controls that do not apply.
- Desktop Blur renders your actual wallpaper file (honoring Fill, Fit, Stretch, Center, Tile, and Span layouts) instead of photographing the screen, so open windows can never leak into the glass and there is no capture flicker.
- Pixel-perfect alignment between the glass and the real wallpaper behind it, including on scaled displays.
- New Blur Width slider: the glass pane can extend beyond the sidebar itself and dissolve into the desktop via Edge Fade. Screen space reserved for maximized windows stays at the sidebar width.
- Blur strength and edge fade now apply only in Desktop Blur mode; Solid and Accent backgrounds are never masked or faded.
- Fixed: background could disappear when applying settings while the settings dialog overlapped the sidebar; appearance changes now rebuild the sidebar cleanly.

### Desktop integration
- In Desktop Blur mode the sidebar sits at the bottom of the window stack: other windows always cover it, preserving the illusion that it is part of the desktop. Always on Top is disabled in this mode.
- Win+D and the taskbar's show-desktop button no longer hide the sidebar on Windows 11 24H2+; it stays visible as part of the desktop and drops back behind windows when you resume work.
- Clicking the desktop while windows are open no longer raises the sidebar above them; it only comes forward when the desktop is actually shown (all app windows minimized).
- When lifted on the bare desktop, the sidebar stays in the normal window band: the taskbar, tray, and tray flyouts always render above it, and show-desktop no longer flickers.

### Settings window
- Widened to a two-column layout with grouped sections: Layout & Text, Background, Panels, Alerts, Clock & Header.
- Window height is capped to the working area so Save/Apply always stay above the taskbar on any resolution; content scrolls if needed.
- Clearer labels and tooltips throughout; labels no longer clip.

### First-run reliability
- The PawnIO driver prompt no longer blocks app startup; it now runs after the sidebar is already visible, with a small progress window while the driver downloads and installs, instead of appearing to freeze with no feedback.
- Sensors reload automatically after the driver installs successfully, so CPU/GPU values populate without restarting the app.
- A hardware/driver failure on a fresh machine no longer crashes the whole app with a raw error dump; it degrades to showing no panels (logged) instead, and the app keeps running.
- Unhandled errors now show a short, readable message with an option to open the error log, instead of a full stack trace dialog.
- The self-contained installer bundles the .NET runtime; installing from `SideMon-<version>-Setup.exe` should never prompt for a separate .NET download. If you saw one, it likely came from a stray/older non-self-contained build.
- The uninstaller now offers to also remove the PawnIO sensor driver (opt-in, off by default, since other hardware-monitoring apps may depend on it). There is nothing to remove for .NET: SideMon never installs a system-wide runtime.
- After the PawnIO driver installs successfully, SideMon now asks with a small dialog ("Reload Now" / "Later") instead of silently reloading, so you know it happened and stay in control of when the reload occurs.
- Fixed: the PawnIO driver install failed with "Unknown argument: /S". PawnIOSetup.exe is not an NSIS installer and uses its own CLI (`-install -silent`); the wrong flag meant automatic driver installation never actually worked.
- Fixed: launching SideMon right after installation could fail with "CreateProcess failed; code 740, the requested operation requires elevation." The installer's post-install launch step now uses ShellExecute, which correctly negotiates UAC for the app's own elevation requirement.
- Fixed: "Run at Startup" never actually created a working scheduled task. `schtasks /create` was called with `/rl HIGHEST` combined with `/xml`, a combination schtasks rejects outright (exit code 5); the failure was silent because nothing checked the result. This has likely never worked. The command no longer passes the conflicting flag, and schtasks failures are now logged with their real error output instead of disappearing.

## 4.0.0 (released)

See the v4.0.0 GitHub release: .NET 8 migration, SideMon rename, Libre Hardware Monitor 0.9.6 with guided PawnIO install, frosted-glass background, bundled fonts, minimalist icons, fullscreen auto-pause, background sensor polling, update notifications, self-contained installer.
