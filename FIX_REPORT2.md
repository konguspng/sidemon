# Fix Report 2

## Files Changed
- `SidebarDiagnostics/App.xaml.cs`: Updated `CheckForUpdatesAsync` to use a robust polling loop. Exposes available update metadata (version and URL) for use in the Settings window and tray. Updates tray balloon logic.
- `SidebarDiagnostics/Utilities.cs`: Re-wrote `UpdateCheck.CheckAsync` to support `HttpClient` reuse, handle `429`/`403` fallbacks with manual redirects, add `SideMon/<version>` User-Agent, and throttle error logging. Added pure functions for version comparison and tag parsing.
- `SidebarDiagnostics/Monitoring.cs`: Added `ShowExtra` nullable boolean to `HardwareConfig` for the Claude/GPT bar toggle.
- `SidebarDiagnostics/Settings.cs` and `SidebarDiagnostics/SettingsModel.cs`: Added `LastNotifiedUpdate` to persist the update notification state. Added `HasUpdate` and `AvailableUpdateText` for binding update statuses in the Settings UI.
- `SidebarDiagnostics/Settings.xaml` and `SidebarDiagnostics/Settings.xaml.cs`: Implemented the UI for "Claude/GPT bars" in the AI Usage group. Displayed the new update availability info and download/check buttons in the General tab.
- `SidebarDiagnostics/Properties/Resources.resx` and `SidebarDiagnostics/Properties/Resources.Designer.cs`: Added localization strings for update prompts, "AI Usage", and "last known" age labels.
- `SidebarDiagnostics/UsageProviders.cs`: Renamed "agy (Antigravity)" to "Gemini (agy)". Modified Claude and Codex providers to retrieve and surface user subscription plans (`subscriptionType` from local file, `plan_type` from response) in their display name.
- `SidebarDiagnostics/UsageMonitor.cs` and `SidebarDiagnostics/UsageModel.cs`: Added parsing, models, and binding logic for subscription plan injection, stale row dimming (opacity), "last known {time} ago", and skipping reset percentage calculation when it's in the past (instead rendering "resets now - refreshing"). Implemented Claude/GPT bar filtering logic for Gemini.
- `SidebarDiagnostics/UsageParsers.cs`: Extended `ParseCodex` to output the `plan_type` string.
- `SidebarDiagnostics/Sidebar.xaml`: Bound `RowOpacity` for usage metric dimming.
- `CHANGELOG.md` and `SidebarDiagnostics/ChangeLog.json`: Refactored to properly structure version 4.3.0 and the newly bumped 4.3.1.
- `installer/SideMon.iss` and `SidebarDiagnostics/Properties/AssemblyInfo.cs`: Bumped version to 4.3.1.

## What Each Fix Does
1. **Update Notifications**: The update logic no longer just fires at startup. It polls properly on a 6-hour interval, storing available update data statically so that the tray menu and settings window can display an "Update available" state even when the app runs continuously for days. Tray balloon shows reliably, and rate-limiting robustly falls back to parsing the `Location` header on the releases page.
2. **AI CLI Usage Display**: Gives users control over whether to see Claude/GPT metrics on the Antigravity tab using the `ShowExtra` toggle on the Gemini provider. Properly falls back naming schema when the secondary group is toggled off. Renamed labels correctly to "AI Usage" and "Gemini (agy)".
3. **Claude Row False Data**: Replaces false progress percentages with "resets now - refreshing" logic when `ResetsAt` is in the past. Dimmed display for stale responses, including formatting a relative age like "last known 3h ago" to provide better context. Surfaces plan data (e.g., "Claude Code - Pro") directly in the title.
4. **Release Housekeeping**: Version bumps, metadata updates, and a refactored changelog ready for 4.3.1.

## What Could Not Be Verified
- The runtime behavior of the GitHub update polling loop. Although compiling and the static structures look correct, GitHub rate limits or proxy issues were not fully replicated in manual tests.
- UI rendering of the Claude/Codex plan names and specific tray balloon triggers on a prolonged usage session, as this would require mocked `.credentials.json` files and letting the app run for extended periods of time (over 6+ hours) to hit the timer triggers.
