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

            if (Framework.Settings.Instance.AlwaysTop)
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

        private async Task CaptureScreenBehind()
        {
            if (CapturedBackgroundImage == null || !Framework.Settings.Instance.GlassBackground) return;

            // Get window position
            var left = this.Left;
            var top = this.Top;
            var width = this.Width;
            var height = this.Height;

            if (width <= 0 || height <= 0) return;

            // Temporarily set window opacity to 0 to capture the background desktop correctly
            double oldOpacity = this.Opacity;
            this.Opacity = 0;

            // Wait for 100ms to allow DWM to update the desktop composition without our window
            await Task.Delay(100);

            try
            {
                int x = (int)left;
                int y = (int)top;
                int w = (int)width;
                int h = (int)height;

                using (var bmp = new System.Drawing.Bitmap(w, h))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
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
                        CapturedBackgroundImage.Source = BlurOnce(imgSource, Framework.Settings.Instance.BlurStrength);
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
            finally
            {
                // Restore window opacity to fully visible
                this.Opacity = 1.0;
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

                // Determine fade direction
                string direction = settings.FeatherDirection;
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