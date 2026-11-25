using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using SidebarDiagnostics.Monitoring;
using SidebarDiagnostics.Utilities;
using SidebarDiagnostics.Windows;

namespace SidebarDiagnostics
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private System.Threading.Mutex _instanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _instanceMutex = new System.Threading.Mutex(true, "Local\\SideMon_InstanceGuard", out bool createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // BOOT HARDENING: wait for the shell/disk after a fresh boot, then pin lazy DLLs
            Resilience.WaitForBootSettle();
            Resilience.PreloadRuntime();

            // ERROR HANDLING
            #if !DEBUG
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(AppDomain_Error);
            DispatcherUnhandledException += (s, ev) => { if (Resilience.IsAssemblyLoadFailure(ev.Exception)) { ErrorLog.Write(ev.Exception); ev.Handled = true; return; } AppDomain_Error(s, new UnhandledExceptionEventArgs(ev.Exception, true)); ev.Handled = true; };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ev) => { AppDomain_Error(s, new UnhandledExceptionEventArgs(ev.Exception, true)); ev.SetObserved(); };
            #endif

            // LANGUAGE
            Culture.SetDefault();
            Culture.SetCurrent(true);

            // SETTINGS
            CheckSettings();

            // VERSION
            Version _version = Assembly.GetExecutingAssembly().GetName().Version;
            string _vstring = _version.ToString(3);

            // TRAY ICON
            TrayIcon = (TaskbarIcon)FindResource("TrayIcon");
            TrayIcon.ToolTipText = string.Format("{0} v{1}", Framework.Resources.AppName, _vstring);
            TrayIcon.TrayContextMenuOpen += TrayIcon_TrayContextMenuOpen;
            TrayIcon.TrayBalloonTipClicked += TrayIcon_BalloonClicked;

            // UPDATE CHECK
            if (Framework.Settings.Instance.CheckForUpdates)
            {
                _ = StartUpdateCheckLoop();
            }

            // START APP
            if (Framework.Settings.Instance.InitialSetup)
            {
                new Setup();
            }
            else
            {
                StartApp(false);
            }

            // SENSOR DRIVER: checked shortly after the app is visible, so a fresh
            // install never looks frozen while this runs. This used to be scheduled
            // at DispatcherPriority.ApplicationIdle, but sensor polling continuously
            // enqueues UI-bound property-change updates, so the dispatcher queue can
            // stay non-empty indefinitely and ApplicationIdle work never actually
            // gets a turn; a short off-thread delay plus a Normal-priority dispatch
            // is not subject to that starvation.
            _ = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() =>
                {
                    _ = CheckPawnIOAsync();
                }));
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _updateLoopRunning = false;

            // a second instance shuts down in OnStartup before the tray icon exists
            TrayIcon?.Dispose();

            base.OnExit(e);

            // skip the native module uninitializers (vcruntime140_cor3 load error on exit)
            Resilience.FastExit(e.ApplicationExitCode);
        }

        public static void StartApp(bool openSettings)
        {
            Version _version = Assembly.GetExecutingAssembly().GetName().Version;
            string _vstring = _version.ToString(3);

            if (!string.Equals(Framework.Settings.Instance.ChangeLog, _vstring, StringComparison.OrdinalIgnoreCase))
            {
                Framework.Settings.Instance.ChangeLog = _vstring;
                Framework.Settings.Instance.Save();

                new ChangeLog(_version).Show();
            }

            new Sidebar(openSettings, Framework.Settings.Instance.InitiallyHidden).Show();

            RefreshIcon();
            RefreshFpsOverlay();
        }

        public static void RefreshIcon()
        {
            TrayIcon.Visibility = Framework.Settings.Instance.ShowTrayIcon ? Visibility.Visible : Visibility.Collapsed;
        }

        public static void RefreshTrayMenu()
        {
            if (TrayIcon?.ContextMenu == null) return;

            var menu = TrayIcon.ContextMenu;
            if (menu.Items.Count > 0 && menu.Items[0] is MenuItem miSettings)
                miSettings.Header = SidebarDiagnostics.Framework.Resources.ButtonSettings;
            if (menu.Items.Count > 1 && menu.Items[1] is MenuItem miReload)
                miReload.Header = SidebarDiagnostics.Framework.Resources.ButtonReload;
            if (menu.Items.Count > 3 && menu.Items[3] is MenuItem miGraph)
                miGraph.Header = SidebarDiagnostics.Framework.Resources.ButtonGraph;
            if (menu.Items.Count > 6 && menu.Items[6] is MenuItem miVis)
            {
                miVis.Header = SidebarDiagnostics.Framework.Resources.ButtonVisibility;
                if (miVis.Items.Count > 0 && miVis.Items[0] is MenuItem miShow)
                    miShow.Header = SidebarDiagnostics.Framework.Resources.ButtonShow;
                if (miVis.Items.Count > 1 && miVis.Items[1] is MenuItem miHide)
                    miHide.Header = SidebarDiagnostics.Framework.Resources.ButtonHide;
            }
            if (menu.Items.Count > 9 && menu.Items[9] is MenuItem miGit)
                miGit.Header = SidebarDiagnostics.Framework.Resources.ButtonGitHub;
            if (menu.Items.Count > 10 && menu.Items[10] is MenuItem miClose)
                miClose.Header = SidebarDiagnostics.Framework.Resources.ButtonClose;
        }

        // toggled independently of the sidebar - the overlay is its own small
        // always-on-top window, not part of the docked sidebar panel list
        public static void RefreshFpsOverlay()
        {
            Overlay.FpsOverlay _overlay = Current.Windows.OfType<Overlay.FpsOverlay>().FirstOrDefault();

            if (Framework.Settings.Instance.ShowFpsOverlay)
            {
                if (_overlay == null)
                {
                    new Overlay.FpsOverlay().Show();
                }
            }
            else if (_overlay != null)
            {
                _overlay.StopPolling();
                _overlay.Close();
            }
        }

        public static void ShowPerformanceCounterError()
        {
            MessageBoxResult _result = MessageBox.Show(Framework.Resources.ErrorPerformanceCounter, Framework.Resources.ErrorTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

            if (_result == MessageBoxResult.OK)
            {
                OpenURL(Constants.URLs.WIKI);
            }
        }

        public static void OpenURL(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        public void OpenSettings()
        {
            Settings _settings = Windows.OfType<Settings>().FirstOrDefault();

            if (_settings != null)
            {
                _settings.WindowState = WindowState.Normal;
                _settings.Activate();
                return;
            }

            Sidebar _sidebar = Sidebar;

            if (_sidebar == null)
            {
                return;
            }

            new Settings(_sidebar);
        }

        public void RestoreSettings(SettingsRestoreState restore, Sidebar sidebar)
        {
            if (restore == null || sidebar == null)
            {
                return;
            }

            Settings _oldWindow = restore.OldWindow;
            if (_oldWindow == null || _oldWindow.IsClosed)
            {
                return;
            }

            _oldWindow.StopReloadTimeout();

            // Create and show the replacement window first at the same bounds, tab and monitor,
            // then close the old window in the same dispatcher operation so no frame shows neither.
            new Settings(sidebar, restore);

            _oldWindow.CloseForReplacement();
        }

        public void OpenGraph()
        {
            Sidebar _sidebar = Sidebar;

            if (_sidebar == null || !_sidebar.Ready)
            {
                return;
            }

            new Graph(_sidebar);
        }

        private async System.Threading.Tasks.Task CheckPawnIOAsync()
        {
            // this runs fire-and-forget from OnStartup ("_ = CheckPawnIOAsync();"), so
            // an unhandled exception here is never observed by anything: it doesn't
            // crash the app and it doesn't get logged, it just silently disappears
            // and the user never sees the install prompt
            try
            {
                await CheckPawnIOCoreAsync().ConfigureAwait(true);
            }
            catch (Exception e)
            {
                ErrorLog.Write(e);
            }
        }

        private async System.Threading.Tasks.Task CheckPawnIOCoreAsync()
        {
            ErrorLog.Write("PawnIO check: starting, IsInstalled=" + PawnIO.IsInstalled);

            if (PawnIO.IsInstalled)
            {
                return;
            }

            ErrorLog.Write("PawnIO check: showing install prompt");

            MessageBoxResult _result = MessageBox.Show(
                Framework.Resources.PawnIOInstallPrompt,
                Framework.Resources.AppName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes,
                MessageBoxOptions.DefaultDesktopOnly);

            ErrorLog.Write("PawnIO check: prompt answered, result=" + _result);

            if (_result != MessageBoxResult.Yes)
            {
                return;
            }

            var _progress = new ProgressDialog(Framework.Resources.PawnIOInstallingProgress);
            _progress.Show();

            bool _installed;

            try
            {
                _installed = await PawnIO.InstallAsync();
            }
            finally
            {
                _progress.Close();
            }

            if (!_installed)
            {
                MessageBoxResult _fallback = MessageBox.Show(
                    Framework.Resources.PawnIOInstallFailedPrompt,
                    Framework.Resources.AppName,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Yes,
                    MessageBoxOptions.DefaultDesktopOnly);

                if (_fallback == MessageBoxResult.Yes)
                {
                    OpenURL("https://pawnio.eu");
                }

                return;
            }

            // sensors already tried to read once at startup with no driver; offer to
            // reload now so CPU/GPU values populate without waiting for a restart
            Sidebar _sidebar = Sidebar;

            if (_sidebar == null || !_sidebar.Ready)
            {
                return;
            }

            var _prompt = new ReloadPromptDialog(Framework.Resources.PawnIOReloadPrompt);
            _prompt.ShowDialog();

            if (_prompt.ReloadRequested)
            {
                MonitorManager.InvalidateOfflineHardwareCache();
            _sidebar.Reload();
            }
        }

        private void CheckSettings()
        {
            if (Framework.Settings.Instance.RunAtStartup && !Utilities.Startup.StartupTaskExists())
            {
                Utilities.Startup.EnableStartupTask();
            }

            Framework.Settings.Instance.MonitorConfig = MonitorConfig.CheckConfig(Framework.Settings.Instance.MonitorConfig);
        }

        private void TrayIcon_TrayContextMenuOpen(object sender, RoutedEventArgs e)
        {
            Monitor _primary = Monitor.GetMonitors().GetPrimary();

            TrayIcon.ContextMenu.HorizontalOffset *= _primary.InverseScaleX;
            TrayIcon.ContextMenu.VerticalOffset *= _primary.InverseScaleY;
            
            // Dynamic update item
            MenuItem firstItem = TrayIcon.ContextMenu.Items[0] as MenuItem;
            if (firstItem != null && firstItem.Name == "DynamicUpdateItem") {
                TrayIcon.ContextMenu.Items.RemoveAt(0);
            }
            if (AvailableUpdateVersion != null) {
                MenuItem newItem = new MenuItem();
                newItem.Name = "DynamicUpdateItem";
                newItem.Header = string.Format(Framework.Resources.TrayUpdateAvailable, AvailableUpdateVersion);
                newItem.Click += TrayIcon_BalloonClicked;
                TrayIcon.ContextMenu.Items.Insert(0, newItem);
            }
        }

        private void Settings_Click(object sender, EventArgs e)
        {
            OpenSettings();
        }

        private void Reload_Click(object sender, EventArgs e)
        {
            Sidebar _sidebar = Sidebar;

            if (_sidebar == null)
            {
                return;
            }

            MonitorManager.InvalidateOfflineHardwareCache();
            _sidebar.Reload();
        }

        private void ResetPosition_Click(object sender, RoutedEventArgs e)
        {
            Sidebar _sidebar = Sidebar;
            if (_sidebar != null)
            {
                _ = _sidebar.Reposition();
            }
        }

        private void AlwaysTop_Click(object sender, RoutedEventArgs e)
        {
            Framework.Settings.Instance.Save();
            Sidebar _sidebar = Sidebar;
            if (_sidebar != null)
            {
                _sidebar.ApplyZOrderPolicy();
            }
        }

        private void Graph_Click(object sender, EventArgs e)
        {
            OpenGraph();
        }

        private void Visibility_SubmenuOpened(object sender, EventArgs e)
        {
            Sidebar _sidebar = Sidebar;

            if (_sidebar == null)
            {
                return;
            }

            MenuItem _this = (MenuItem)sender;

            (_this.Items.GetItemAt(0) as MenuItem).IsChecked = _sidebar.Visibility == Visibility.Visible;
            (_this.Items.GetItemAt(1) as MenuItem).IsChecked = _sidebar.Visibility == Visibility.Hidden;
        }

        private async void Show_Click(object sender, EventArgs e)
        {
            Sidebar _sidebar = Sidebar;

            if (_sidebar == null || _sidebar.Visibility == Visibility.Visible)
            {
                return;
            }

            await _sidebar.AppBarShow();
        }

        private void Hide_Click(object sender, EventArgs e)
        {
            Sidebar _sidebar = Sidebar;

            if (_sidebar == null || _sidebar.Visibility == Visibility.Hidden)
            {
                return;
            }

            _sidebar.AppBarHide();
        }

        private void GitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenURL(Constants.URLs.REPO);
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            _ = CheckForUpdatesAsync(true);
        }

        public static string AvailableUpdateVersion = null;
        public static string AvailableUpdateURL = null;
        private static bool _updateLoopRunning = false;
        private static bool _updateCheckInFlight = false;

        private static async System.Threading.Tasks.Task StartUpdateCheckLoop()
        {
            if (_updateLoopRunning) return;
            _updateLoopRunning = true;
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(60));
            while (_updateLoopRunning)
            {
                await CheckForUpdatesAsync(false);
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromHours(6));
            }
        }

        public static async System.Threading.Tasks.Task CheckForUpdatesAsync(bool manual)
        {
            if (_updateCheckInFlight) return;
            _updateCheckInFlight = true;
            try
            {
                UpdateInfo _update = await UpdateCheck.CheckAsync();

                if (_update != null)
                {
                    AvailableUpdateVersion = _update.Version;
                    AvailableUpdateURL = _update.URL;
                    AvailableUpdate = _update;
                    
                    if (Current.Windows.OfType<Settings>().FirstOrDefault() is Settings settingsWindow)
                    {
                        settingsWindow.Model.HasUpdate = true;
                        settingsWindow.Model.AvailableUpdateText = string.Format(Framework.Resources.SettingsUpdateAvailable, _update.Version);
                    }

                    TrayIcon.ToolTipText = string.Format(Framework.Resources.UpdateTrayTooltipFormat, Framework.Resources.AppName, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3));

                    if (Framework.Settings.Instance.LastNotifiedUpdate != _update.Version)
                    {
                        Framework.Settings.Instance.LastNotifiedUpdate = _update.Version;
                        Framework.Settings.Instance.Save();
                        
                        if (Framework.Settings.Instance.ShowTrayIcon)
                        {
                            TrayIcon.ShowBalloonTip(Framework.Resources.AppName, string.Format(Framework.Resources.SettingsUpdateAvailable, _update.Version) + (UpdateInstaller.CanInstall(_update) ? Framework.Resources.UpdateBalloonClickSuffix : ""), BalloonIcon.Info);
                        }
                        else
                        {
                            // no tray icon to click: ask once per version
                            if (MessageBox.Show(string.Format(Framework.Resources.SettingsUpdateAvailable, _update.Version) + (UpdateInstaller.CanInstall(_update) ? Framework.Resources.UpdatePromptInstallSuffix : Framework.Resources.UpdatePromptOpenPageSuffix), Framework.Resources.AppName, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                            {
                                InstallUpdate();
                            }
                        }
                    }
                    
                    if (manual)
                    {
                        MessageBox.Show(string.Format(Framework.Resources.SettingsUpdateAvailable, _update.Version), Framework.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (manual)
                {
                    string msg = UpdateCheck.LastFailed ? Framework.Resources.UpdateCheckFailed : Framework.Resources.UpdateNotAvailable;
                    MessageBox.Show(msg, Framework.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                _updateCheckInFlight = false;
            }
        }

        private static void TrayIcon_BalloonClicked(object sender, RoutedEventArgs e)
        {
            InstallUpdate();
        }

        public static UpdateInfo AvailableUpdate = null;
        private static bool _updateInstalling = false;

        private static void UpdateNotice(string text)
        {
            try
            {
                if (Framework.Settings.Instance.ShowTrayIcon && TrayIcon != null)
                {
                    TrayIcon.ShowBalloonTip(Framework.Resources.AppName, text, BalloonIcon.Info);
                }
            }
            catch { }
        }

        // Click on the update notice / tray item / Settings link: download the installer, verify it
        // and start the setup; SideMon closes itself so the setup can replace the files and relaunches
        // it afterwards. Anything that cannot be verified falls back to the release page.
        public static async void InstallUpdate()
        {
            UpdateInfo _update = AvailableUpdate;

            if (_update == null || _updateInstalling)
            {
                return;
            }

            if (!UpdateInstaller.CanInstall(_update))
            {
                if (AvailableUpdateURL != null)
                {
                    OpenURL(AvailableUpdateURL);
                }

                return;
            }

            _updateInstalling = true;

            try
            {
                UpdateNotice(string.Format(Framework.Resources.UpdateDownloadingFormat, _update.Version));

                string _path = await System.Threading.Tasks.Task.Run(() => UpdateInstaller.DownloadAsync(_update, null));

                UpdateNotice(string.Format(Framework.Resources.UpdateInstallingFormat, _update.Version));

                UpdateInstaller.Launch(_path);

                await System.Threading.Tasks.Task.Delay(1500);

                Current.Shutdown();
            }
            catch (Exception ex)
            {
                _updateInstalling = false;

                ErrorLog.Write(new Exception("Auto update failed: " + ex.GetType().Name + " - " + ex.Message));

                if (MessageBox.Show(Framework.Resources.UpdateDownloadFailedPrompt, Framework.Resources.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes && AvailableUpdateURL != null)
                {
                    OpenURL(AvailableUpdateURL);
                }
            }
        }

        private void Close_Click(object sender, EventArgs e)
        {
            Shutdown();
        }

        private static void AppDomain_Error(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;

            ErrorLog.Write(ex);

#if HAS_REPORT_SECRET
            string report = ReportService.BuildReport();
            
            string logTailPreview = "";
            try
            {
                int logStart = report.IndexOf("\r\n\r\n");
                if (logStart != -1)
                {
                    string logPart = report.Substring(logStart + 4);
                    var lines = logPart.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    // start at the newest log entry (a line beginning with "[") so the error
                    // message itself is shown, not just the bottom of its stack trace
                    int entryStart = 0;
                    for (int li = lines.Length - 1; li >= 0; li--)
                    {
                        if (lines[li].StartsWith("[") && !lines[li].Contains("[Diagnostic]")) { entryStart = li; break; }
                    }
                    logTailPreview = string.Join("\n", lines.Skip(entryStart).Take(12));
                }
            }
            catch { }

            MessageBoxResult _result = MessageBox.Show(
                string.Format(Framework.Resources.ErrorCrashReportPromptFormat, logTailPreview),
                Framework.Resources.ErrorTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Error,
                MessageBoxResult.No,
                MessageBoxOptions.DefaultDesktopOnly);

            if (_result == MessageBoxResult.Yes)
            {
                try
                {
                    var sendTask = System.Threading.Tasks.Task.Run(() => ReportService.Send(report));
                    if (sendTask.Wait(15000))
                    {
                        var result = sendTask.Result;
                        MessageBox.Show(result.FriendlyMessage, Framework.Resources.AppName, MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                    }
                    else
                    {
                        MessageBox.Show(Framework.Resources.ErrorReportServerUnreachable, Framework.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                    }
                }
                catch
                {
                    MessageBox.Show(Framework.Resources.ErrorReportServerUnreachable, Framework.Resources.AppName, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                }
            }
#else
            MessageBox.Show(
                Framework.Resources.ErrorCrashGeneric,
                Framework.Resources.ErrorTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                MessageBoxResult.OK,
                MessageBoxOptions.DefaultDesktopOnly);
#endif
            Resilience.FastExit(1);
        }

        public Sidebar Sidebar
        {
            get
            {
                return Windows.OfType<Sidebar>().FirstOrDefault();
            }
        }

        public IEnumerable<Graph> Graphs
        {
            get
            {
                return Windows.OfType<Graph>();
            }
        }

        public new static App Current
        {
            get
            {
                return (App)Application.Current;
            }
        }

        public static TaskbarIcon TrayIcon { get; set; }

        internal static bool _reloading { get; set; } = false;

        internal static bool _reloadOpenSettings { get; set; } = false;

        internal static SettingsRestoreState PendingSettingsRestore { get; set; }
    }

    public sealed class SettingsRestoreState
    {
        public Settings OldWindow { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public WindowState WindowState { get; set; }
        public int SelectedTabIndex { get; set; }
        public MonitorType? SelectedMonitorType { get; set; }
        public double VerticalScrollOffset { get; set; }
    }
}
