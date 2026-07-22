# SideMon Changelog

This file is the source for GitHub release notes. Keep it updated as changes land; each version section becomes the release body when that version ships.

## 4.1.2 (released)

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
