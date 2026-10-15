# AI CLI Usage Monitors — Independent Bug Review

This review covers the new "AI CLI Usage monitors" feature: `SidebarDiagnostics/UsageModel.cs`, `UsageMonitor.cs`, `UsageParsers.cs`, `UsageProviders.cs`, and corresponding modifications in `Monitoring.cs`, `SettingsModel.cs`, `Settings.xaml`, and `Properties/Resources.resx`.

---

## 1. Thread Safety (UsageRegistry state vs UI thread)

### Finding 1.1: Lock contention and blocking across `_detectLock` in `CopilotProvider`
- **File:Line**: [`SidebarDiagnostics/UsageProviders.cs:394`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L394), [`SidebarDiagnostics/UsageProviders.cs:495`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L495)
- **Severity**: Medium
- **What breaks**: `CopilotProvider.Detect()` acquires `_detectLock` and executes `ReadToken()`, which starts an external child process (`gh auth token`) and waits up to 10 seconds (`WaitForExit`) plus 2 seconds (`outTask.Wait`). While this long-running external process runs under `_detectLock`, any thread (such as the UI thread or background thread) setting `CustomPath` (`UsageProviders.cs:394`) blocks waiting for `_detectLock`. This causes UI freezes when configuring settings.
- **Concrete fix**: Do not hold `_detectLock` across child process execution or remove process execution from `Detect()` entirely (see Finding 3.1). Ensure locks only protect quick in-memory state updates.

### Finding 1.2: Unsynchronized mutation of `_provider.CustomPath` in `UsageRegistry.SetCustomPath`
- **File:Line**: [`SidebarDiagnostics/UsageModel.cs:196`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageModel.cs#L196)
- **Severity**: Low
- **What breaks**: `_provider.CustomPath = _new;` is assigned outside the `lock (_lock)` block while `CustomPath` is read asynchronously on background worker threads in `FetchAsync()` and `FindTokenPath()`. While string reference writes are atomic in .NET, a volatile or synchronized access ensures consistent visibility across threads.
- **Concrete fix**: Synchronize `CustomPath` assignment under `_lock` or declare backing fields `volatile`.

---

## 2. Leaked Processes / Handles / HttpClient

### Finding 2.1: Leaked `agy` child process on cancellation or unexpected exceptions
- **File:Line**: [`SidebarDiagnostics/UsageProviders.cs:245-265`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L245-L265)
- **Severity**: High
- **What breaks**: In `AgyProvider.FetchAsync`, the process is started with `using var process = Process.Start(psi);`. In .NET, `Process.Dispose()` only releases the local process handle—it does **not** terminate the underlying operating system process. The call to `process.Kill(true)` is placed solely inside `catch (OperationCanceledException)`. If any other exception occurs (I/O error, pipe error, or unhandled exception during `await outputTask`), or if the outer caller cancels via a token that faults differently, `process.Kill(true)` is skipped. The `agy.exe` / `agy.cmd` process is orphaned and remains running in the background indefinitely, accumulating zombie processes.
- **Concrete fix**: Wrap the execution in a `try / finally` block that guarantees process termination if it has not exited:
  ```csharp
  try
  {
      process.StandardInput.Close();
      var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
      var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
      await process.WaitForExitAsync(cts.Token);
      output = await outputTask;
  }
  finally
  {
      if (!process.HasExited)
      {
          try { process.Kill(true); } catch { }
      }
  }
  ```

### Finding 2.2: Fire-and-forget stderr reader task on disposed `CancellationTokenSource`
- **File:Line**: [`SidebarDiagnostics/UsageProviders.cs:252`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L252)
- **Severity**: Low
- **What breaks**: `_ = process.StandardError.ReadToEndAsync(cts.Token);` starts an unawaited task registered with `cts.Token`. When `FetchAsync` exits, `using var cts` disposes the `CancellationTokenSource`. If the unawaited stderr task is still running or its cancellation callback fires against a disposed CTS, it can throw `ObjectDisposedException` or trigger unobserved task exceptions.
- **Concrete fix**: Await both standard output and standard error readers together with `await Task.WhenAll(outputTask, errorTask);`.

---

## 3. Deadlocks (Process pipes, Task.Wait on UI thread: Detect() is called from Settings UI)

### Finding 3.1: Synchronous `gh auth token` execution and `Task.Wait()` on UI thread in `Detect()`
- **File:Line**: [`SidebarDiagnostics/UsageProviders.cs:464-471`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L464-L471), [`SidebarDiagnostics/UsageProviders.cs:484`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L484), [`SidebarDiagnostics/UsageProviders.cs:506`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L506)
- **Severity**: High
- **What breaks**: `CopilotProvider.Detect()` is called directly on the WPF UI thread when opening the Settings window via `SettingsModel` ([`SettingsModel.cs:140`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/SettingsModel.cs#L140)) and `UsageRegistry.GetHardware()` ([`UsageModel.cs:212`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageModel.cs#L212)). In `CopilotProvider.Detect()`, when local config files are absent, it calls `ReadToken()`, which calls `RunGhAuthToken()`.
  Inside `RunGhAuthToken()`:
  1. `proc.WaitForExit((int)GhTimeout.TotalMilliseconds)` blocks the WPF UI thread for up to 10 seconds.
  2. `outTask = proc.StandardOutput.ReadToEndAsync()` creates a Task on the UI thread.
  3. `outTask.Wait(2000)` and `outTask.Result` synchronously wait for that Task on the UI thread. If the asynchronous stream read attempts to post a continuation back to the WPF `DispatcherSynchronizationContext`, it will deadlock. Even if it avoids deadlock, opening the Settings window freezes the entire UI for up to 12 seconds if `gh` prompts, waits for keychain access, or is slow.
  4. It violates the documented contract of `IUsageProvider.Detect()` ([`UsageModel.cs:98-99`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageModel.cs#L98-L99)): "installed + signed in, determined locally without any network call".
- **Concrete fix**: Never execute external child processes or call `Task.Wait()` on the UI thread inside `Detect()`. Keep `Detect()` strictly local (file existence and PATH checks only, e.g. checking `ReadFileToken() != null || ProviderHelper.IsOnPath("gh")`). Move `RunGhAuthToken()` execution exclusively to `FetchAsync()`, which runs asynchronously on background threadpool workers.

---

## 4. Exceptions That Can Escape to the UI

### Finding 4.1: `Environment.ExpandEnvironmentVariables(null)` in `ProviderHelper.ResolvePath`
- **File:Line**: [`SidebarDiagnostics/UsageProviders.cs:16-19`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L16-L19)
- **Severity**: Low
- **What breaks**: `ProviderHelper.ResolvePath` passes its argument directly to `Environment.ExpandEnvironmentVariables(path)`. In .NET, passing `null` throws an unhandled `ArgumentNullException`. If `ProviderHelper.FileExists(null)` is called (e.g. from an uninitialized path parameter), the exception will propagate and can crash the caller.
- **Concrete fix**: Guard against null:
  ```csharp
  public static string ResolvePath(string path)
  {
      return string.IsNullOrEmpty(path) ? string.Empty : Environment.ExpandEnvironmentVariables(path);
  }
  ```

---

## 5. Credentials Ever Logged / Returned / Shown

- **Status**: **none found**
- **Analysis**:
  - OAuth bearer tokens and account IDs are used strictly in `HttpRequestMessage.Headers` in memory.
  - No credential values are included in `UsageResult`, `UsageWindow`, or `UsageSnapshot`.
  - No tokens are written to `ErrorLog` or stdout/stderr.
  - File paths in `CustomPath` are displayed in the Settings UI, but token strings themselves are never exposed or rendered.

---

## 6. Wrong Time / Percent Math

- **Status**: **none found**
- **Analysis**:
  - `UsageParsers.Clamp()` correctly handles `NaN` and bounds values to `[0, 100]`.
  - Agy's calculation (`100d - _remaining`) properly converts remaining percentage to used percentage.
  - Copilot's calculation `(_entitlement - _remaining) / _entitlement * 100d` checks `_entitlement <= 0` prior to division, preventing division by zero.
  - Copilot free quota calculation `(_total - _left) / _total * 100d` guards `_total <= 0`.
  - `ClassifySeconds()` correctly classifies 5-hour (`<= 6h`), weekly (`>= 6d`), and monthly (`>= 25d`) limit windows.

---

## 7. DST / Timezone Mistakes in Reset Countdown

- **Status**: **none found**
- **Analysis**:
  - All date parsers (`ParseDate`, `ParseUnixOrDate`) parse incoming timestamps into `DateTimeOffset` using `DateTimeStyles.AssumeUniversal`.
  - `FormatReset()` computes the countdown via `resetsAt.Value - DateTimeOffset.Now`. Because `DateTimeOffset - DateTimeOffset` calculates the exact difference in UTC ticks, the countdown is completely immune to local Daylight Saving Time (DST) shifts or timezone offset differences.

---

## 8. JSON Edge Cases

### Finding 8.1: Numeric `quota_reset_date_utc` dropped in `UsageParsers.ParseCopilot`
- **File:Line**: [`SidebarDiagnostics/UsageParsers.cs:303-306`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageParsers.cs#L303-L306)
- **Severity**: Medium
- **What breaks**: In `ParseCopilot`:
  ```csharp
  DateTimeOffset? _reset =
      ParseDate(GetString(_root, "quota_reset_date_utc")) ??
      ParseDate(GetString(_root, "quota_reset_date")) ??
      ParseDate(GetString(_root, "limited_user_reset_date"));
  ```
  `GetString()` returns `null` unless `_value.ValueKind == JsonValueKind.String`. If GitHub's internal API returns `quota_reset_date_utc` or `quota_reset_date` as a Unix epoch number (integer seconds or milliseconds), `GetString()` returns `null`, and `_reset` becomes `null`. As a result, the reset countdown is completely missing from the UI.
  (In contrast, `ParseCodex` correctly uses `ParseUnixOrDate()` to support both number and string representations).
- **Concrete fix**: Use `ParseUnixOrDate()` instead of `GetString` + `ParseDate`:
  ```csharp
  DateTimeOffset? _reset =
      (_root.TryGetProperty("quota_reset_date_utc", out var r1) ? ParseUnixOrDate(r1) : null) ??
      (_root.TryGetProperty("quota_reset_date", out var r2) ? ParseUnixOrDate(r2) : null) ??
      (_root.TryGetProperty("limited_user_reset_date", out var r3) ? ParseUnixOrDate(r3) : null);
  ```

---

## 9. Settings Load / Save / Merge Errors

### Finding 9.1: `HardwareConfig.CustomPath` does not implement `INotifyPropertyChanged`, breaking dirty tracking and Cancel
- **File:Line**: [`SidebarDiagnostics/Monitoring.cs:2803`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/Monitoring.cs#L2803)
- **Severity**: High
- **What breaks**: In `HardwareConfig`, `CustomPath` was added as an auto-property:
  ```csharp
  public string CustomPath { get; set; }
  ```
  unlike `Enabled`, `Name`, `ID`, etc., which invoke `NotifyPropertyChanged(...)`.
  In `SettingsModel.cs:1321`, `_hardware.PropertyChanged += Child_PropertyChanged;` listens to hardware changes to set `IsChanged = true`.
  Because `CustomPath` does not raise `PropertyChanged`:
  1. When the user modifies `CustomPath` in the Settings dialog, `SettingsModel.IsChanged` remains `false`.
  2. The "Apply" button stays disabled.
  3. The Close/Cancel button does not change its label to "Cancel".
  4. If the user edits `CustomPath` and clicks "Close", `CloseButton_Click` checks `if (Model.IsChanged)`, which is `false`, and immediately closes the window without saving or offering to revert, discarding the user's edits.
- **Concrete fix**: Convert `CustomPath` to a full property with a backing field that calls `NotifyPropertyChanged("CustomPath")`:
  ```csharp
  private string _customPath;
  public string CustomPath
  {
      get => _customPath;
      set
      {
          if (!string.Equals(_customPath, value, StringComparison.Ordinal))
          {
              _customPath = value;
              NotifyPropertyChanged("CustomPath");
          }
      }
  }
  ```

### Finding 9.2: Settings UI does not refresh `UsageRegistry.SetCustomPath` or `Status` when `CustomPath` is edited
- **File:Line**: [`SidebarDiagnostics/SettingsModel.cs:140`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/SettingsModel.cs#L140)
- **Severity**: Medium
- **What breaks**: `UsageRegistry.SetCustomPath(newhw.ID, newhw.CustomPath); newhw.Status = UsageRegistry.GetStatusText(newhw.ID);` is called only once during initial construction of `SettingsModel`. When the user edits `CustomPath` in the Settings TextBox, `UsageRegistry.SetCustomPath` is never invoked, and `newhw.Status` is never updated. Even if the user enters a valid path to their credentials, the "Status" column in Settings remains stuck at "Not logged in" or "Not installed" until the application is restarted.
- **Concrete fix**: In `SettingsModel`, subscribe to `PropertyChanged` on `HardwareConfig` and, when `propertyName == "CustomPath"` for an `AIUsage` monitor, invoke `UsageRegistry.SetCustomPath(hw.ID, hw.CustomPath)` and update `hw.Status = UsageRegistry.GetStatusText(hw.ID)`.

### Finding 9.3: Identical default `Order` between `Battery` and `AIUsage`
- **File:Line**: [`SidebarDiagnostics/Monitoring.cs:2664`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/Monitoring.cs#L2664), [`SidebarDiagnostics/Monitoring.cs:2685`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/Monitoring.cs#L2685)
- **Severity**: Low
- **What breaks**: In `MonitorConfig.Default`, `Battery` has `Order = 0` and `AIUsage` also has `Order = 0`. When loading an older `settings.json`, `AIUsage` is merged with `Order = 0`. Because both monitors share the exact same order value, ordering in `OrderByDescending(c => c.Order)` relies on secondary sort stability, which can cause inconsistent panel ordering between runs.
- **Concrete fix**: Assign distinct default order values (or shift existing orders).

---

## 10. Metric Rebuild / Dispose Races (Rows disposed while WPF still binds)

### Finding 10.1: Synchronous disposal of old metrics on worker thread while WPF UI thread is still binding
- **File:Line**: [`SidebarDiagnostics/UsageMonitor.cs:187-193`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageMonitor.cs#L187-L193)
- **Severity**: High
- **What breaks**: `UsageMonitor.Update()` runs on a background thread pool thread (`MonitorTimer_Elapsed`). When the quota signature changes (such as transitioning from the initial `"status"` row to active quota rows after the first poll, or when window sets change):
  ```csharp
  Metrics = _currentRows.Cast<iMetric>().ToArray();

  foreach (iMetric _metric in _old)
  {
      _metric.Dispose();
  }
  ```
  Setting `Metrics` dispatches `NotifyPropertyChanged("Metrics")` to the UI thread asynchronously via `Application.Current.Dispatcher.BeginInvoke`.
  Immediately afterwards, on the background worker thread, `_metric.Dispose()` runs on every metric in `_old`.
  WPF's `ItemsControl` on the UI thread is still actively rendering, evaluating bindings, or processing queued property notifications on the objects in `_old`.
  In `BaseMetric.Dispose()`:
  - `_disposed` is set to `true`.
  - `_converter` is set to `null`.
  - `AlertBlinker.Unsubscribe(this)` runs concurrently.
  If WPF accesses `_converter`, or if an `AlertBlinker` tick fires on the UI thread before the Dispatcher processes the collection change, WPF operates on disposed objects, leading to race conditions and potential `NullReferenceException`.
- **Concrete fix**: Do not dispose `_old` synchronously on the background worker thread. Instead, marshal disposal of `_old` to the UI thread at low priority (e.g. `DispatcherPriority.ApplicationIdle`), ensuring disposal happens only after WPF has unbound from the old metric instances:
  ```csharp
  Metrics = _currentRows.Cast<iMetric>().ToArray();

  if (_old != null && _old.Length > 0)
  {
      App.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(() =>
      {
          foreach (iMetric _metric in _old)
          {
              _metric.Dispose();
          }
      }));
  }
  ```

---

## 11. Settings.xaml Binding Mistakes

### Finding 11.1: `DataTrigger Binding="{Binding Path=Type}" Value="AIUsage"` fails to match enum `MonitorType.AIUsage`
- **File:Line**: [`SidebarDiagnostics/Settings.xaml:396`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/Settings.xaml#L396), [`SidebarDiagnostics/Settings.xaml:453`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/Settings.xaml#L453)
- **Severity**: High
- **What breaks**: In WPF XAML, `DataTrigger.Value` has type `object`. Because the `DataTemplate` on `DataGrid.RowDetailsTemplate` does not specify a static `DataType`, the XAML parser has no type hint and assigns `Value = "AIUsage"` as a `System.String`.
  At runtime, `MonitorConfig.Type` returns an enum instance of type `MonitorType.AIUsage`.
  WPF compares `(string)"AIUsage"` with `(MonitorType)MonitorType.AIUsage`. The equality check evaluates to `false`.
  Consequently:
  1. The StackPanel containing the AI Usage provider grid (with Provider, Custom Path, and Status columns) remains `Collapsed` and NEVER becomes visible.
  2. The generic hardware ListView (which only has Checkbox and Name columns) is NEVER collapsed.
  The entire AI CLI Usage provider configuration grid is completely broken and invisible in the Settings window.
- **Concrete fix**: Use `{x:Static monitor:MonitorType.AIUsage}` in both DataTriggers:
  ```xml
  <DataTrigger Binding="{Binding Path=Type}" Value="{x:Static monitor:MonitorType.AIUsage}">
  ```

### Finding 11.2: `HardwareConfig.Status` does not implement `INotifyPropertyChanged`
- **File:Line**: [`SidebarDiagnostics/Monitoring.cs:2800`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/Monitoring.cs#L2800), [`SidebarDiagnostics/Settings.xaml:430`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/Settings.xaml#L430)
- **Severity**: Medium
- **What breaks**: `Settings.xaml` binds the Status column to `HardwareConfig.Status` via:
  ```xml
  <TextBlock Text="{Binding Path=Status, Mode=OneWay}" VerticalAlignment="Center" Margin="12,0,0,0" />
  ```
  Because `HardwareConfig.Status` is an auto-property without `NotifyPropertyChanged("Status")`, any background detection update or path-revalidation will never update the TextBlock in the UI.
- **Concrete fix**: Implement `NotifyPropertyChanged("Status")` in `HardwareConfig.Status`.

---

## 12. Polling Frequency Violations (>= 5 min per provider, none when disabled)

### Finding 12.1: Server HTTP 429 `Retry-After` capped to `MaxBackoff` (60 minutes)
- **File:Line**: [`SidebarDiagnostics/UsageModel.cs:305-308`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageModel.cs#L305-L308)
- **Severity**: Medium
- **What breaks**: In `UsageRegistry.PollAsync`:
  ```csharp
  if (_result.RetryAfter.HasValue && _result.RetryAfter.Value > _delay)
  {
      _delay = _result.RetryAfter.Value;
  }

  if (_delay > MaxBackoff)
  {
      _delay = MaxBackoff;
  }
  ```
  If an API responds with HTTP 429 and a `Retry-After` header indicating a cooldown longer than 60 minutes (such as 2 hours / 7200 seconds), `_delay` is capped down to `MaxBackoff` (60 minutes). SideMon will poll the provider 60 minutes before the rate-limit window resets, violating the server's explicit backoff instruction.
- **Concrete fix**: Do not clamp `_delay` to `MaxBackoff` when `RetryAfter` is explicitly provided by the server:
  ```csharp
  if (_result.RetryAfter.HasValue)
  {
      _delay = _result.RetryAfter.Value;
  }
  else if (_delay > MaxBackoff)
  {
      _delay = MaxBackoff;
  }
  ```

### Finding 12.2: Disabled Copilot CLI checked via external process during Settings load
- **File:Line**: [`SidebarDiagnostics/UsageProviders.cs:484`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/UsageProviders.cs#L484), [`SidebarDiagnostics/SettingsModel.cs:140`](file:///K:/Work/vibe%20code/sidebar%20diag/sidemon/SidebarDiagnostics/SettingsModel.cs#L140)
- **Severity**: Medium
- **What breaks**: While background sensor polling (`PollIfDue`) strictly respects `HardwareConfig.Enabled` (only enabled providers receive a `UsageMonitor`), opening the Settings window triggers `GetStatusText(newhw.ID)` for all providers unconditionally. For Copilot, this spawns `gh auth token` even if Copilot has never been enabled by the user.
- **Concrete fix**: Eliminating external process invocation from `Detect()` (as detailed in Finding 3.1) ensures no CLI commands run for disabled providers.

---

## Summary of Findings by Severity

| Severity | ID | Category | File:Line | Description |
| :--- | :--- | :--- | :--- | :--- |
| **High** | 2.1 | Leaked Processes | `UsageProviders.cs:245-265` | Orphaned `agy` child process leaked on cancellation / exceptions |
| **High** | 3.1 | Deadlocks | `UsageProviders.cs:464-471` | Synchronous `gh auth token` execution and `Task.Wait()` on UI thread |
| **High** | 9.1 | Settings Merge | `Monitoring.cs:2803` | `CustomPath` missing `INotifyPropertyChanged` breaking dirty tracking & Cancel |
| **High** | 10.1 | Metric Rebuild | `UsageMonitor.cs:187-193` | Synchronous disposal of old metrics on worker thread while WPF still binds |
| **High** | 11.1 | Binding Mistakes | `Settings.xaml:396, 453` | `DataTrigger Value="AIUsage"` fails to match enum `MonitorType.AIUsage` |
| **Med** | 1.1 | Thread Safety | `UsageProviders.cs:394, 495` | `_detectLock` held across long child process execution blocking `CustomPath` |
| **Med** | 8.1 | JSON Edge Cases | `UsageParsers.cs:303-306` | Numeric `quota_reset_date_utc` dropped in `ParseCopilot` |
| **Med** | 9.2 | Settings Merge | `SettingsModel.cs:140` | Status and `SetCustomPath` not updated when user edits `CustomPath` |
| **Med** | 11.2 | Binding Mistakes | `Monitoring.cs:2800` | `HardwareConfig.Status` missing `INotifyPropertyChanged` |
| **Med** | 12.1 | Polling Frequency | `UsageModel.cs:305-308` | HTTP 429 `Retry-After` clamped to `MaxBackoff`, violating server backoff |
| **Med** | 12.2 | Polling Frequency | `UsageProviders.cs:484` | External `gh` process launched for disabled providers on Settings open |
| **Low** | 1.2 | Thread Safety | `UsageModel.cs:196` | Unsynchronized `CustomPath` assignment in `SetCustomPath` |
| **Low** | 2.2 | Leaked Handles | `UsageProviders.cs:252` | Fire-and-forget stderr reader task on disposed CTS in `AgyProvider` |
| **Low** | 4.1 | UI Exceptions | `UsageProviders.cs:16-19` | `Environment.ExpandEnvironmentVariables(null)` in `ResolvePath` |
| **Low** | 9.3 | Settings Merge | `Monitoring.cs:2664, 2685` | Identical default `Order = 0` between `Battery` and `AIUsage` |
