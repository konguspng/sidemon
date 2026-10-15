# SideMon - "Sign in again" Button Implementation Report (Task L)

## Summary of Implementation

Implemented the "Sign in again" button on the sidebar AI Usage card and in the Settings window for expired or unauthenticated AI CLI logins (Task L). When an AI provider's session expires or is not logged in (`UsageStatus.AuthExpired` or `UsageStatus.NotLoggedIn`), a subtle icon-only sign-in button appears directly on the provider's header in the sidebar card and next to the Renew button in Settings > Monitors > AI Usage. Clicking the button launches the CLI's login command in a visible terminal window (`wt.exe` or `cmd.exe /k`), switches the status to "Waiting for sign-in...", and fast-polls every 5 seconds until login completes or the terminal exits.

### Key Behaviors Implemented
1. **Sidebar Card Sign-in Button**:
   - Feather `log-in` icon glyph (door with arrow, 10x10 path inside a 14x14 icon button, `IconButton` style).
   - Placed on the provider header line right-aligned (`DockPanel.Dock="Right"`).
   - Controlled by `SignInButtonVisibility` (Visible when `AuthExpired` / `NotLoggedIn` or actively signing in; Collapsed when `Ok` or when disabled by setting).
   - Fully clickable on the `AppBarWindow` (`WS_EX_NOACTIVATE` / `MA_NOACTIVATE`) without stealing keyboard focus from the user's active application; the launched terminal comes to the foreground.
   - Wrapped `ResetText` (`TextWrapping="Wrap"`) ensuring note lines ("Login expired - last updated ... ago. Open Claude Code to renew.") fit cleanly at 230 px sidebar width without horizontal clipping or overflow.

2. **Terminal Launch Machinery (`ILoginLauncher` / `DefaultLoginLauncher`)**:
   - Validates executable paths (`.exe`, `.cmd`, `.bat`) and verifies file existence before execution to prevent shell injection.
   - Inspects environment for Windows Terminal (`wt.exe`), falling back to `cmd.exe /k` to ensure the console stays open after exit so users can inspect errors.
   - Quotes executable paths and arguments safely without shell injection.
   - Commands:
     - **Claude Code**: `claude auth login` (respects `CLAUDE_CONFIG_DIR` and provider's `CustomPath`).
     - **agy (Antigravity CLI)**: Launches `agy` interactively (verified via `agy --help` that no login subcommand exists).
     - **Gemini CLI**: Launches `gemini` with banner `"Please choose 'Login with Google' in Gemini CLI"`.
     - **Codex CLI**: `codex login`.
     - **GitHub Copilot**: `gh auth login`.
   - Single-instance tracking per provider: clicking the button while a login session is already open restores/focuses the existing console window (`ShowWindow(proc.MainWindowHandle, 9)` and `SetForegroundWindow`) rather than spawning duplicate windows.
   - Injectable test hooks: `DefaultLoginLauncher.CustomBuilder` and `DefaultLoginLauncher.ProcessStarter`.

3. **Status Feedback & Polling Loop**:
   - Displays `"Waiting for sign-in..."` in the status row / stale note on the sidebar card and in the Settings row while login is active.
   - During sign-in, fast-polls the provider every 5 seconds (up to 10 minutes maximum).
   - Detects process exit on `cmd.exe` or shims to stop fast-polling and run an immediate final poll.
   - Handles Windows Terminal stub behavior (`wt.exe` exits immediately while `WindowsTerminal.exe` remains open) so fast polling continues until success or timeout.
   - When the poll succeeds (`UsageStatus.Ok`), the button automatically collapses, quotas return, and regular polling resumes.

4. **Settings Dialog**:
   - Added "Sign in again" icon button to Settings > Monitors > AI Usage provider rows (beside the Claude Renew button).
   - Added setting `"Show sign-in button on the sidebar"` (default `true`) under AI Usage Options & Alerts.
   - Preserves compatibility with existing Claude Renew features (Renew is tried first if enabled; sign-in button serves as visible fallback).
   - Seamlessly reconciles and merges loaded `MonitorConfig` parameters in `Settings.Load()` via `MonitorConfig.CheckConfig()`.

5. **Safe Environments & No-Network Guarantees**:
   - Respects `SIDEMON_TEST_DIR` in `Paths.LocalApp` to ensure test harnesses never touch `%LOCALAPPDATA%\SideMon`.
   - `ProviderHelper.Client` is settable for test harnesses to inject mock HTTP handlers and avoid external network requests.
   - English resources localized via `Resources.resx` and strongly typed `Resources.Designer.cs`.

---

## Files Modified

| File | Changes Made |
|---|---|
| `SidebarDiagnostics/Properties/Resources.resx` | Added `UsageSignInTooltip`, `UsageWaitingForSignIn`, `SettingsShowSignInButton`, `SettingsShowSignInButtonTooltip`. |
| `SidebarDiagnostics/Properties/Resources.Designer.cs` | Generated strongly typed resource properties. |
| `SidebarDiagnostics/Commands.cs` | Added `ActionCommand : ICommand` implementation. |
| `SidebarDiagnostics/Windows.cs` | Added P/Invoke signatures `ShowWindow` and `SetForegroundWindow` to `NativeMethods`. |
| `SidebarDiagnostics/Monitoring.cs` | Added `ParamKey.ShowSignInButton`, mapped name/tooltip, added default `true` to `AIUsage` `MonitorConfig`. |
| `SidebarDiagnostics/UsageProviders.cs` | Added `ILoginLauncher`, `DefaultLoginLauncher`, executable path validation, safe CLI command builders, `ProviderHelper.Client` property, and improved `ResolveCustomFile` file name matching. |
| `SidebarDiagnostics/UsageModel.cs` | Added `LoginSession`, `LaunchLogin`, `IsSigningIn`, `GetLoginSession`, `ResetLoginSessions`, `ForceRepoll`, fast polling logic in `PollIfDue`/`PollAsync`, and updated status text in `GetStatusText`. |
| `SidebarDiagnostics/UsageMonitor.cs` | Added `SignInCommand`, `SignInButtonVisibility`, `showSignInButton` parameter, note/suffix updates during sign-in, and property notifications. |
| `SidebarDiagnostics/Sidebar.xaml` | Replaced plain provider title with `DockPanel` containing the right-aligned `IconButton` bound to `SignInCommand` and `SignInButtonVisibility`; added `TextWrapping="Wrap"` to `ResetText`. |
| `SidebarDiagnostics/Settings.xaml` | Added "Sign in again" button next to Claude Renew button in AI Usage hardware rows. |
| `SidebarDiagnostics/Settings.xaml.cs` | Added `HardwareSignIn_Click` event handler triggering `UsageRegistry.LaunchLogin()`. |
| `SidebarDiagnostics/Settings.cs` | Updated `Settings.Load()` to merge loaded `MonitorConfig` via `MonitorConfig.CheckConfig()` so new parameters are populated. |
| `SidebarDiagnostics/SettingsModel.cs` | Set `RenewStatus` to `UsageWaitingForSignIn` during active sign-in. |
| `SidebarDiagnostics/Utilities.cs` | Added `SIDEMON_TEST_DIR` environment variable check in `Paths.LocalApp` to isolate test harnesses from owner settings. |

---

## Verification Executed

### 1. Fake `claude.cmd` Shim Test (`test_login_feat`)
- **Setup**:
  - Isolated test directory `%LOCALAPPDATA%\Temp\test_login_feat`.
  - Fake `claude.cmd` logging all arguments to `%~dp0shim_args.log`.
  - Fake `CLAUDE_CONFIG_DIR` containing `.credentials.json` with token `"sk-ant-test-expired-token-12345"` and `expiresAt: 1000` (in the past).
  - Mock HTTP client injecting 200 OK usage responses (zero external network requests).
- **Execution**:
  - `BuildProcessStartInfo` produced safe command: `wt.exe --title "SideMon - Claude sign-in" cmd.exe /k "...claude.cmd auth login"`.
  - Step 1: Initial poll returned `AuthExpired`. `SignInButtonVisibility` became `Visible`.
  - Step 2: First click (`LaunchSignIn()`) launched `claude.cmd auth login` (`processLaunchCount == 1`). `UsageRegistry.IsSigningIn` became `true`. Status row changed to `"Waiting for sign-in..."`.
  - Step 3: Second click while active was blocked from spawning duplicate process (`processLaunchCount` remained 1).
  - Step 4: Shim finished, credentials updated to future expiry. Next poll succeeded (`UsageStatus.Ok`). Button hid (`SignInButtonVisibility == Collapsed`), `IsSigningIn` became `false`, quotas returned (25%, 48%).
  - Step 5: `shim_args.log` verified to contain exactly one line: `auth login`.
  - Step 6: Security scan: grepped all log files and console for `"sk-ant-test-expired-token-12345"`. Confirmed 0 occurrences in logs/console (found only in test script and `.credentials.json`).

### 2. Rendered Sidebar Usage Card at 230 px (`render_login`)
Rendered the card inside a 230 px host container and inspected each PNG:
- **State 1 (OK)**:
  - File: `C:\Users\kong\AppData\Local\Temp\card_ok.png` (230x186)
  - Result: Quota windows visible (5-hour 25%, Weekly 48%), reset countdowns visible, sign-in button hidden.
- **State 2 (Expired with Sign-in Button)**:
  - File: `C:\Users\kong\AppData\Local\Temp\card_expired.png` (230x218)
  - Result: Sign-in icon button visible in header, quotas at 50% opacity, note lines ("Login expired - last updated just now ago. Open Claude Code to renew.") wrap neatly at 230 px.
- **State 3 (Waiting for sign-in)**:
  - File: `C:\Users\kong\AppData\Local\Temp\card_waiting.png` (230x186)
  - Result: Sign-in icon button visible, note line shows `"Waiting for sign-in..."` under load bars.

### 3. Rendered Settings Window (`render_login`)
- File: `C:\Users\kong\AppData\Local\Temp\st_ai_feat.png` (1152x877)
- Result:
  - Providers list shows both the Renew button and Sign-in button side-by-side for Claude Code.
  - Sign-in buttons present for agy, Codex, Copilot, and Gemini CLI rows.
  - Live status displays `"Waiting for sign-in..."`.
  - Options & Alerts section displays `"Show sign-in button on the sidebar"` checkbox (checked by default).

### 4. WPF NOACTIVATE Window Click Test
- Executed `TestNoActivate.cs` on a window with `WM_MOUSEACTIVATE` hook returning `MA_NOACTIVATE` (3).
- Confirmed that button click and `ActionCommand` execute normally without stealing focus.

### 5. Build Verification
- Command: `MSBuildEnableWorkloadResolver=false dotnet build SidebarDiagnostics/SidebarDiagnostics.csproj -c Release -o "%LOCALAPPDATA%\Temp\verifybuild_feat"`
- Result: **0 Warning(s)**, **0 Error(s)**.

---

## Real CLI `--help` Inspection Findings
- **Claude Code**: `claude auth login` confirmed to exist.
- **Antigravity CLI (`agy`)**: Inspected `agy --help`. Subcommands available are: `agent`, `agents`, `changelog`, `help`, `install`, `mcp`, `mic-serve`, `models`, `plugin`, `plugins`, `remote-control`, `update`. No login/auth subcommand exists; interactive launch (`agy`) is used.
- **GitHub CLI (`gh` / Copilot)**: `gh auth login` flags verified (`-w, --web`, etc.).
- **Gemini CLI (`gemini`)**: Not installed on this PC. Configured to launch with interactive guidance banner.
- **Codex CLI (`codex`)**: Not installed on this PC. Configured to launch `codex login`.

---

## Explicit Manual Test Steps for Owner
To test the live sidebar button interactively:
1. Start SideMon with AI Usage monitor enabled (Claude Code provider ticked).
2. Intentionally expire the Claude token or disconnect OAuth in `%USERPROFILE%\.claude\.credentials.json` (e.g. set `"expiresAt": 1000`).
3. Observe the sidebar card: after the next refresh tick, the card dims to stale opacity, shows "Login expired", and the "Sign in again" button appears in the top-right of the Claude header.
4. Click the "Sign in again" button on the sidebar:
   - Notice the sidebar does not steal focus from your active foreground application.
   - A new visible terminal window titled "SideMon - Claude sign-in" opens running `claude auth login`.
   - The card status changes to "Waiting for sign-in...".
   - Clicking the button a second time brings the existing terminal window to the front without opening a second terminal.
5. Complete sign-in in the terminal.
6. Within 5 seconds of completion, the sidebar status flips back to OK, the "Sign in again" button disappears, and active quota readings return.

---

## Not Verified List
1. Live network interactive OAuth sign-in against real Anthropic / Google accounts (intentionally avoided to protect owner's active sessions and avoid external network traffic).
2. Physical launch of `gemini` and `codex` CLIs on this system (neither CLI is currently installed on this machine).
3. Multi-monitor mixed DPI dragging of the terminal window across monitors with different scale factors.
