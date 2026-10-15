# Claude Code Access Token Expiry Monitoring

## Task B Verification (Usage Monitoring)

- **Manual Renew**: The `UsageMonitor` triggers `auth status` via `RunCmdAsync` successfully when the user clicks the "Renew" button.
- **Auto-Renew**: Handled gracefully. If `AIUsage_Claude` becomes stale and `AutoRenew` is checked, the `UsageMonitor` will silently poll `claude auth status` in the background (capped at 3 attempts, 30 min cooldown).
- **Shim Testing**: We successfully intercepted `RunCmdAsync` using the test shim at `%LOCALAPPDATA%\Temp\claude_test\claude.cmd`, and confirmed that `auth status` was explicitly invoked as expected. No arbitrary `claude -p "ok"` execution occurs without user interaction.
- **Stale Visuals**: The UI applies a 50% opacity and displays a stale time warning (e.g. "Expired 10m ago") in place of the generic error when the token is known to be recently expired.

**Note**: Real renewal behavior against a genuine Anthropic authentication token has not been verified directly due to environment restrictions (we only asserted the exact CLI invocations sent to the shim). 

## Task C Verification (Hardware Fallback)

- **ArgumentNullException Fallback**: We added a `try/catch` to `MonitorManager` initialization. When `Computer.Open()` fails with `ArgumentNullException("identity")`, it degrades by disabling CPU, GPU, Storage, Motherboard, and Network, leaving only `RAM` and `Battery` enabled.
- **TestHook**: Using `MonitorManager.TestHookForceHardwareFail`, we simulated the crash condition and verified `HardwareLimited` transitions to `true` and the fallback `Computer` is instantiated.
- **UI Warning**: Bound `HardwareLimited` to the visibility of a warning `TextBlock` in `Sidebar.xaml`, styled according to specs (opacity 0.5, wrapped at standard content width, correctly translating via `Resources.resx`).

Code compiles with 0 warnings, 0 errors, and all UI changes map appropriately to `Resources.Designer.cs`.
