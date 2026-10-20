using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SidebarDiagnostics.Windows;
using SidebarDiagnostics.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace SidebarDiagnostics
{
    public struct WorkAreaInfo
    {
        public RECT Bounds;
        public RECT WorkArea;
        public double ScaleX;
        public double ScaleY;
        public bool IsAutoHide;

        public double InverseScaleY => ScaleY > 0 ? 1.0 / ScaleY : 1.0;
        public double InverseScaleX => ScaleX > 0 ? 1.0 / ScaleX : 1.0;

        public double UsableHeightDips
        {
            get
            {
                RECT rect = IsAutoHide ? Bounds : WorkArea;
                return (rect.Bottom - rect.Top) * InverseScaleY;
            }
        }
    }

    /// <summary>
    /// Interaction logic for Sidebar.xaml
    /// </summary>
    public partial class Sidebar : AppBarWindow
    {
        public Sidebar(bool openSettings, bool initiallyHidden)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            // Always enable transparency (layered window) so that legacy blur-behind
            // composition and custom alpha-feathered gradients render correctly.
            AllowsTransparency = true;

            InitializeComponent();

            _openSettings = openSettings;
            _initiallyHidden = initiallyHidden;

            // Content can grow after the first fit (usage rows arrive from the network,
            // drives appear, text wraps): re-check the fit whenever the unscaled content
            // height really changes. The window scale is a LayoutTransform, so changing
            // it does not resize ContentStack and cannot retrigger this.
            ContentStack.SizeChanged += (s, e) =>
            {
                if (e.HeightChanged && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 1d)
                {
                    ScheduleAutoFit();
                }
            };
        }

        public void Reload()
        {
            if (!Ready)
            {
                return;
            }

            Ready = false;

            App._reloading = true;

            Close();
        }

        public async Task Reset(bool enableHotkeys)
        {
            if (!Ready)
            {
                return;
            }

            Ready = false;

            await BindSettings(enableHotkeys);

            await BindModel();
        }

        public async Task Reposition()
        {
            if (!Ready)
            {
                return;
            }

            Ready = false;

            await BindPosition();

            Ready = true;

            ScheduleAutoFit();
        }

        public void ContentReload()
        {
            if (!Ready)
            {
                return;
            }

            Ready = false;

            Model.Reload();

            Ready = true;

            BindGraphs();

            ScheduleAutoFit();
        }

        // While a fullscreen app (game, video) is on this screen, stop polling sensors
        // and rendering updates entirely so the sidebar costs no CPU or GPU time.
        protected override void OnFullScreenAppChanged(bool active)
        {
            if (Model == null || !Ready)
            {
                return;
            }

            if (active)
            {
                if (!_pausedForFullscreen && Visibility == Visibility.Visible)
                {
                    _pausedForFullscreen = true;
                    Model.Pause();
                }
            }
            else if (_pausedForFullscreen)
            {
                _pausedForFullscreen = false;

                if (Visibility == Visibility.Visible)
                {
                    Model.Resume();
                }
            }
        }

        private bool _pausedForFullscreen = false;

        private void SystemEvents_SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
            {
                Model?.Pause();
            }
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            {
                if (Visibility == Visibility.Visible && !_pausedForFullscreen)
                {
                    Model?.Resume();
                }
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
            {
                Model?.Pause();
            }
            else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                if (Visibility == Visibility.Visible && !_pausedForFullscreen)
                {
                    Model?.Resume();
                }
            }
        }

        public override async Task AppBarShow()
        {
            await base.AppBarShow();

            // Window.Show() re-activates and raises the window to the top of its
            // z-band regardless of prior state, silently undoing the bottom/non-topmost
            // policy set at bind time; reassert it every time we come back from Hide()
            ApplyZOrderPolicy();

            Model.Resume();
        }

        public void ApplyZOrderPolicy()
        {
            if (Framework.Settings.Instance.AlwaysTop && !Framework.Settings.Instance.GlassBackground)
            {
                SetTopMost(false);
            }
            else
            {
                // If there are no normal windows visible (e.g. Win+D was pressed),
                // lift the sidebar above the desktop so it is not hidden behind it, but only
                // to the top of the NORMAL band (SetTop): that is always below the taskbar and
                // every tray flyout, so SideMon can never cover the "^" overflow popup.
                // Otherwise, pin it to the bottom of the window stack.
                if (!ShowDesktop.AnyNormalWindowVisible())
                {
                    SetTop(false);
                }
                else
                {
                    ClearTopMost(false);
                    SetBottom(false);
                }
            }
        }

        public override void AppBarHide()
        {
            base.AppBarHide();

            Model.Pause();
        }

        private async Task Initialize()
        {
            Ready = false;

            Devices.AddHook(this);

            DisableAeroPeek();

            await BindSettings(true);

            await BindModel();
        }

        private async Task BindSettings(bool enableHotkeys)
        {
            await BindPosition();

            // The tool-window style (out of Alt+Tab, all virtual desktops) is set
            // once in AppBarWindow.OnSourceInitialized before the window is first
            // shown; this is a cheap no-op re-assert on later reloads.
            HideInAltTab();

            ApplyZOrderPolicy();

            ShowDesktop.AddHook(this);

            if (Framework.Settings.Instance.ClickThrough)
            {
                SetClickThrough();
            }
            else
            {
                ClearClickThrough();
            }

            FontFamily = Framework.SidebarFonts.GetFamily(Framework.Settings.Instance.FontFamilyName);

            MainContent.Width = double.NaN;
            MainContent.HorizontalAlignment = HorizontalAlignment.Stretch;

            ApplyGlassStyling();
            this.Opacity = 1.0;

            if (WindowControls.Visibility != Visibility.Visible)
            {
                if (Framework.Settings.Instance.CollapseMenuBar)
                {
                    WindowControls.Visibility = Visibility.Collapsed;
                }
                else
                {
                    WindowControls.Visibility = Visibility.Hidden;
                }
            }

            Hotkey.Initialize(this, Framework.Settings.Instance.Hotkeys);

            if (enableHotkeys)
            {
                Hotkey.Enable();
            }
        }

        private async Task BindPosition()
        {
            await SetAppBar();
        }
        
        private async Task BindModel()
        {
            await Task.Run(async () =>
            {
                if (Model != null)
                {
                    Model.Dispose();
                    Model = null;
                }

                await Dispatcher.BeginInvoke(DispatcherPriority.Normal, new ModelReadyHandler(ModelReady), new SidebarModel());
            });
        }

        private delegate void ModelReadyHandler(SidebarModel model);

        private void SignInButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (Framework.Settings.Instance.ClickThrough)
            {
                ClearClickThrough();
            }
        }

        private void SignInButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (Framework.Settings.Instance.ClickThrough)
            {
                SetClickThrough();
            }
        }

        private void ModelReady(SidebarModel model)
        {
            DataContext = Model = model;
            model.Start();

            Ready = true;

            BindGraphs();

            if (_openSettings)
            {
                _openSettings = false;

                App.Current.OpenSettings();
            }

            if (_initiallyHidden)
            {
                _initiallyHidden = false;

                AppBarHide();
            }

            ScheduleAutoFit();
        }

        private DispatcherTimer _autoFitSettleTimer;

        private EventHandler _autoFitLayoutHandler;

        // ItemsControl content bound via DataContext isn't realized synchronously,
        // and a single LayoutUpdated event isn't a reliable "fully settled" signal -
        // it can fire multiple times as items are incrementally realized. Instead,
        // restart a short timer on every LayoutUpdated; once layout goes quiet for
        // that long, it's actually settled, and only then do we measure.
        private void ScheduleAutoFit()
        {
            if (_autoFitLayoutHandler == null)
            {
                _autoFitLayoutHandler = (s, e) => RestartAutoFitSettleTimer();

                LayoutUpdated += _autoFitLayoutHandler;
            }

            RestartAutoFitSettleTimer();
        }

        private void RestartAutoFitSettleTimer()
        {
            if (_autoFitSettleTimer == null)
            {
                _autoFitSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };

                _autoFitSettleTimer.Tick += (s, e) =>
                {
                    _autoFitSettleTimer.Stop();

                    if (_autoFitLayoutHandler != null)
                    {
                        LayoutUpdated -= _autoFitLayoutHandler;
                        _autoFitLayoutHandler = null;
                    }

                    AutoFitUIScale();
                };
            }

            _autoFitSettleTimer.Stop();
            _autoFitSettleTimer.Start();
        }

        public static Func<int, WorkAreaInfo> WorkAreaProvider { get; set; } = DefaultWorkAreaProvider;

        public static WorkAreaInfo DefaultWorkAreaProvider(int screenIndex)
        {
            Windows.Monitor _monitor = Windows.Monitor.GetMonitorFromIndex(screenIndex);

            bool _isAutoHide = false;
            try
            {
                var _data = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                UIntPtr _state = Windows.NativeMethods.SHAppBarMessage(APPBARMSG.ABM_GETSTATE, ref _data);
                if (((ulong)_state.ToUInt64() & 0x01) != 0) // ABS_AUTOHIDE
                {
                    _isAutoHide = true;
                }
            }
            catch { }

            return new WorkAreaInfo
            {
                Bounds = _monitor.Size,
                WorkArea = _monitor.WorkArea,
                ScaleX = _monitor.ScaleX,
                ScaleY = _monitor.ScaleY,
                IsAutoHide = _isAutoHide
            };
        }

        public static double ComputeAutoFitScale(double totalUnscaledHeight, double userTargetScale, WorkAreaInfo info, int yOffset = 0)
        {
            double usableHeight = Math.Max(0d, info.UsableHeightDips - Math.Abs(yOffset));
            if (usableHeight <= 0d || totalUnscaledHeight <= 0d)
            {
                return userTargetScale;
            }

            if (totalUnscaledHeight * userTargetScale <= usableHeight)
            {
                return userTargetScale;
            }

            double rawScale = usableHeight / totalUnscaledHeight;
            double rounded = Math.Round(rawScale, 2);
            if (rounded * totalUnscaledHeight > usableHeight + 1.0)
            {
                rounded = Math.Floor(rawScale * 100d) / 100d;
            }

            return Math.Min(userTargetScale, Math.Max(0.5d, rounded));
        }

        // If the enabled monitors/drives don't fit the screen at their natural size,
        // shrink the whole sidebar just enough to fit by writing the required scale
        // into the real UI Scale setting (Advanced tab) - the same setting a user
        // would drag manually, so it renders and persists exactly the same way.
        private void AutoFitUIScale()
        {
            if (ContentStack == null || WindowControls == null)
            {
                return;
            }

            if (!Framework.Settings.Instance.AutoFitScale)
            {
                if (Math.Abs(Framework.Settings.Instance.MaxUIScale - 3.0d) > 0.01d)
                {
                    Framework.Settings.Instance.MaxUIScale = 3.0d;
                    Framework.Settings.Instance.Save();
                }
                return;
            }

            double _availableWidth = Math.Max(0d, Framework.Settings.Instance.SidebarWidth - 30d);
            ContentStack.Measure(new Size(_availableWidth, double.PositiveInfinity));
            double _naturalHeight = ContentStack.DesiredSize.Height;
            if (_naturalHeight <= 0d)
            {
                return;
            }

            double _menuHeight = WindowControls.Visibility == Visibility.Collapsed ? 0d : (WindowControls.ActualHeight > 0d ? WindowControls.ActualHeight : 32d);
            double _totalUnscaledHeight = _menuHeight + 30d + _naturalHeight;

            WorkAreaInfo _info = WorkAreaProvider(Framework.Settings.Instance.ScreenIndex);
            double _userScale = Framework.Settings.Instance.UIScale;
            double _availableHeight = Math.Max(0d, _info.UsableHeightDips - Math.Abs(Framework.Settings.Instance.YOffset));

            bool _overflowing = (_totalUnscaledHeight * _userScale) > _availableHeight;
            double _reducedScale = 3.0d;
            bool _changed = false;
            bool _scaleChanged = false;

            if (_overflowing)
            {
                double _scale = ComputeAutoFitScale(_totalUnscaledHeight, _userScale, _info, Framework.Settings.Instance.YOffset);
                _reducedScale = _scale;
                if (Math.Abs(Framework.Settings.Instance.UIScale - _scale) > 0.01d)
                {
                    Framework.Settings.Instance.UIScale = _scale;
                    _changed = true;
                    _scaleChanged = true;
                }
            }

            // We use MaxUIScale to pass the reduced scale to SettingsModel so it knows when to show the warning.
            if (Math.Abs(Framework.Settings.Instance.MaxUIScale - _reducedScale) > 0.01d)
            {
                Framework.Settings.Instance.MaxUIScale = _reducedScale;
                _changed = true;
            }

            if (_changed)
            {
                Framework.Settings.Instance.Save();
            }

            // The saved scale alone does not resize the docked window: re-dock so the
            // new scale is applied right away (the same path Apply takes). Layout is
            // stable afterwards, so the follow-up auto-fit finds nothing to change.
            if (_scaleChanged)
            {
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await Reposition(); }
                    catch (Exception _ex) { Utilities.ErrorLog.Write(_ex); }
                }));
            }
        }

        private void BindGraphs()
        {
            foreach (Graph _graph in App.Current.Graphs)
            {
                _graph.Model.BindData(Model.MonitorManager);
            }
        }

        private void GraphButton_Click(object sender, RoutedEventArgs e)
        {
            App.Current.OpenGraph();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            App.Current.OpenSettings();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Shutdown();
        }
        
        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            WindowControls.Visibility = Visibility.Visible;
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            WindowControls.Visibility = Framework.Settings.Instance.CollapseMenuBar ? Visibility.Collapsed : Visibility.Hidden;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await Initialize();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
                ApplyZOrderPolicy();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _isHiding = true;

            Ready = false;

            DataContext = null;

            if (Model != null)
            {
                Model.Dispose();
                Model = null;
            }

            ClearAppBar();

            Devices.RemoveHook(this);
            ShowDesktop.RemoveHook();
            Hotkey.Dispose();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (App._reloading)
            {
                App._reloading = false;

                bool _openSettings = App._reloadOpenSettings;
                App._reloadOpenSettings = false;

                new Sidebar(_openSettings, false).Show();
            }
            else
            {
                App.Current.Shutdown();
            }
        }

        private bool _ready { get; set; } = false;

        public bool Ready
        {
            get
            {
                return _ready;
            }
            set
            {
                _ready = value;
                if (Model != null)
                {
                    Model.Ready = value;
                }
                // Skeleton visibility is controlled directly here because Ready does not
                // raise INotifyPropertyChanged, so a XAML binding would never re-evaluate.
                if (SkeletonPanel != null)
                {
                    SkeletonPanel.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        public SidebarModel Model { get; private set; }

        private bool _openSettings { get; set; } = false;

        private bool _initiallyHidden { get; set; } = false;

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject([In] IntPtr hObject);

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern int GetSystemMetrics(int nIndex);

            [StructLayout(LayoutKind.Sequential)]
            public struct WINRECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hWnd, out WINRECT lpRect);
        }

        // paints the region [x,y,w,h] (virtual-screen pixels) of the desktop wallpaper
        // as Windows composes it for the sidebar's monitor: Fill/Fit/Stretch/Center/Tile/Span
        private System.Drawing.Bitmap RenderWallpaperRegion(int x, int y, int w, int h)
        {
            try
            {
                string _path = null;
                int _style = 10;
                bool _tile = false;

                using (Microsoft.Win32.RegistryKey _key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (_key != null)
                    {
                        _path = _key.GetValue("WallPaper") as string;

                        int _parsed;
                        if (int.TryParse(_key.GetValue("WallpaperStyle") as string, out _parsed))
                        {
                            _style = _parsed;
                        }

                        _tile = string.Equals(_key.GetValue("TileWallpaper") as string, "1", StringComparison.Ordinal);
                    }
                }

                System.Drawing.Color _bgColor = System.Drawing.Color.Black;

                using (Microsoft.Win32.RegistryKey _key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors"))
                {
                    string[] _rgb = (_key?.GetValue("Background") as string)?.Split(' ');

                    int _r, _g, _b;
                    if (_rgb != null && _rgb.Length == 3 && int.TryParse(_rgb[0], out _r) && int.TryParse(_rgb[1], out _g) && int.TryParse(_rgb[2], out _b))
                    {
                        _bgColor = System.Drawing.Color.FromArgb(_r, _g, _b);
                    }
                }

                Windows.Monitor _monitor = Windows.Monitor.GetMonitorFromIndex(Framework.Settings.Instance.ScreenIndex);

                int _areaX = _monitor.Size.Left;
                int _areaY = _monitor.Size.Top;
                int _areaW = _monitor.Size.Right - _monitor.Size.Left;
                int _areaH = _monitor.Size.Bottom - _monitor.Size.Top;

                if (_style == 22) // span: one image across the whole virtual desktop
                {
                    _areaX = NativeMethods.GetSystemMetrics(76);
                    _areaY = NativeMethods.GetSystemMetrics(77);
                    _areaW = NativeMethods.GetSystemMetrics(78);
                    _areaH = NativeMethods.GetSystemMetrics(79);
                }

                var _dest = new System.Drawing.Bitmap(w, h);

                using (var _gfx = System.Drawing.Graphics.FromImage(_dest))
                {
                    _gfx.Clear(_bgColor);

                    if (!string.IsNullOrEmpty(_path) && System.IO.File.Exists(_path))
                    {
                        using (var _wall = new System.Drawing.Bitmap(_path))
                        {
                            _gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            _gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                            // shift so that drawing in monitor-area coordinates lands on our region
                            _gfx.TranslateTransform(_areaX - x, _areaY - y);

                            if (_tile && _style == 0)
                            {
                                using (var _brush = new System.Drawing.TextureBrush(_wall))
                                {
                                    _gfx.FillRectangle(_brush, 0, 0, _areaW, _areaH);
                                }
                            }
                            else
                            {
                                float _dw, _dh;

                                switch (_style)
                                {
                                    case 2: // stretch
                                        _dw = _areaW;
                                        _dh = _areaH;
                                        break;
                                    case 6: // fit
                                        float _fit = Math.Min((float)_areaW / _wall.Width, (float)_areaH / _wall.Height);
                                        _dw = _wall.Width * _fit;
                                        _dh = _wall.Height * _fit;
                                        break;
                                    case 0: // center
                                        _dw = _wall.Width;
                                        _dh = _wall.Height;
                                        break;
                                    default: // fill (10) and span (22)
                                        float _fill = Math.Max((float)_areaW / _wall.Width, (float)_areaH / _wall.Height);
                                        _dw = _wall.Width * _fill;
                                        _dh = _wall.Height * _fill;
                                        break;
                                }

                                _gfx.DrawImage(_wall, (_areaW - _dw) / 2f, (_areaH - _dh) / 2f, _dw, _dh);
                            }
                        }
                    }
                }

                return _dest;
            }
            catch
            {
                return null;
            }
        }

        // Real glass: Windows' own acrylic compositor blur (the same effect as the taskbar and
        // Start menu) is applied to this window, so it blurs whatever is really behind the
        // sidebar, live. The tint is the configured colour; opacity is its alpha.
        private void ApplyGlassStyling()
        {
            var settings = Framework.Settings.Instance;

            // the window itself stays transparent; BackgroundBorder (or the acrylic) is the background
            this.Background = Brushes.Transparent;

            if (LayoutRoot != null)
            {
                LayoutRoot.OpacityMask = null;
            }

            Color tintColor;
            try
            {
                tintColor = settings.AutoBGColor ? SystemParameters.WindowGlassColor : (Color)ColorConverter.ConvertFromString(settings.BGColor);
            }
            catch
            {
                tintColor = Colors.Black;
            }

            bool _acrylic = settings.GlassBackground && GlassSupported;

            if (BackgroundBorder != null)
            {
                if (_acrylic)
                {
                    // near-invisible fill keeps the whole area clickable
                    BackgroundBorder.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                }
                else
                {
                    BackgroundBorder.Background = new SolidColorBrush(tintColor) { Opacity = settings.BGOpacity };
                }
            }

            if (_acrylic)
            {
                SetGlass(tintColor, settings.BGOpacity, settings.GlassBlurAmount);
            }
            else
            {
                ClearGlass();
            }
        }


    }
}