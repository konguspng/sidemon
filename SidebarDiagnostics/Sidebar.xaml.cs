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
    /// <summary>
    /// Interaction logic for Sidebar.xaml
    /// </summary>
    public partial class Sidebar : AppBarWindow
    {
        public Sidebar(bool openSettings, bool initiallyHidden)
        {
            // Always enable transparency (layered window) so that legacy blur-behind
            // composition and custom alpha-feathered gradients render correctly.
            AllowsTransparency = true;

            InitializeComponent();

            _openSettings = openSettings;
            _initiallyHidden = initiallyHidden;
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

            await CaptureScreenBehind();
            ApplyGlassStyling();

            Ready = true;
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

        public override async Task AppBarShow()
        {
            await base.AppBarShow();

            Model.Resume();
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

            if (Framework.Settings.Instance.GlassBackground)
            {
                // glass imitates the wallpaper, so the sidebar must sit at the bottom
                // of the window stack: other windows always cover it, never the reverse
                ClearTopMost(false);
                SetBottom(false);

                ShowDesktop.AddHook(this);
            }
            else if (Framework.Settings.Instance.AlwaysTop)
            {
                SetTopMost(false);

                ShowDesktop.RemoveHook();
            }
            else
            {
                ClearTopMost(false);

                ShowDesktop.AddHook(this);
            }

            if (Framework.Settings.Instance.ClickThrough)
            {
                SetClickThrough();
            }
            else
            {
                ClearClickThrough();
            }

            FontFamily = Framework.SidebarFonts.GetFamily(Framework.Settings.Instance.FontFamilyName);

            // when the glass area is wider than the sidebar, keep the content pinned
            // to the docked edge at the configured sidebar width
            if (Framework.Settings.Instance.GlassBackground && Framework.Settings.Instance.BlurWidth > Framework.Settings.Instance.SidebarWidth)
            {
                MainContent.Width = Framework.Settings.Instance.SidebarWidth;
                MainContent.HorizontalAlignment = Framework.Settings.Instance.DockEdge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            else
            {
                MainContent.Width = double.NaN;
                MainContent.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            ClearGlass();
            await CaptureScreenBehind();
            ApplyGlassStyling();
            this.Opacity = 1.0;

            if (Framework.Settings.Instance.ToolbarMode)
            {
                HideInAltTab();
            }
            else
            {
                ShowInAltTab();
            }

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
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
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

        private async Task CaptureScreenBehind()
        {
            if (CapturedBackgroundImage == null || !Framework.Settings.Instance.GlassBackground) return;

            // Get window position
            var left = this.Left;
            var top = this.Top;
            var width = this.Width;
            var height = this.Height;

            if (width <= 0 || height <= 0) return;

            await Task.CompletedTask;

            try
            {
                // exact window rectangle in physical virtual-screen pixels
                int x, y, w, h;
                IntPtr _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                NativeMethods.WINRECT _rect;

                if (_hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out _rect) && _rect.Right > _rect.Left && _rect.Bottom > _rect.Top)
                {
                    x = _rect.Left;
                    y = _rect.Top;
                    w = _rect.Right - _rect.Left;
                    h = _rect.Bottom - _rect.Top;
                }
                else
                {
                    double _scaleX = 1d, _scaleY = 1d;
                    PresentationSource _source = PresentationSource.FromVisual(this);

                    if (_source?.CompositionTarget != null)
                    {
                        _scaleX = _source.CompositionTarget.TransformToDevice.M11;
                        _scaleY = _source.CompositionTarget.TransformToDevice.M22;
                    }

                    x = (int)Math.Round(left * _scaleX);
                    y = (int)Math.Round(top * _scaleY);
                    w = (int)Math.Round(width * _scaleX);
                    h = (int)Math.Round(height * _scaleY);
                }

                // pad the rendered region by the blur radius so the gaussian never
                // samples past the edge, then crop the padding back off afterwards;
                // this keeps the visible pixels perfectly aligned with the desktop
                int _pad = (int)Math.Ceiling(Math.Max(0d, Framework.Settings.Instance.BlurStrength)) + 2;

                // render the wallpaper file the way Windows lays it out, rather than
                // photographing the screen, so open windows never leak into the glass
                using (System.Drawing.Bitmap bmp = RenderWallpaperRegion(x - _pad, y - _pad, w + (2 * _pad), h + (2 * _pad)))
                {
                    if (bmp == null)
                    {
                        CapturedBackgroundImage.Source = null;
                        return;
                    }

                    var handle = bmp.GetHbitmap();
                    try
                    {
                        var imgSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            handle,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        imgSource.Freeze();

                        // blur once here instead of running a live shader every frame
                        BitmapSource _blurred = BlurOnce(imgSource, Framework.Settings.Instance.BlurStrength);

                        var _cropped = new CroppedBitmap(_blurred, new Int32Rect(_pad, _pad, w, h));
                        _cropped.Freeze();

                        CapturedBackgroundImage.Source = _cropped;
                    }
                    finally
                    {
                        DeleteObject(handle);
                    }
                }
            }
            catch
            {
                CapturedBackgroundImage.Source = null;
            }
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

        private static BitmapSource BlurOnce(BitmapSource source, double radius)
        {
            if (radius <= 0d)
            {
                return source;
            }

            var img = new System.Windows.Controls.Image()
            {
                Source = source,
                Effect = new System.Windows.Media.Effects.BlurEffect()
                {
                    Radius = radius,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
                }
            };

            var size = new Size(source.PixelWidth, source.PixelHeight);
            img.Measure(size);
            img.Arrange(new Rect(size));

            var rtb = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(img);
            rtb.Freeze();

            return rtb;
        }

        private void ApplyGlassStyling()
        {
            var settings = Framework.Settings.Instance;

            // Make the window itself transparent so that the BackgroundBorder handles rendering
            this.Background = Brushes.Transparent;

            // Ensure LayoutRoot has no mask so all text, metrics, and charts stay fully visible
            if (LayoutRoot != null)
            {
                LayoutRoot.OpacityMask = null;
            }

            // Get background brush (tint)
            Brush tintBrush;
            try
            {
                Color tintColor;
                if (settings.AutoBGColor)
                {
                    tintColor = SystemParameters.WindowGlassColor;
                }
                else
                {
                    tintColor = (Color)ColorConverter.ConvertFromString(settings.BGColor);
                }
                tintBrush = new SolidColorBrush(tintColor) { Opacity = settings.BGOpacity };
            }
            catch
            {
                tintBrush = new SolidColorBrush(Colors.Black) { Opacity = settings.BGOpacity };
            }

            if (BackgroundBorder != null)
            {
                // If glass/blur is enabled, show the blurred image and configure its blur radius
                if (settings.GlassBackground && CapturedBackgroundImage != null)
                {
                    CapturedBackgroundImage.Visibility = Visibility.Visible;

                    // Set tint overlay color
                    if (TintOverlay != null)
                    {
                        TintOverlay.Background = tintBrush;
                    }
                    BackgroundBorder.Background = null;
                }
                else
                {
                    if (CapturedBackgroundImage != null)
                    {
                        CapturedBackgroundImage.Visibility = Visibility.Collapsed;
                    }
                    if (TintOverlay != null)
                    {
                        TintOverlay.Background = null;
                    }
                    // Apply tint directly to the border when glass is off
                    BackgroundBorder.Background = tintBrush;
                }

                // the edge fade belongs to the glass effect only; solid and accent
                // backgrounds must never be masked
                string direction = settings.GlassBackground ? settings.FeatherDirection : "None";
                if (direction == "Auto")
                {
                    // Opposite of docked edge. Usually, if docked on Right, we want to fade on Left.
                    // If docked on Left, we want to fade on Right.
                    direction = (settings.DockEdge == DockEdge.Right) ? "Left" : "Right";
                }

                if (direction == "None" || settings.FeatherSize <= 0.0d)
                {
                    BackgroundBorder.OpacityMask = null;
                }
                else
                {
                    double size = settings.FeatherSize / 100.0;
                    var mask = new LinearGradientBrush();

                    if (direction == "Left")
                    {
                        mask.StartPoint = new Point(0, 0);
                        mask.EndPoint = new Point(1, 0);
                        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
                        mask.GradientStops.Add(new GradientStop(Colors.Black, size));
                        mask.GradientStops.Add(new GradientStop(Colors.Black, 1.0));
                    }
                    else if (direction == "Right")
                    {
                        mask.StartPoint = new Point(0, 0);
                        mask.EndPoint = new Point(1, 0);
                        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
                        mask.GradientStops.Add(new GradientStop(Colors.Black, 1.0 - size));
                        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                    }
                    else if (direction == "Top")
                    {
                        mask.StartPoint = new Point(0, 0);
                        mask.EndPoint = new Point(0, 1);
                        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
                        mask.GradientStops.Add(new GradientStop(Colors.Black, size));
                        mask.GradientStops.Add(new GradientStop(Colors.Black, 1.0));
                    }
                    else if (direction == "Bottom")
                    {
                        mask.StartPoint = new Point(0, 0);
                        mask.EndPoint = new Point(0, 1);
                        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
                        mask.GradientStops.Add(new GradientStop(Colors.Black, 1.0 - size));
                        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                    }

                    BackgroundBorder.OpacityMask = mask;
                }
            }
        }

        private void ClearGlassStyling()
        {
            if (BackgroundBorder != null)
            {
                BackgroundBorder.Background = null;
                BackgroundBorder.OpacityMask = null;
            }
            if (TintOverlay != null)
            {
                TintOverlay.Background = null;
            }
            if (CapturedBackgroundImage != null)
            {
                CapturedBackgroundImage.Visibility = Visibility.Collapsed;
                CapturedBackgroundImage.Source = null;
            }
            if (LayoutRoot != null)
            {
                LayoutRoot.OpacityMask = null;
            }
            // Let the XAML Style set the background brush
            this.ClearValue(Window.BackgroundProperty);
        }
    }
}