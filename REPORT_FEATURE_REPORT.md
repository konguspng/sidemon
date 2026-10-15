# Report Feature Implementation

## Changes Made
1. **ReportService**: Added `ReportService.cs` containing `BuildReport()` (compiles version, OS, .NET version, and up to 40,000 chars of the error log tail), `Scrub()` (removes `Environment.UserName`, `Environment.MachineName`, Windows file paths, known token patterns, emails, IPv4 addresses, and custom paths from `UsageRegistry.Providers`), and `Send()` (HTTP POST to `https://pishonstudio.com/sidemon/report.php` with 20s timeout and exact headers).
2. **Crash Handler**: Updated `AppDomain_Error` in `App.xaml.cs`. `ErrorLog.Write(ex)` remains first. A new modal `MessageBox` prompts the user. If they accept, `ReportService.Send()` is run synchronously, and a 15-second cap is enforced via `Wait(15000)`. If successful, a thank-you box is shown. Removed the previous `Process.Start` code that opened `error.log`. The process is explicitly closed via `Environment.Exit(1)` after all flows.
3. **Settings UI**: Added a `Support` section in `Settings.xaml` under the General tab (below Advanced Driver Options). This includes a "Send log to developer..." button bound to `SendLog_Click`. Added `IsReportFeatureAvailable` property to `SettingsModel`.
4. **Preview Window**: Created `ReportPreviewWindow.xaml` and `.cs` matching the dark flat UI requirements. Bound the cancel button to Esc, and the send button triggers async sending with UI progression. 
5. **Secret Handling**: Created `ReportSecret.cs` containing the secret read from `%LOCALAPPDATA%\Temp\relay_secret.txt`. Created `ReportSecret.cs.example`. Added `ReportSecret.cs` to `.gitignore`. Added `#if HAS_REPORT_SECRET` conditions in `ReportService` and `SettingsModel`, and modified `SidebarDiagnostics.csproj` to define the constant if the file exists.

## Verification Done
- **Unit checks**: Created a test harness project `wd` to test `Scrub()`. Validated that it scrubs paths, username, machine name, tokens, IPs, and emails correctly while keeping the rest readable. Asserted that `BuildReport()` limits the report size properly.
- **Send() tests**: The `wd` harness instantiated a local `HttpListener` fake server. Validated exact JSON, headers, timeout handling, and no-throw semantics, correctly handling fake 200 and 403 responses.
- **Crash Path**: Called the crash handler logic from a ThreadPool thread, which correctly showed the MessageBox with the 12-line preview and executed the network call without freezing or depending on the WPF Dispatcher.
- **grep proof**: Searched for `Paths.ErrorLogFile`, `Process.Start`, and `"error.log"`. The only remaining uses of `ErrorLogFile` are `ErrorLog.Write()` and `ReportService` reading the file. I verified that no tray menu or `Settings.xaml.cs` buttons open the file anymore.
- **Build**: Successfully ran `dotnet build` with zero errors and zero warnings.
- **Preview UI Render**: The `ReportPreviewWindow` was rendered to a PNG and visually confirmed to meet the styling requirements. The crash message text was also dumped and verified to contain the correct 12-line preview.

## Verification Unable to Complete
- **Settings General Tab Render**: I could not render the Settings window to a PNG via the throwaway test harness because I accidentally overwrote the initial `Program.cs` that `render_settings.py` relied on, and rendering `Settings` directly in a custom thread requires the global `FlatWindowStyle` and `App.xaml` resources which fail to load in a standalone window unit test. The XAML structural changes were, however, confirmed by manual inspection and compilation.
