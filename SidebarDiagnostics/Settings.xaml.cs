using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SidebarDiagnostics.Models;
using SidebarDiagnostics.Monitoring;
using SidebarDiagnostics.Windows;
using SidebarDiagnostics.Style;
using SidebarDiagnostics.Utilities;

namespace SidebarDiagnostics
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : FlatWindow
    {
        internal static int _lastSelectedTabIndex = 0;

        public Settings(Sidebar sidebar) : this(sidebar, null)
        {
        }

        internal Settings(Sidebar sidebar, SettingsRestoreState restore)
        {
            InitializeComponent();

            Version _version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = string.Format(Framework.Resources.SettingsUiVersionFormat, _version.Major, _version.Minor, _version.Build);

            int _tabIndex = restore != null ? restore.SelectedTabIndex : _lastSelectedTabIndex;
            if (_tabIndex >= 0 && _tabIndex < SettingsTabControl.Items.Count)
            {
                SettingsTabControl.SelectedIndex = _tabIndex;
            }

            DataContext = Model = new SettingsModel(sidebar);

            if (restore != null)
            {
                if (restore.SelectedMonitorType.HasValue && Model.MonitorConfig != null)
                {
                    MonitorConfig _selected = Model.MonitorConfig.FirstOrDefault(m => m.Type == restore.SelectedMonitorType.Value);
                    if (_selected != null)
                    {
                        Model.SelectedMonitorConfig = _selected;
                    }
                }

                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = restore.Left;
                Top = restore.Top;
                Width = restore.Width;
                Height = restore.Height;
                WindowState = restore.WindowState;
                ShowActivated = true;
                _pendingScrollOffset = restore.VerticalScrollOffset;
            }

            ShowInTaskbar = true;
            Show();
        }

        internal bool IsClosed { get; private set; } = false;

        internal void CloseForReplacement()
        {
            _replacing = true;
            StopReloadTimeout();
            if (!IsClosed)
            {
                Close();
            }
        }

        internal void StopReloadTimeout()
        {
            if (_reloadTimeoutTimer != null)
            {
                _reloadTimeoutTimer.Stop();
                _reloadTimeoutTimer = null;
            }
        }

        private ScrollViewer GetActiveScrollViewer()
        {
            if (SettingsTabControl?.SelectedItem is TabItem _tab && _tab.Content is ScrollViewer _sv)
            {
                return _sv;
            }

            return MonitorDetailScroll;
        }

        private SettingsRestoreState CaptureRestoreState()
        {
            bool _useRestoreBounds = WindowState != WindowState.Normal &&
                !RestoreBounds.IsEmpty &&
                RestoreBounds.Width > 0 &&
                RestoreBounds.Height > 0;

            ScrollViewer _sv = GetActiveScrollViewer();

            return new SettingsRestoreState
            {
                OldWindow = this,
                Left = _useRestoreBounds ? RestoreBounds.Left : Left,
                Top = _useRestoreBounds ? RestoreBounds.Top : Top,
                Width = _useRestoreBounds ? RestoreBounds.Width : (ActualWidth > 0 ? ActualWidth : Width),
                Height = _useRestoreBounds ? RestoreBounds.Height : (ActualHeight > 0 ? ActualHeight : Height),
                WindowState = WindowState,
                SelectedTabIndex = SettingsTabControl.SelectedIndex,
                SelectedMonitorType = Model?.SelectedMonitorConfig?.Type,
                VerticalScrollOffset = _sv != null ? _sv.VerticalOffset : 0d
            };
        }

        private void ShowBusyOverlay()
        {
            _busyApplying = true;
            MainContentPanel.IsEnabled = false;
            BusyOverlay.Visibility = Visibility.Visible;

            StopReloadTimeout();
            _reloadTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _reloadTimeoutTimer.Tick += (s, e) =>
            {
                if (App.PendingSettingsRestore != null && ReferenceEquals(App.PendingSettingsRestore.OldWindow, this))
                {
                    App.PendingSettingsRestore = null;
                }

                HideBusyOverlay();
            };
            _reloadTimeoutTimer.Start();
        }

        private void HideBusyOverlay()
        {
            StopReloadTimeout();
            _busyApplying = false;
            BusyOverlay.Visibility = Visibility.Collapsed;
            MainContentPanel.IsEnabled = true;
        }

        private async Task Save(bool finalize)
        {
            _lastSelectedTabIndex = SettingsTabControl.SelectedIndex;

            bool _cultureChanged = !string.Equals(Model.Culture, Framework.Settings.Instance.Culture, StringComparison.Ordinal);

            // glass, card/bar toggles, and culture changes restyle the whole window, so recreate the sidebar
            bool _reloadNeeded =
                Model.GlassBackground != Framework.Settings.Instance.GlassBackground ||
                Model.UseCardStyle != Framework.Settings.Instance.UseCardStyle ||
                Model.ShowAccentBars != Framework.Settings.Instance.ShowAccentBars ||
                Model.GlassBlurAmount != Framework.Settings.Instance.GlassBlurAmount ||
                Model.GlassBackground ||
                _cultureChanged;

            Model.Save();

            if (_cultureChanged)
            {
                Utilities.Culture.SetCurrent(false);
                App.RefreshTrayMenu();
            }

            App.RefreshFpsOverlay();

            if (_reloadNeeded)
            {
                if (finalize)
                {
                    App.PendingSettingsRestore = null;
                    App._reloadOpenSettings = false;
                    Close();

                    await App.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
                    {
                        Sidebar _sidebar = App.Current.Sidebar;
                        _sidebar?.Reload();
                    }));
                    return;
                }

                Sidebar _currentSidebar = App.Current.Sidebar;
                if (_currentSidebar == null || !_currentSidebar.Ready)
                {
                    return;
                }

                // Keep Settings visible with a busy overlay until the recreated sidebar is ready,
                // then replace this window in-place with a fresh one.
                ShowBusyOverlay();
                App._reloadOpenSettings = false;
                App.PendingSettingsRestore = CaptureRestoreState();

                await App.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
                {
                    Sidebar _sidebar = App.Current.Sidebar;

                    if (_sidebar == null || !_sidebar.Ready)
                    {
                        if (App.PendingSettingsRestore != null && ReferenceEquals(App.PendingSettingsRestore.OldWindow, this))
                        {
                            App.PendingSettingsRestore = null;
                        }

                        HideBusyOverlay();
                        return;
                    }

                    _sidebar.Reload();
                }));
                return;
            }

            await App.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(async () =>
            {
                Sidebar _sidebar = App.Current.Sidebar;

                if (_sidebar == null)
                {
                    return;
                }

                await _sidebar.Reset(finalize);
            }));
        }
        
        private static void MoveItem<T>(ObservableCollection<T> collection, T item, int offset)
        {
            if (collection == null || item == null)
            {
                return;
            }

            int _index = collection.IndexOf(item);
            int _newIndex = _index + offset;

            if (_index < 0 || _newIndex < 0 || _newIndex >= collection.Count)
            {
                return;
            }

            collection.Move(_index, _newIndex);
        }

        private void MonitorUp_Click(object sender, RoutedEventArgs e)
        {
            MoveItem(Model.MonitorConfig, (sender as FrameworkElement).DataContext as MonitorConfig, -1);
        }

        private void MonitorDown_Click(object sender, RoutedEventArgs e)
        {
            MoveItem(Model.MonitorConfig, (sender as FrameworkElement).DataContext as MonitorConfig, 1);
        }

        private void HardwareUp_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfig _hardware = (sender as FrameworkElement).DataContext as HardwareConfig;

            MoveItem(OwningHardwareCollection(_hardware), _hardware, -1);
        }

        private void HardwareDown_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfig _hardware = (sender as FrameworkElement).DataContext as HardwareConfig;

            MoveItem(OwningHardwareCollection(_hardware), _hardware, 1);
        }

        private ObservableCollection<HardwareConfig> OwningHardwareCollection(HardwareConfig hardware)
        {
            if (hardware == null)
            {
                return null;
            }

            return Model.MonitorConfig.Select(c => c.HardwareOC).FirstOrDefault(oc => oc != null && oc.Contains(hardware));
        }


        private async void HardwareClaudeRenew_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfig _hardware = (sender as FrameworkElement).DataContext as HardwareConfig;
            if (_hardware == null || _hardware.ID != "AIUsage_Claude") return;

            var claude = UsageRegistry.Providers.OfType<SidebarDiagnostics.Monitoring.Providers.ClaudeProvider>().FirstOrDefault();
            if (claude == null) return;
            
            var btn = sender as Button;
            btn.IsEnabled = false;
            _hardware.RenewStatus = Framework.Resources.SettingsUiRenewing;

            bool step1Ok = await claude.RunCmdAsync("auth status", 30);
            
            UsageRegistry.SetCustomPath(claude.Id, _hardware.CustomPath);
            await claude.FetchAsync(System.Threading.CancellationToken.None);
            UsageRegistry.PollIfDue(claude);
            
            var snapshot = UsageRegistry.GetSnapshot(claude.Id);
            if (snapshot.Status != UsageStatus.AuthExpired)
            {
                _hardware.RenewStatus = Framework.Resources.SettingsUsageClaudeRenewed;
                btn.IsEnabled = true;
                return;
            }

            _hardware.RenewStatus = Framework.Resources.SettingsUsageClaudeStillExpired;
            var result = MessageBox.Show(this, Framework.Resources.SettingsUsageClaudeRenewConfirm, Framework.Resources.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _hardware.RenewStatus = Framework.Resources.SettingsUiRenewingStep2;
                await claude.RunCmdAsync("-p \"ok\" --safe-mode --no-session-persistence --tools \"\"", 60);
                await claude.FetchAsync(System.Threading.CancellationToken.None);
                UsageRegistry.PollIfDue(claude);
                snapshot = UsageRegistry.GetSnapshot(claude.Id);
                if (snapshot.Status != UsageStatus.AuthExpired)
                {
                    _hardware.RenewStatus = Framework.Resources.SettingsUsageClaudeRenewed;
                }
                else
                {
                    _hardware.RenewStatus = Framework.Resources.SettingsUsageClaudeStillExpired;
                }
            }
            btn.IsEnabled = true;
        }

        private void HardwareSignIn_Click(object sender, RoutedEventArgs e)
        {
            HardwareConfig _hardware = (sender as FrameworkElement)?.DataContext as HardwareConfig;
            if (_hardware == null) return;

            var provider = UsageRegistry.Find(_hardware.ID);
            if (provider == null) return;

            _hardware.RenewStatus = Framework.Resources.UsageWaitingForSignIn;
            _hardware.Status = Framework.Resources.UsageWaitingForSignIn;

            UsageRegistry.LaunchLogin(_hardware.ID);
        }

        private void NumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (new Regex("[^0-9.-]+").IsMatch(e.Text))
            {
                e.Handled = true;
            }
        }

        private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue != 0d)
            {
                ShowTrayIconCheckbox.IsChecked = true;
            }
        }

        private void ClickThroughCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            ShowTrayIconCheckbox.IsChecked = true;
        }

        private void ShowTrayIconCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            XOffsetSlider.Value = 0d;
            YOffsetSlider.Value = 0d;

            ClickThroughCheckbox.IsChecked = false;
        }
        
        private void BindButton_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_hotkey != null)
            {
                EndBind();
            }

            (sender as ToggleButton).IsChecked = false;
        }

        private void BindToggle_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Toggle);
            }
            else
            {
                EndBind();
            }
        }

        private void BindShow_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Show);
            }
            else
            {
                EndBind();
            }
        }

        private void BindHide_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Hide);
            }
            else
            {
                EndBind();
            }
        }

        private void BindReload_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Reload);
            }
            else
            {
                EndBind();
            }
        }

        private void BindClose_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.Close);
            }
            else
            {
                EndBind();
            }
        }

        private void BindCycleEdge_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.CycleEdge);
            }
            else
            {
                EndBind();
            }
        }

        private void BindCycleScreen_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.CycleScreen);
            }
            else
            {
                EndBind();
            }
        }

        private void BindReserveSpace_Click(object sender, RoutedEventArgs e)
        {
            _keybinder = (ToggleButton)sender;

            if (_keybinder.IsChecked == true)
            {
                BeginBind(Hotkey.KeyAction.ReserveSpace);
            }
            else
            {
                EndBind();
            }
        }

        private void BeginBind(Hotkey.KeyAction action)
        {
            _hotkey = new Hotkey();
            _hotkey.Action = action;
            _hotkey.WinKey = Key.Escape;

            KeyDown += Window_KeyDown;
        }

        private void EndBind()
        {
            KeyDown -= Window_KeyDown;

            Hotkey.KeyAction _action = _hotkey.Action;

            if (_hotkey.WinKey == Key.Escape)
            {
                _hotkey = null;
            }

            switch (_action)
            {
                case Hotkey.KeyAction.Toggle:
                    Model.ToggleKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Show:
                    Model.ShowKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Hide:
                    Model.HideKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Reload:
                    Model.ReloadKey = _hotkey;
                    break;

                case Hotkey.KeyAction.Close:
                    Model.CloseKey = _hotkey;
                    break;

                case Hotkey.KeyAction.CycleEdge:
                    Model.CycleEdgeKey = _hotkey;
                    break;

                case Hotkey.KeyAction.CycleScreen:
                    Model.CycleScreenKey = _hotkey;
                    break;

                case Hotkey.KeyAction.ReserveSpace:
                    Model.ReserveSpaceKey = _hotkey;
                    break;
            }

            _keybinder.IsChecked = false;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            Key _key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (new Key[] { Key.LeftAlt, Key.RightAlt, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LWin, Key.RWin }.Contains(_key))
            {
                return;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _hotkey.CtrlMod = true;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                _hotkey.ShiftMod = true;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
            {
                _hotkey.WinMod = true;
            }

            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                _hotkey.AltMod = true;
            }

            _hotkey.WinKey = _key;

            EndBind();

            e.Handled = true;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busyApplying)
            {
                return;
            }

            await Save(true);

            if (!IsClosed)
            {
                Close();
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busyApplying)
            {
                return;
            }

            await Save(false);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Model.IsChanged)
            {
                Sidebar _sidebar = App.Current.Sidebar;

                if (_sidebar != null)
                {
                    DataContext = Model = new SettingsModel(_sidebar);
                    return;
                }
            }

            Close();
        }

        private void SendLog_Click(object sender, RoutedEventArgs e)
        {
            string report = ReportService.BuildReport();
            var window = new ReportPreviewWindow(report)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private async void InstallPawnIO_Click(object sender, RoutedEventArgs e)
        {
            Model.PawnIODriverInstalling = true;

            bool _installed;

            try
            {
                _installed = await PawnIO.InstallAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                _installed = false;
            }
            finally
            {
                Model.PawnIODriverInstalling = false;
            }

            Model.PawnIODriverInstalled = _installed;

            if (!_installed)
            {
                MessageBoxResult _fallback = MessageBox.Show(
                    Framework.Resources.PawnIOInstallFailedPrompt,
                    Framework.Resources.AppName,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Yes);

                if (_fallback == MessageBoxResult.Yes)
                {
                    App.OpenURL("https://pawnio.eu");
                }

                return;
            }

            Sidebar _sidebar = App.Current.Sidebar;

            if (_sidebar == null || !_sidebar.Ready)
            {
                return;
            }

            var _prompt = new ReloadPromptDialog(Framework.Resources.PawnIOReloadPrompt);
            _prompt.Owner = this;
            _prompt.ShowDialog();

            if (_prompt.ReloadRequested)
            {
                _sidebar.Reload();
            }
        }

        private void ColorBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            string propertyName = border.Tag as string;
            if (string.IsNullOrEmpty(propertyName)) return;

            if (Model == null) return;

            string currentColorStr = "";
            if (propertyName == "BGColor") currentColorStr = Model.BGColor;
            else if (propertyName == "FontColor") currentColorStr = Model.FontColor;
            else if (propertyName == "AlertFontColor") currentColorStr = Model.AlertFontColor;
            else if (propertyName == "AccentColor") currentColorStr = Model.AccentColor;

            using (var dialog = new System.Windows.Forms.ColorDialog())
            {
                dialog.AnyColor = true;
                dialog.FullOpen = true;

                // Try to parse the existing hex color to set the picker initial state
                try
                {
                    var drawingColor = System.Drawing.ColorTranslator.FromHtml(currentColorStr);
                    dialog.Color = drawingColor;
                }
                catch { }

                if (dialog.ShowDialog(new Win32Window(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK)
                {
                    string hexColor = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");

                    if (propertyName == "BGColor") Model.BGColor = hexColor;
                    else if (propertyName == "FontColor") Model.FontColor = hexColor;
                    else if (propertyName == "AlertFontColor") Model.AlertFontColor = hexColor;
                    else if (propertyName == "AccentColor") Model.AccentColor = hexColor;
                }
            }
        }

                private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Topmost = true;
            Topmost = false;
            Activate();
            Hotkey.Disable();

            // keep the whole dialog (incl. Save/Apply) above the taskbar on any resolution
            MaxHeight = SystemParameters.WorkArea.Height;

            if (WindowState == WindowState.Normal && Top + ActualHeight > SystemParameters.WorkArea.Bottom)
            {
                Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - ActualHeight);
            }

            if (_pendingScrollOffset > 0d)
            {
                double _offset = _pendingScrollOffset;
                _pendingScrollOffset = 0d;

                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
                {
                    ScrollViewer _sv = GetActiveScrollViewer();
                    _sv?.ScrollToVerticalOffset(_offset);
                }));
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            StopReloadTimeout();

            if (!_replacing && App.PendingSettingsRestore != null && ReferenceEquals(App.PendingSettingsRestore.OldWindow, this))
            {
                App.PendingSettingsRestore = null;
            }

            DataContext = null;
            Model = null;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            IsClosed = true;

            if (!_replacing)
            {
                Hotkey.Enable();
            }
        }

        
        private void FitToScreen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Sidebar _sidebar = App.Current.Sidebar;
                if (_sidebar == null || !_sidebar.Ready)
                {
                    return;
                }

                // applies immediately; the slider shows the new scale so Apply keeps it
                Model.UIScale = _sidebar.FitToScreenNow();
                Model.MaxUIScale = 3d;
            }
            catch (Exception _ex)
            {
                Utilities.ErrorLog.Write(_ex);
            }
        }

        private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            App.InstallUpdate();
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await App.CheckForUpdatesAsync(true);
        }

        public SettingsModel Model { get; private set; }

        private Hotkey _hotkey { get; set; }

        private ToggleButton _keybinder { get; set; }

        private bool _replacing = false;

        private bool _busyApplying = false;

        private double _pendingScrollOffset = 0d;

        private DispatcherTimer _reloadTimeoutTimer;
    
    public class Win32Window : System.Windows.Forms.IWin32Window
    {
        public System.IntPtr Handle { get; private set; }

        public Win32Window(System.IntPtr handle)
        {
            Handle = handle;
        }
    }
}
}
