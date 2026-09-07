using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SidebarDiagnostics.Windows;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Overlay
{
    public partial class FpsOverlay : DPIAwareWindow
    {
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 32;
        private const long WS_EX_TOOLWINDOW = 128;

        private DispatcherTimer _pollTimer;

        public FpsOverlay()
        {
            InitializeComponent();

            Loaded += FpsOverlay_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Set both ex-styles before the window is first shown: WS_EX_TRANSPARENT
            // for click-through, WS_EX_TOOLWINDOW so the overlay never shows up in
            // Alt+Tab / the task switcher. Applying WS_EX_TOOLWINDOW after the first
            // Show() (the old Loaded path) is ignored by Windows until the window is
            // hidden and shown again, which the overlay never does.
            IntPtr _hwnd = new WindowInteropHelper(this).Handle;

            long _style = NativeMethods.GetWindowLongPtr(_hwnd, GWL_EXSTYLE);

            NativeMethods.SetWindowLongPtr(_hwnd, GWL_EXSTYLE, _style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        }

        private void FpsOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            Reposition();

            Framework.Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            // same cadence as the sidebar's own sensor polling - frequent enough to
            // feel live without adding meaningful overhead
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pollTimer.Tick += (s, ev) => Poll();
            _pollTimer.Start();

            Poll();
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "FpsOverlayCorner" || e.PropertyName == "ScreenIndex")
            {
                Reposition();
            }
        }

        public void Reposition()
        {
            Monitor _monitor = Monitor.GetMonitorFromIndex(Framework.Settings.Instance.ScreenIndex);

            double _left = _monitor.WorkArea.Left * _monitor.InverseScaleX;
            double _top = _monitor.WorkArea.Top * _monitor.InverseScaleY;
            double _right = _monitor.WorkArea.Right * _monitor.InverseScaleX;
            double _bottom = _monitor.WorkArea.Bottom * _monitor.InverseScaleY;

            const double _margin = 12d;

            switch (Framework.Settings.Instance.FpsOverlayCorner)
            {
                case OverlayCorner.TopLeft:
                    Left = _left + _margin;
                    Top = _top + _margin;
                    break;

                case OverlayCorner.TopRight:
                    Left = _right - Width - _margin;
                    Top = _top + _margin;
                    break;

                case OverlayCorner.BottomLeft:
                    Left = _left + _margin;
                    Top = _bottom - Height - _margin;
                    break;

                case OverlayCorner.BottomRight:
                default:
                    Left = _right - Width - _margin;
                    Top = _bottom - Height - _margin;
                    break;
            }
        }

        private void Poll()
        {
            double? _fps = RTSSReader.GetForegroundFps();

            if (_fps.HasValue)
            {
                FpsValue.Text = Math.Round(_fps.Value).ToString();
                FpsValue.Visibility = Visibility.Visible;
                FpsLabel.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Collapsed;
            }
            else
            {
                FpsValue.Visibility = Visibility.Collapsed;
                FpsLabel.Visibility = Visibility.Collapsed;
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = RTSSReader.IsAvailable ? "--" : "No RTSS";
            }
        }

        public void StopPolling()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }

            Framework.Settings.Instance.PropertyChanged -= Settings_PropertyChanged;
        }
    }
}
