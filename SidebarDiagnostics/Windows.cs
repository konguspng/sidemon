using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Text.Json.Serialization;
using SidebarDiagnostics.Style;

namespace SidebarDiagnostics.Windows
{
    public enum WinOS : byte
    {
        Unknown,
        Other,
        Win7,
        Win8,
        Win8_1,
        Win10
    }

    public static class OS
    {
        private static WinOS _os { get; set; } = WinOS.Unknown;

        public static WinOS Get
        {
            get
            {
                if (_os != WinOS.Unknown)
                {
                    return _os;
                }

                Version _version = Environment.OSVersion.Version;

                if (_version.Major >= 10)
                {
                    _os = WinOS.Win10;
                }
                else if (_version.Major == 6 && _version.Minor == 3)
                {
                    _os = WinOS.Win8_1;
                }
                else if (_version.Major == 6 && _version.Minor == 2)
                {
                    _os = WinOS.Win8;
                }
                else if (_version.Major == 6 && _version.Minor == 1)
                {
                    _os = WinOS.Win7;
                }
                else
                {
                    _os = WinOS.Other;
                }

                return _os;
            }
        }

        public static bool SupportDPI
        {
            get
            {
                return OS.Get >= WinOS.Win8_1;
            }
        }

        public static bool SupportVirtualDesktop
        {
            get
            {
                return OS.Get >= WinOS.Win10;
            }
        }

        // DWMWA_SYSTEMBACKDROP_TYPE (real acrylic/mica) exists since Windows 11 22H2
        public static bool SupportSystemBackdrop
        {
            get
            {
                return Environment.OSVersion.Version.Build >= 22621;
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern long GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        internal static extern long GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        internal static extern long SetWindowLong(IntPtr hwnd, int index, long newStyle);

        [DllImport("user32.dll")]
        internal static extern long SetWindowLongPtr(IntPtr hwnd, int index, long newStyle);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwnd_after, int x, int y, int cx, int cy, uint uflags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int RegisterWindowMessage(string msg);

        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern UIntPtr SHAppBarMessage(int dwMessage, ref AppBarWindow.APPBARDATA pData);

        [DllImport("user32.dll")]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lpRect, Monitor.EnumCallback callback, int dwData);

        [DllImport("user32.dll")]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref Monitor.MONITORINFO lpmi);

        [DllImport("shcore.dll")]
        internal static extern IntPtr GetDpiForMonitor(IntPtr hmonitor, Monitor.MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr notificationFilter, int flags);

        [DllImport("user32.dll")]
        internal static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, ShowDesktop.WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, AppBarWindow.DWMWINDOWATTRIBUTE dwmAttribute, IntPtr pvAttribute, uint cbAttribute);

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    public static class ShowDesktop
    {
        private const uint WINEVENT_OUTOFCONTEXT = 0u;
        private const uint EVENT_SYSTEM_FOREGROUND = 3u;
        private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016u;
        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017u;

        private const string WORKERW = "WorkerW";
        private const string PROGMAN = "Progman";
        private const string TRAYWND = "Shell_TrayWnd";

        public static void AddHook(Sidebar sidebar)
        {
            if (IsHooked)
            {
                return;
            }

            IsHooked = true;

            _delegate = new WinEventDelegate(WinEventHook);
            _hookIntPtr = NativeMethods.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _delegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            // "Show desktop" / Win+D / Win+M minimize every window; the shell raises the desktop
            // window only AFTER that, so react to minimizes too
            _minimizeHookIntPtr = NativeMethods.SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _delegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            _sidebar = sidebar;
            _sidebarHwnd = new WindowInteropHelper(sidebar).Handle;

            // Safety net: with no app window open, the desktop must never sit above SideMon,
            // however the shell got it there (animation timing, peek, explorer restart...)
            _watchdog = new System.Windows.Threading.DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(1500) };
            _watchdog.Tick += (s, e) => WatchdogTick();
            _watchdog.Start();
        }

        public static void RemoveHook()
        {
            if (!IsHooked)
            {
                return;
            }

            IsHooked = false;

            if (_watchdog != null)
            {
                _watchdog.Stop();
                _watchdog = null;
            }

            NativeMethods.UnhookWinEvent(_hookIntPtr.Value);

            if (_minimizeHookIntPtr.HasValue)
            {
                NativeMethods.UnhookWinEvent(_minimizeHookIntPtr.Value);
                _minimizeHookIntPtr = null;
            }

            _delegate = null;
            _hookIntPtr = null;
            _sidebar = null;
            _sidebarHwnd = null;
        }

        private static string GetWindowClass(IntPtr hwnd)
        {
            StringBuilder _sb = new StringBuilder(32);
            NativeMethods.GetClassName(hwnd, _sb, _sb.Capacity);
            return _sb.ToString();
        }

        internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private static void WinEventHook(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND || eventType == EVENT_SYSTEM_MINIMIZESTART || eventType == EVENT_SYSTEM_MINIMIZEEND)
            {
                if (_sidebar == null)
                {
                    return;
                }

                // A tray overflow / jump list / menu flyout taking the foreground never
                // changes what SideMon should do, and re-applying the policy while one is
                // open is exactly when SideMon used to end up on top of it: do nothing.
                if (hwnd != IntPtr.Zero && IsTransientFlyoutClass(GetWindowClass(hwnd)))
                {
                    return;
                }

                // Re-evaluate z-order based on what's visible
                _ = LiftIfDesktopShown(false);
            }
        }

        private static bool IsShellClass(string cls)
        {
            foreach (string _shell in SHELLCLASSES)
            {
                if (string.Equals(cls, _shell, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Win+D / "show desktop" is a sequence, not an instant: windows minimize, then the shell
        // raises the desktop window some hundreds of ms later (longer on a busy machine). One
        // check at 0 and 250 ms lost that race, leaving SideMon hidden BEHIND the desktop. Check
        // repeatedly over the next few seconds; a newer trigger supersedes an older burst.
        private static readonly int[] BURST_MS = { 0, 120, 300, 600, 1000, 1800, 3000 };

        private static int _burstGeneration = 0;

        private static async System.Threading.Tasks.Task LiftIfDesktopShown(bool isDesktop)
        {
            int _generation = ++_burstGeneration;
            int _elapsed = 0;

            foreach (int _at in BURST_MS)
            {
                if (_at > _elapsed)
                {
                    await System.Threading.Tasks.Task.Delay(_at - _elapsed);
                    _elapsed = _at;
                }

                if (!IsHooked || _generation != _burstGeneration)
                {
                    return;
                }

                EvaluateDesktop(isDesktop);
            }
        }

        private static void WatchdogTick()
        {
            try
            {
                if (_sidebar == null || !_sidebarHwnd.HasValue)
                {
                    return;
                }

                if (Framework.Settings.Instance.AlwaysTop && !Framework.Settings.Instance.GlassBackground)
                {
                    return;
                }

                if (IsDesktopAboveWindow(_sidebarHwnd.Value) && !AnyNormalWindowVisible())
                {
                    _sidebar.SetTop(false);
                }
            }
            catch
            {
            }
        }

        // the nearest desktop window (Progman, or the visible WorkerW hosting the icons) above hwnd
        public static IntPtr FindDesktopAbove(IntPtr hwnd)
        {
            int _guard = 0;

            for (IntPtr _w = DesktopNative.GetWindow(hwnd, 3 /* GW_HWNDPREV */); _w != IntPtr.Zero && _guard < 4000; _w = DesktopNative.GetWindow(_w, 3), _guard++)
            {
                string _class = GetWindowClass(_w);

                if (string.Equals(_class, PROGMAN, StringComparison.Ordinal) ||
                    (string.Equals(_class, WORKERW, StringComparison.Ordinal) && DesktopNative.IsWindowVisible(_w)))
                {
                    return _w;
                }
            }

            return IntPtr.Zero;
        }

        public static IntPtr GetWindowAbove(IntPtr hwnd)
        {
            return DesktopNative.GetWindow(hwnd, 3 /* GW_HWNDPREV */);
        }

        public static bool IsTopMostWindow(IntPtr hwnd)
        {
            return (DesktopNative.GetWindowLongPtr(hwnd, -20).ToInt64() & 8L) != 0L; // WS_EX_TOPMOST
        }

        // true when the desktop window (Progman, or the visible WorkerW hosting the icons) is
        // above the given window in the z-order
        public static bool IsDesktopAboveWindow(IntPtr hwnd)
        {
            int _guard = 0;

            for (IntPtr _w = DesktopNative.GetWindow(hwnd, 3 /* GW_HWNDPREV */); _w != IntPtr.Zero && _guard < 4000; _w = DesktopNative.GetWindow(_w, 3), _guard++)
            {
                string _class = GetWindowClass(_w);

                if (string.Equals(_class, PROGMAN, StringComparison.Ordinal) ||
                    (string.Equals(_class, WORKERW, StringComparison.Ordinal) && DesktopNative.IsWindowVisible(_w)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EvaluateDesktop(bool isDesktop)
        {
            if (_sidebar == null)
            {
                return;
            }

            _sidebar.ApplyZOrderPolicy();
        }

        // short-lived shell surfaces: tray overflow ("^" arrow), jump lists / thumbnails,
        // popup menus (tray icon context menus use #32768)
        private static readonly string[] TRANSIENTFLYOUTS = { "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Xaml_WindowedPopupClass", "#32768", "TaskListThumbnailWnd", "TaskListOverlayWnd", "Shell_InputSwitchTopLevelWindow", "DV2ControlHost" };

        private static readonly string[] SHELLPROCESSES = { "explorer", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp", "textinputhost", "shellhost", "lockapp" };

        private static bool IsTransientFlyoutClass(string cls)
        {
            foreach (string _c in TRANSIENTFLYOUTS)
            {
                if (string.Equals(cls, _c, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsShellProcess(uint pid)
        {
            try
            {
                using (System.Diagnostics.Process _p = System.Diagnostics.Process.GetProcessById((int)pid))
                {
                    return Array.IndexOf(SHELLPROCESSES, _p.ProcessName.ToLowerInvariant()) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        // The LOWEST window in the topmost band that belongs to the Windows shell: the
        // taskbar, the tray overflow flyout, Start/Search, jump lists, tray menus. A
        // topmost SideMon must be inserted directly BELOW it, never above any shell
        // surface (HWND_TOPMOST would put it above all of them). Zero when none is open.
        public static IntPtr FindLowestTopmostShellWindow(IntPtr ownHwnd)
        {
            IntPtr _found = IntPtr.Zero;

            try
            {
                uint _ownPid = (uint)Environment.ProcessId;

                DesktopNative.EnumWindows((hwnd, lparam) =>
                {
                    try
                    {
                        if (hwnd == ownHwnd || !DesktopNative.IsWindowVisible(hwnd))
                        {
                            return true;
                        }

                        long _exstyle = DesktopNative.GetWindowLongPtr(hwnd, -20).ToInt64();

                        if ((_exstyle & 8L) == 0L) // not WS_EX_TOPMOST
                        {
                            return true;
                        }

                        DesktopNative.GetWindowThreadProcessId(hwnd, out uint _pid);

                        if (_pid == _ownPid) // never anchor to our own windows (FPS overlay...)
                        {
                            return true;
                        }

                        string _class = GetWindowClass(hwnd);

                        if (!IsShellClass(_class) && !IsTransientFlyoutClass(_class) && !IsShellProcess(_pid))
                        {
                            return true;
                        }

                        int _cloaked = 0;
                        try { DesktopNative.DwmGetWindowAttribute(hwnd, 14, out _cloaked, sizeof(int)); } catch { }

                        if (_cloaked != 0)
                        {
                            return true;
                        }

                        RECT _rect;

                        if (!NativeMethods.GetWindowRect(hwnd, out _rect) || _rect.Right - _rect.Left <= 10 || _rect.Bottom - _rect.Top <= 10 || _rect.Left <= -30000 || _rect.Top <= -30000)
                        {
                            return true;
                        }

                        _found = hwnd; // EnumWindows walks top to bottom: the last match is the lowest
                    }
                    catch
                    {
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }

            return _found;
        }

        private static readonly string[] SHELLCLASSES = { WORKERW, PROGMAN, TRAYWND, "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow" };

        public static bool AnyNormalWindowVisible()
        {
            bool _found = false;

            DesktopNative.EnumWindows((hwnd, lparam) =>
            {
                if (_sidebarHwnd.HasValue && hwnd == _sidebarHwnd.Value)
                {
                    return true;
                }

                if (!DesktopNative.IsWindowVisible(hwnd) || DesktopNative.IsIconic(hwnd))
                {
                    return true;
                }

                long _exstyle = DesktopNative.GetWindowLongPtr(hwnd, -20).ToInt64();

                if ((_exstyle & 128L) != 0L) // WS_EX_TOOLWINDOW
                {
                    return true;
                }

                string _class = GetWindowClass(hwnd);

                foreach (string _shell in SHELLCLASSES)
                {
                    if (string.Equals(_class, _shell, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                // skip DWM-cloaked ghosts (suspended UWP apps, other virtual desktops)
                int _cloaked = 0; try { DesktopNative.DwmGetWindowAttribute(hwnd, 14, out _cloaked, sizeof(int)); } catch { }

                if (_cloaked != 0)
                {
                    return true;
                }

                // check rect to ignore 0x0 or off-screen / minimized windows
                RECT _rect;
                if (NativeMethods.GetWindowRect(hwnd, out _rect))
                {
                    if (_rect.Right <= _rect.Left || _rect.Bottom <= _rect.Top)
                    {
                        return true;
                    }

                    if (_rect.Right - _rect.Left <= 10 || _rect.Bottom - _rect.Top <= 10)
                    {
                        return true;
                    }

                    if (_rect.Left <= -30000 || _rect.Top <= -30000)
                    {
                        return true;
                    }
                }

                _found = true;
                return false;
            }, IntPtr.Zero);

            return _found;
        }

        private static class DesktopNative
        {
            public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);

            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
            public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

            [DllImport("user32.dll")]
            public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

            [DllImport("dwmapi.dll")]
            public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        }

        public static bool IsHooked { get; private set; } = false;

        private static IntPtr? _hookIntPtr { get; set; }

        private static WinEventDelegate _delegate { get; set; }

        private static Sidebar _sidebar { get; set; }

        private static IntPtr? _sidebarHwnd { get; set; }

        private static IntPtr? _minimizeHookIntPtr { get; set; }

        private static System.Windows.Threading.DispatcherTimer _watchdog { get; set; }
    }

    public static class Devices
    {
        private const int WM_DEVICECHANGE = 0x0219;

        private static class DBCH_DEVICETYPE
        {
            public const int DBT_DEVTYP_DEVICEINTERFACE = 5;
            public const int DBT_DEVTYP_HANDLE = 6;
            public const int DBT_DEVTYP_OEM = 0;
            public const int DBT_DEVTYP_PORT = 3;
            public const int DBT_DEVTYP_VOLUME = 2;
        }

        private static class FLAGS
        {
            public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
            public const int DEVICE_NOTIFY_SERVICE_HANDLE = 1;
            public const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 4;
        }

        private static class WM_DEVICECHANGE_EVENT
        {
            public const int DBT_CONFIGCHANGECANCELED = 0x0019;
            public const int DBT_CONFIGCHANGED = 0x0018;
            public const int DBT_CUSTOMEVENT = 0x8006;
            public const int DBT_DEVICEARRIVAL = 0x8000;
            public const int DBT_DEVICEQUERYREMOVE = 0x8001;
            public const int DBT_DEVICEQUERYREMOVEFAILED = 0x8002;
            public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
            public const int DBT_DEVICEREMOVEPENDING = 0x8003;
            public const int DBT_DEVICETYPESPECIFIC = 0x8005;
            public const int DBT_DEVNODES_CHANGED = 0x0007;
            public const int DBT_QUERYCHANGECONFIG = 0x0017;
            public const int DBT_USERDEFINED = 0xFFFF;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEV_BROADCAST_HDR
        {
            public int dbch_size;
            public int dbch_devicetype;
            public int dbch_reserved;
        }

        public static void AddHook(Sidebar window)
        {
            if (IsHooked)
            {
                return;
            }

            IsHooked = true;

            DEV_BROADCAST_HDR _data = new DEV_BROADCAST_HDR();
            _data.dbch_size = Marshal.SizeOf(_data);
            _data.dbch_devicetype = DBCH_DEVICETYPE.DBT_DEVTYP_DEVICEINTERFACE;

            IntPtr _buffer = Marshal.AllocHGlobal(_data.dbch_size);
            Marshal.StructureToPtr(_data, _buffer, true);

            IntPtr _hwnd = new WindowInteropHelper(window).Handle;

            NativeMethods.RegisterDeviceNotification(
                _hwnd,
                _buffer,
                FLAGS.DEVICE_NOTIFY_ALL_INTERFACE_CLASSES
                );

            window.HwndSource.AddHook(DeviceHook);
        }

        public static void RemoveHook(Sidebar window)
        {
            if (!IsHooked)
            {
                return;
            }

            IsHooked = false;

            window.HwndSource.RemoveHook(DeviceHook);
        }

        private static IntPtr DeviceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                switch (wParam.ToInt32())
                {
                    case WM_DEVICECHANGE_EVENT.DBT_DEVICEARRIVAL:
                    case WM_DEVICECHANGE_EVENT.DBT_DEVICEREMOVECOMPLETE:

                        if (_cancelRestart != null)
                        {
                            _cancelRestart.Cancel();
                        }

                        _cancelRestart = new CancellationTokenSource();

                        Task.Delay(TimeSpan.FromSeconds(1), _cancelRestart.Token).ContinueWith(_ =>
                        {
                            if (_.IsCanceled)
                            {
                                return;
                            }

                            App.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(() =>
                            {
                                Sidebar _sidebar = App.Current.Sidebar;

                                if (_sidebar != null)
                                {
                                    _sidebar.ContentReload();
                                }
                            }));

                            _cancelRestart = null;
                        });
                        break;
                }

                handled = true;
            }

            return IntPtr.Zero;
        }

        public static bool IsHooked { get; private set; } = false;

        private static CancellationTokenSource _cancelRestart { get; set; }
    }

    public class Hotkey
    {
        private const int WM_HOTKEY = 0x0312;

        private static class MODIFIERS
        {
            public const uint MOD_NOREPEAT = 0x4000;
            public const uint MOD_ALT = 0x0001;
            public const uint MOD_CONTROL = 0x0002;
            public const uint MOD_SHIFT = 0x0004;
            public const uint MOD_WIN = 0x0008;
        }

        public enum KeyAction : byte
        {
            Toggle,
            Show,
            Hide,
            Reload,
            Close,
            CycleEdge,
            CycleScreen,
            ReserveSpace
        }

        public Hotkey() { }

        public Hotkey(int index, KeyAction action, uint virtualKey, bool altMod = false, bool ctrlMod = false, bool shiftMod = false, bool winMod = false)
        {
            Index = index;
            Action = action;
            VirtualKey = virtualKey;
            AltMod = altMod;
            CtrlMod = ctrlMod;
            ShiftMod = shiftMod;
            WinMod = winMod;
        }

        public KeyAction Action { get; set; }

        public uint VirtualKey { get; set; }

        public bool AltMod { get; set; }

        public bool CtrlMod { get; set; }

        public bool ShiftMod { get; set; }

        public bool WinMod { get; set; }

        [JsonIgnore]
        public Key WinKey
        {
            get
            {
                return KeyInterop.KeyFromVirtualKey((int)VirtualKey);
            }
            set
            {
                VirtualKey = (uint)KeyInterop.VirtualKeyFromKey(value);
            }
        }

        private int Index { get; set; }

        public static void Initialize(Sidebar window, Hotkey[] settings)
        {
            if (settings == null || settings.Length == 0)
            {
                Dispose();
                return;
            }

            Disable();

            _sidebar = window;
            _index = 0;

            RegisteredKeys = settings.Select(h =>
            {
                h.Index = _index;
                _index++;
                return h;
            }).ToArray();

            window.HwndSource.AddHook(KeyHook);

            IsHooked = true;
        }

        public static void Dispose()
        {
            if (!IsHooked)
            {
                return;
            }

            IsHooked = false;

            Disable();

            RegisteredKeys = null;

            _sidebar.HwndSource.RemoveHook(KeyHook);
            _sidebar = null;
        }

        public static void Enable()
        {
            if (RegisteredKeys == null)
            {
                return;
            }

            foreach (Hotkey _hotkey in RegisteredKeys)
            {
                Register(_hotkey);
            }
        }

        public static void Disable()
        {
            if (RegisteredKeys == null)
            {
                return;
            }

            foreach (Hotkey _hotkey in RegisteredKeys)
            {
                Unregister(_hotkey);
            }
        }

        private static void Register(Hotkey hotkey)
        {
            uint _mods = MODIFIERS.MOD_NOREPEAT;

            if (hotkey.AltMod)
            {
                _mods |= MODIFIERS.MOD_ALT;
            }

            if (hotkey.CtrlMod)
            {
                _mods |= MODIFIERS.MOD_CONTROL;
            }

            if (hotkey.ShiftMod)
            {
                _mods |= MODIFIERS.MOD_SHIFT;
            }

            if (hotkey.WinMod)
            {
                _mods |= MODIFIERS.MOD_WIN;
            }

            NativeMethods.RegisterHotKey(
                new WindowInteropHelper(_sidebar).Handle,
                hotkey.Index,
                _mods,
                hotkey.VirtualKey
                );
        }

        private static void Unregister(Hotkey hotkey)
        {
            NativeMethods.UnregisterHotKey(
                new WindowInteropHelper(_sidebar).Handle,
                hotkey.Index
                );
        }

        public static Hotkey[] RegisteredKeys { get; private set; }

        private static IntPtr KeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int _id = wParam.ToInt32();

                Hotkey _hotkey = RegisteredKeys.FirstOrDefault(k => k.Index == _id);

                if (_hotkey != null && _sidebar != null && _sidebar.Ready)
                {
                    switch (_hotkey.Action)
                    {
                        case KeyAction.Toggle:
                            if (_sidebar.Visibility == Visibility.Visible)
                            {
                                _sidebar.AppBarHide();
                            }
                            else
                            {
                                _ = _sidebar.AppBarShow();
                            }
                            break;

                        case KeyAction.Show:
                            _ = _sidebar.AppBarShow();
                            break;

                        case KeyAction.Hide:
                            _sidebar.AppBarHide();
                            break;

                        case KeyAction.Reload:
                            _sidebar.Reload();
                            break;

                        case KeyAction.Close:
                            App.Current.Shutdown();
                            break;

                        case KeyAction.CycleEdge:
                            if (_sidebar.Visibility == Visibility.Visible)
                            {
                                switch (Framework.Settings.Instance.DockEdge)
                                {
                                    case DockEdge.Right:
                                        Framework.Settings.Instance.DockEdge = DockEdge.Left;
                                        break;

                                    default:
                                    case DockEdge.Left:
                                        Framework.Settings.Instance.DockEdge = DockEdge.Right;
                                        break;
                                }

                                Framework.Settings.Instance.Save();

                                _ = _sidebar.Reposition();
                            }
                            break;

                        case KeyAction.CycleScreen:
                            if (_sidebar.Visibility == Visibility.Visible)
                            {
                                Monitor[] _monitors = Monitor.GetMonitors();

                                if (Framework.Settings.Instance.ScreenIndex < (_monitors.Length - 1))
                                {
                                    Framework.Settings.Instance.ScreenIndex++;
                                }
                                else
                                {
                                    Framework.Settings.Instance.ScreenIndex = 0;
                                }

                                Framework.Settings.Instance.Save();

                                _ = _sidebar.Reposition();
                            }
                            break;

                        case KeyAction.ReserveSpace:
                            Framework.Settings.Instance.UseAppBar = !Framework.Settings.Instance.UseAppBar;
                            Framework.Settings.Instance.Save();

                            _ = _sidebar.Reposition();
                            break;
                    }

                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public static bool IsHooked { get; private set; } = false;

        private static Sidebar _sidebar { get; set; }

        private static int _index { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width
        {
            get
            {
                return Right - Left;
            }
        }

        public int Height
        {
            get
            {
                return Bottom - Top;
            }
        }
    }

    public class WorkArea
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Right { get; set; }

        public double Bottom { get; set; }

        public double Width
        {
            get
            {
                return Right - Left;
            }
        }

        public double Height
        {
            get
            {
                return Bottom - Top;
            }
        }

        public void Scale(double x, double y)
        {
            Left *= x;
            Top *= y;
            Right *= x;
            Bottom *= y;
        }

        public void Offset(double x, double y)
        {
            Left += x;
            Top += y;
            Right += x;
            Bottom += y;
        }

        public void SetWidth(DockEdge edge, double width)
        {
            switch (edge)
            {
                case DockEdge.Left:
                    Right = Left + width;
                    break;

                case DockEdge.Right:
                    Left = Right - width;
                    break;
            }
        }

        public static WorkArea FromRECT(RECT rect)
        {
            return new WorkArea()
            {
                Left = rect.Left,
                Top = rect.Top,
                Right = rect.Right,
                Bottom = rect.Bottom
            };
        }
    }

    public class Monitor
    {
        private const uint DPICONST = 96u;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public int cbSize;
            public RECT Size;
            public RECT WorkArea;
            public bool IsPrimary;
        }

        internal enum MONITOR_DPI_TYPE : int
        {
            MDT_EFFECTIVE_DPI = 0,
            MDT_ANGULAR_DPI = 1,
            MDT_RAW_DPI = 2,
            MDT_DEFAULT = MDT_EFFECTIVE_DPI
        }

        public RECT Size { get; set; }

        public RECT WorkArea { get; set; }

        public double DPIx { get; set; }

        public double ScaleX
        {
            get
            {
                return DPIx / DPICONST;
            }
        }

        public double InverseScaleX
        {
            get
            {
                return 1 / ScaleX;
            }
        }

        public double DPIy { get; set; }

        public double ScaleY
        {
            get
            {
                return DPIy / DPICONST;
            }
        }

        public double InverseScaleY
        {
            get
            {
                return 1 / ScaleY;
            }
        }

        public bool IsPrimary { get; set; }

        internal delegate bool EnumCallback(IntPtr hDesktop, IntPtr hdc, ref RECT pRect, int dwData);

        public static Monitor GetMonitor(IntPtr hMonitor)
        {
            MONITORINFO _info = new MONITORINFO();
            _info.cbSize = Marshal.SizeOf(_info);

            NativeMethods.GetMonitorInfo(hMonitor, ref _info);

            uint _dpiX = Monitor.DPICONST;
            uint _dpiY = Monitor.DPICONST;

            if (OS.SupportDPI)
            {
                NativeMethods.GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out _dpiX, out _dpiY);
            }

            return new Monitor()
            {
                Size = _info.Size,
                WorkArea = _info.WorkArea,
                DPIx = _dpiX,
                DPIy = _dpiY,
                IsPrimary = _info.IsPrimary
            };
        }

                public static Monitor[] GetMonitors()
        {
            List<Monitor> _monitors = new List<Monitor>();

            try {
                EnumCallback _callback = (IntPtr hMonitor, IntPtr hdc, ref RECT pRect, int dwData) =>
                {
                    try { _monitors.Add(GetMonitor(hMonitor)); } catch {}
                    return true;
                };

                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _callback, 0);
            } catch (Exception ex) { Utilities.ErrorLog.Write(ex); }

            if (_monitors.Count == 0)
            {
                _monitors.Add(new Monitor[] { }.GetPrimary());
            }

            return _monitors.OrderByDescending(m => m.IsPrimary).ToArray();
        }

        public static Monitor GetMonitorFromIndex(int index)
        {
            return GetMonitorFromIndex(index, GetMonitors());
        }

                private static Monitor GetMonitorFromIndex(int index, Monitor[] monitors)
        {
            if (index >= 0 && index < monitors.Length)
                return monitors[index];
            else
                return monitors.GetPrimary();
        }

        public static void GetWorkArea(AppBarWindow window, out int screen, out DockEdge edge, out WorkArea initPos, out WorkArea windowWA, out WorkArea appbarWA)
        {
            screen = Framework.Settings.Instance.ScreenIndex;
            edge = Framework.Settings.Instance.DockEdge;

            double _uiScale = Framework.Settings.Instance.UIScale;

            if (OS.SupportDPI)
            {
                window.UpdateScale(_uiScale, _uiScale, false);
            }

            Monitor[] _monitors = GetMonitors();

            Monitor _primary = _monitors.GetPrimary();
            Monitor _active = GetMonitorFromIndex(screen, _monitors);

            initPos = new WorkArea()
            {
                Top = _active.WorkArea.Top,
                Left = _active.WorkArea.Left,
                Bottom = _active.WorkArea.Top + 10,
                Right = _active.WorkArea.Left + 10
            };

            windowWA = Windows.WorkArea.FromRECT(_active.WorkArea);
            windowWA.Scale(_active.InverseScaleX, _active.InverseScaleY);

            double _modifyX = 0d;
            double _modifyY = 0d;

            windowWA.Offset(_modifyX, _modifyY);

            double _windowWidth = Framework.Settings.Instance.SidebarWidth * _uiScale;

            windowWA.SetWidth(edge, _windowWidth);

            int _offsetX = Framework.Settings.Instance.XOffset;
            int _offsetY = Framework.Settings.Instance.YOffset;

            windowWA.Offset(_offsetX, _offsetY);

            appbarWA = Windows.WorkArea.FromRECT(_active.WorkArea);

            appbarWA.Offset(_modifyX, _modifyY);

            // reserve only the sidebar content width, never the extra blur area
            double _appbarWidth = Framework.Settings.Instance.UseAppBar ? (Framework.Settings.Instance.SidebarWidth * _uiScale) * _active.ScaleX : 0;

            appbarWA.SetWidth(edge, _appbarWidth);

            appbarWA.Offset(_offsetX, _offsetY);
        }
    }

    public static class MonitorExtensions
    {
                public static Monitor GetPrimary(this Monitor[] monitors)
        {
            if (monitors == null || monitors.Length == 0)
            {
                return new Monitor()
                {
                    Size = new RECT { Right = 1920, Bottom = 1080 },
                    WorkArea = new RECT { Right = 1920, Bottom = 1080 },
                    DPIx = 96,
                    DPIy = 96,
                    IsPrimary = true
                };
            }
            return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        }
    }

    public partial class DPIAwareWindow : FlatWindow
    {
        private static class WM_MESSAGES
        {
            public const int WM_DPICHANGED = 0x02E0;
            public const int WM_GETMINMAXINFO = 0x0024;
            public const int WM_SIZE = 0x0005;
            public const int WM_WINDOWPOSCHANGING = 0x0046;
            public const int WM_WINDOWPOSCHANGED = 0x0047;
        }

        public override void BeginInit()
        {
            Utilities.Culture.SetCurrent(false);

            base.BeginInit();
        }

        public override void EndInit()
        {
            base.EndInit();

            _originalWidth = base.Width;
            _originalHeight = base.Height;

            if (AutoDPI && OS.SupportDPI)
            {
                Loaded += DPIAwareWindow_Loaded;
            }
        }

        public void HandleDPI()
        {
            //IntPtr _hwnd = new WindowInteropHelper(this).Handle;

            //IntPtr _hmonitor = NativeMethods.MonitorFromWindow(_hwnd, 0);

            //Monitor _monitorInfo = Monitor.GetMonitor(_hmonitor);

            double _uiScale = Framework.Settings.Instance.UIScale;

            UpdateScale(_uiScale, _uiScale, true);
        }

        public void UpdateScale(double scaleX, double scaleY, bool resize)
        {
            if (VisualChildrenCount > 0)
            {
                GetVisualChild(0).SetValue(LayoutTransformProperty, new ScaleTransform(scaleX, scaleY));
            }

            if (resize)
            {
                SizeToContent _autosize = SizeToContent;
                SizeToContent = SizeToContent.Manual;

                base.Width = _originalWidth * scaleX;
                base.Height = _originalHeight * scaleY;

                SizeToContent = _autosize;
            }
        }

        private void DPIAwareWindow_Loaded(object sender, RoutedEventArgs e)
        {
            HandleDPI();

            Framework.Settings.Instance.PropertyChanged += UIScale_PropertyChanged;

            //HwndSource.AddHook(WindowHook);
        }

        private void UIScale_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "UIScale")
            {
                HandleDPI();
            }
        }

        //private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        //{
        //    if (msg == WM_MESSAGES.WM_DPICHANGED)
        //    {
        //        HandleDPI();

        //        handled = true;
        //    }

        //    return IntPtr.Zero;
        //}

        public HwndSource HwndSource
        {
            get
            {
                return (HwndSource)PresentationSource.FromVisual(this);
            }
        }

        public static readonly DependencyProperty AutoDPIProperty = DependencyProperty.Register("AutoDPI", typeof(bool), typeof(DPIAwareWindow), new UIPropertyMetadata(true));

        public bool AutoDPI
        {
            get
            {
                return (bool)GetValue(AutoDPIProperty);
            }
            set
            {
                SetValue(AutoDPIProperty, value);
            }
        }

        public new double Width
        {
            get
            {
                return base.Width;
            }
            set
            {
                _originalWidth = base.Width = value;
            }
        }

        public new double Height
        {
            get
            {
                return base.Height;
            }
            set
            {
                _originalHeight = base.Height = value;
            }
        }

        private double _originalWidth { get; set; }

        private double _originalHeight { get; set; }
    }

    [Serializable]
    public enum DockEdge : byte
    {
        Left,
        Top,
        Right,
        Bottom,
        None
    }

    public partial class AppBarWindow : DPIAwareWindow
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        private static class APPBARMSG
        {
            public const int ABM_NEW = 0;
            public const int ABM_REMOVE = 1;
            public const int ABM_QUERYPOS = 2;
            public const int ABM_SETPOS = 3;
            public const int ABM_GETSTATE = 4;
            public const int ABM_GETTASKBARPOS = 5;
            public const int ABM_ACTIVATE = 6;
            public const int ABM_GETAUTOHIDEBAR = 7;
            public const int ABM_SETAUTOHIDEBAR = 8;
            public const int ABM_WINDOWPOSCHANGED = 9;
            public const int ABM_SETSTATE = 10;
        }

        private static class APPBARNOTIFY
        {
            public const int ABN_STATECHANGE = 0;
            public const int ABN_POSCHANGED = 1;
            public const int ABN_FULLSCREENAPP = 2;
            public const int ABN_WINDOWARRANGE = 3;
        }

        internal enum DWMWINDOWATTRIBUTE : int
        {
            DWMWA_NCRENDERING_ENABLED = 1,
            DWMWA_NCRENDERING_POLICY = 2,
            DWMWA_TRANSITIONS_FORCEDISABLED = 3,
            DWMWA_ALLOW_NCPAINT = 4,
            DWMWA_CAPTION_BUTTON_BOUNDS = 5,
            DWMWA_NONCLIENT_RTL_LAYOUT = 6,
            DWMWA_FORCE_ICONIC_REPRESENTATION = 7,
            DWMWA_FLIP3D_POLICY = 8,
            DWMWA_EXTENDED_FRAME_BOUNDS = 9,
            DWMWA_HAS_ICONIC_BITMAP = 10,
            DWMWA_DISALLOW_PEEK = 11,
            DWMWA_EXCLUDED_FROM_PEEK = 12,
            DWMWA_CLOAK = 13,
            DWMWA_CLOAKED = 14,
            DWMWA_FREEZE_REPRESENTATION = 15,
            DWMWA_LAST = 16,
            DWMWA_SYSTEMBACKDROP_TYPE = 38
        }

        private static class HWND_FLAG
        {
            public static readonly IntPtr HWND_TOP = IntPtr.Zero;
            public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
            public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
            public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_NOACTIVATE = 0x0010;
        }

        private static class WND_STYLE
        {
            public const int GWL_EXSTYLE = -20;

            public const long WS_EX_TRANSPARENT = 32;
            public const long WS_EX_TOOLWINDOW = 128;
            public const long WS_EX_APPWINDOW = 0x00040000;
            public const long WS_EX_NOACTIVATE = 0x08000000;
        }

        private static class WM_MESSAGES
        {
            public const int WM_ACTIVATE = 0x0006;
            public const int WM_SHOWWINDOW = 0x0018;
            public const int WM_SETTINGCHANGE = 0x001A;
            public const int WM_MOUSEACTIVATE = 0x0021;
            public const int WM_WINDOWPOSCHANGING = 0x0046;
            public const int WM_DISPLAYCHANGE = 0x007E;
            public const int WM_SYSCOMMAND = 0x0112;
            public const int SC_MINIMIZE = 0xF020;
            public const int MA_NOACTIVATE = 3;
        }

        private static class WM_WINDOWPOSCHANGING
        {
            public const int MSG = 0x0046;
            public const uint SWP_NOSIZE = 0x0001;
            public const uint SWP_NOMOVE = 0x0002;
            public const uint SWP_SHOWWINDOW = 0x0040;
            public const uint SWP_HIDEWINDOW = 0x0080;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPOS
        {
            public IntPtr hWnd;
            public IntPtr hWndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            Loaded += AppBarWindow_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // The sidebar is a desktop widget, never a task the user switches to, so
            // it is always a tool window: out of Alt+Tab / the task switcher, off the
            // taskbar, and shown on every virtual desktop. This must happen here,
            // while the HWND exists but the window has not been shown yet - Windows
            // only reads a window's Alt+Tab eligibility on the hidden -> shown
            // transition, so setting WS_EX_TOOLWINDOW after the first Show() (as the
            // old BindSettings path did) left the sidebar in the switcher for the
            // whole session.
            IsInAltTab = false;
            SetWindowLong(WND_STYLE.WS_EX_TOOLWINDOW | WND_STYLE.WS_EX_NOACTIVATE, WND_STYLE.WS_EX_APPWINDOW);
        }

        private void AppBarWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PreventMove();
        }

        public void Move(WorkArea workArea)
        {
            _canMove = true;

            try
            {
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;
            }
            finally
            {
                _canMove = false;
            }
        }

        private void PreventMove()
        {
            _canMove = false;

            if (!_hookRegistered)
            {
                _hookRegistered = true;
                HwndSource.AddHook(MoveHook);
            }
        }

        private void AllowMove()
        {
            _canMove = true;
        }

        private IntPtr MoveHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WINDOWPOSCHANGING.MSG)
            {
                WINDOWPOS _pos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));

                if (!_canMove)
                {
                    _pos.flags |= WM_WINDOWPOSCHANGING.SWP_NOMOVE;
                }

                if (!_isHiding)
                {
                    // Never allow the shell or other apps to hide or minimize the sidebar (e.g. on Win+D)
                    _pos.flags &= ~WM_WINDOWPOSCHANGING.SWP_HIDEWINDOW;

                    // If moved off-screen (e.g. -32000, -32000 on Win+D minimize), keep position and size
                    if (_pos.x <= -30000 || _pos.y <= -30000)
                    {
                        _pos.flags |= WM_WINDOWPOSCHANGING.SWP_NOMOVE | WM_WINDOWPOSCHANGING.SWP_NOSIZE;
                    }
                }

                Marshal.StructureToPtr(_pos, lParam, true);

                handled = true;
            }
            else if (msg == WM_MESSAGES.WM_SYSCOMMAND)
            {
                if ((wParam.ToInt32() & 0xFFF0) == WM_MESSAGES.SC_MINIMIZE)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            else if (msg == WM_MESSAGES.WM_SHOWWINDOW)
            {
                if (wParam == IntPtr.Zero && !_isHiding)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            else if (msg == WM_MESSAGES.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(WM_MESSAGES.MA_NOACTIVATE);
            }
            else if (msg == WM_MESSAGES.WM_ACTIVATE)
            {
                handled = true;
                return IntPtr.Zero;
            }
            else if (msg == WM_MESSAGES.WM_DISPLAYCHANGE || msg == WM_MESSAGES.WM_SETTINGCHANGE || msg == _taskbarCreatedMsg || (msg == 0x021B && wParam.ToInt32() == 0x0012))
            {
                if (this is Sidebar sidebar)
                {
                    sidebar.ApplyZOrderPolicy();
                    if (msg == WM_MESSAGES.WM_DISPLAYCHANGE || msg == WM_MESSAGES.WM_SETTINGCHANGE)
                    {
                        _ = sidebar.Reposition();
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static int _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        public void SetTopMost(bool activate)
        {
            IsTopMost = true;

            // Never plain HWND_TOPMOST: that lands above the taskbar and every tray flyout
            // (the "^" overflow popup was drawn UNDER SideMon). Insert directly beneath the
            // lowest shell window instead; this is a single call, so there is no flicker.
            IntPtr _shell = ShowDesktop.FindLowestTopmostShellWindow(new WindowInteropHelper(this).Handle);

            SetPos(_shell != IntPtr.Zero ? _shell : HWND_FLAG.HWND_TOPMOST, activate);
        }

        public void ClearTopMost(bool activate)
        {
            IsTopMost = false;

            SetPos(HWND_FLAG.HWND_NOTOPMOST, activate);
        }

        public void SetBottom(bool activate)
        {
            IsTopMost = false;

            SetPos(HWND_FLAG.HWND_NOTOPMOST, activate);
            SetPos(HWND_FLAG.HWND_BOTTOM, activate);
        }

        // top of the normal band only: stays above the bare desktop but below the
        // taskbar, tray flyouts, and anything genuinely topmost
        public void SetTop(bool activate)
        {
            IsTopMost = true;

            // leave the topmost band first so HWND_TOP means "top of the normal windows",
            // which is always below the taskbar and every tray flyout
            SetPos(HWND_FLAG.HWND_NOTOPMOST, activate);
            SetPos(HWND_FLAG.HWND_TOP, activate);

            // While the desktop is shown (Win+D) the shell keeps the desktop window at the very
            // top of the normal band and SetWindowPos(HWND_TOP) reports success without moving
            // us above it. Insert directly above the desktop window instead.
            IntPtr _self = new WindowInteropHelper(this).Handle;
            IntPtr _desktop = ShowDesktop.FindDesktopAbove(_self);

            if (_desktop != IntPtr.Zero)
            {
                IntPtr _after = ShowDesktop.GetWindowAbove(_desktop);

                if (_after == IntPtr.Zero)
                {
                    SetPos(HWND_FLAG.HWND_TOP, activate);
                }
                else if (!ShowDesktop.IsTopMostWindow(_after))
                {
                    SetPos(_after, activate);
                }
            }
        }

        private void SetPos(IntPtr hwnd_after, bool activate)
        {
            uint _uflags = HWND_FLAG.SWP_NOMOVE | HWND_FLAG.SWP_NOSIZE | HWND_FLAG.SWP_NOACTIVATE;

            try { NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, hwnd_after, 0, 0, 0, 0, _uflags); } catch { }
        }

        public void SetClickThrough()
        {
            if (IsClickThrough)
            {
                return;
            }

            IsClickThrough = true;

            SetWindowLong(WND_STYLE.WS_EX_TRANSPARENT, null);
        }

        public void ClearClickThrough()
        {
            if (!IsClickThrough)
            {
                return;
            }

            IsClickThrough = false;

            SetWindowLong(null, WND_STYLE.WS_EX_TRANSPARENT);
        }

        public void HideInAltTab()
        {
            if (!IsInAltTab)
            {
                return;
            }

            IsInAltTab = false;

            SetWindowLong(WND_STYLE.WS_EX_TOOLWINDOW | WND_STYLE.WS_EX_NOACTIVATE, WND_STYLE.WS_EX_APPWINDOW);
        }

        private static class DWMSBT
        {
            public const int AUTO = 0;
            public const int NONE = 1;
            public const int MAINWINDOW = 2;   // mica
            public const int TRANSIENTWINDOW = 3;   // acrylic
            public const int TABBEDWINDOW = 4;
        }

        // Real Windows acrylic (ACCENT_ENABLE_ACRYLICBLURBEHIND = 4) through the compositor,
        // the same live backdrop blur the taskbar and Start menu use. Verified on a layered
        // (AllowsTransparency) window: it blurs what is truly behind, including while the
        // window is inactive. Needs Windows 10 1803+ (build 17134).
        public static bool GlassSupported
        {
            get
            {
                return Environment.OSVersion.Version.Build >= 17134;
            }
        }

        public void SetGlass(System.Windows.Media.Color tint, double opacity)
        {
            IntPtr _hwnd = new WindowInteropHelper(this).Handle;

            byte _alpha = (byte)Math.Max(1d, Math.Min(255d, Math.Round(opacity * 255d)));

            var accent = new AccentPolicy();
            accent.AccentState = 4; // ACCENT_ENABLE_ACRYLICBLURBEHIND
            accent.AccentFlags = 0; // NOT 2: the border flag forces an opaque black result and makes the alpha channel (tint strength) ignored
            accent.GradientColor = (_alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R; // AABBGGRR

            ApplyAccent(_hwnd, accent);
        }

        public void ClearGlass()
        {
            IntPtr _hwnd = new WindowInteropHelper(this).Handle;

            var accent = new AccentPolicy();
            accent.AccentState = 0; // ACCENT_DISABLED

            ApplyAccent(_hwnd, accent);
        }

        private static void ApplyAccent(IntPtr hwnd, AccentPolicy accent)
        {
            var accentStructSize = Marshal.SizeOf(accent);
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData();
                data.Attribute = 19; // WCA_ACCENT_POLICY
                data.SizeOfData = accentStructSize;
                data.Data = accentPtr;

                try { NativeMethods.SetWindowCompositionAttribute(hwnd, ref data); } catch { }
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }


        public void DisableAeroPeek()
        {
            IntPtr _hwnd = new WindowInteropHelper(this).Handle;

                        try {
                IntPtr _status = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(_status, 1);
                NativeMethods.DwmSetWindowAttribute(_hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK, _status, sizeof(int));
            } catch { }
        }

        private void SetWindowLong(long? add, long? remove)
        {
            IntPtr _hwnd = new WindowInteropHelper(this).Handle;

            bool _32bit = IntPtr.Size == 4;

            long _style;

            if (_32bit)
            {
                _style = NativeMethods.GetWindowLong(_hwnd, WND_STYLE.GWL_EXSTYLE);
            }
            else
            {
                _style = NativeMethods.GetWindowLongPtr(_hwnd, WND_STYLE.GWL_EXSTYLE);
            }

            if (add.HasValue)
            {
                _style |= add.Value;
            }

            if (remove.HasValue)
            {
                _style &= ~remove.Value;
            }

            if (_32bit)
            {
                NativeMethods.SetWindowLong(_hwnd, WND_STYLE.GWL_EXSTYLE, _style);
            }
            else
            {
                NativeMethods.SetWindowLongPtr(_hwnd, WND_STYLE.GWL_EXSTYLE, _style);
            }
        }

        // awaits resume on the dispatcher (WPF sync context), so the whole sequence
        // stays on the UI thread without the old fire-and-forget ContinueWith chain
        public async Task SetAppBar()
        {
            if (_settingAppBar)
            {
                return;
            }

            _settingAppBar = true;

            try
            {
                ClearAppBar();

                await Task.Delay(100);

                await BindAppBar();
            }
            finally
            {
                _settingAppBar = false;
            }
        }

        private async Task BindAppBar()
        {
            Monitor.GetWorkArea(this, out int screen, out DockEdge edge, out WorkArea initPos, out WorkArea windowWA, out WorkArea appbarWA);

            Move(initPos);

            APPBARDATA _data = NewData();

            try { _callbackID = _data.uCallbackMessage = NativeMethods.RegisterWindowMessage("AppBarMessage"); NativeMethods.SHAppBarMessage(APPBARMSG.ABM_NEW, ref _data); } catch { }

            Screen = screen;
            DockEdge = edge;

            _data.uEdge = (int)edge;
            _data.rc = new RECT()
            {
                Left = (int)Math.Round(appbarWA.Left),
                Top = (int)Math.Round(appbarWA.Top),
                Right = (int)Math.Round(appbarWA.Right),
                Bottom = (int)Math.Round(appbarWA.Bottom)
            };

                        try {
                NativeMethods.SHAppBarMessage(APPBARMSG.ABM_QUERYPOS, ref _data);
                NativeMethods.SHAppBarMessage(APPBARMSG.ABM_SETPOS, ref _data);
            } catch { }

            IsAppBar = true;

            appbarWA.Left = _data.rc.Left;
            appbarWA.Top = _data.rc.Top;
            appbarWA.Right = _data.rc.Right;
            appbarWA.Bottom = _data.rc.Bottom;

            AppBarWidth = appbarWA.Width;

            await Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(() =>
            {
                Move(windowWA);
            }));

            await Task.Delay(500);

            HwndSource.AddHook(AppBarHook);
        }

        public void ClearAppBar()
        {
            if (!IsAppBar)
            {
                return;
            }

            HwndSource.RemoveHook(AppBarHook);

            try { APPBARDATA _data = NewData(); NativeMethods.SHAppBarMessage(APPBARMSG.ABM_REMOVE, ref _data); } catch { }

            IsAppBar = false;
        }

        // raised when the shell reports a fullscreen app appearing/leaving this screen
        protected virtual void OnFullScreenAppChanged(bool active) { }

        public virtual async Task AppBarShow()
        {
            _isHiding = false;

            if (Framework.Settings.Instance.UseAppBar)
            {
                await SetAppBar();
            }

            Show();
        }

        public virtual void AppBarHide()
        {
            _isHiding = true;

            try
            {
                Hide();

                if (IsAppBar)
                {
                    ClearAppBar();
                }
            }
            finally
            {
                _isHiding = false;
            }
        }

        private APPBARDATA NewData()
        {
            APPBARDATA _data = new APPBARDATA();
            _data.cbSize = Marshal.SizeOf(_data);
            _data.hWnd = new WindowInteropHelper(this).Handle;

            return _data;
        }

        private IntPtr AppBarHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _callbackID)
            {
                switch (wParam.ToInt32())
                {
                    case APPBARNOTIFY.ABN_POSCHANGED:
                        _ = SetAppBar();
                        break;

                    case APPBARNOTIFY.ABN_FULLSCREENAPP:
                        if (lParam.ToInt32() == 1)
                        {
                            _wasTopMost = IsTopMost;

                            if (IsTopMost)
                            {
                                SetBottom(false);
                            }

                            OnFullScreenAppChanged(true);
                        }
                        else
                        {
                            if (_wasTopMost)
                            {
                                SetTopMost(false);
                            }

                            OnFullScreenAppChanged(false);
                        }
                        break;
                }

                handled = true;
            }

            return IntPtr.Zero;
        }

        public bool IsTopMost { get; private set; } = false;

        public bool IsClickThrough { get; private set; } = false;

        public bool IsInAltTab { get; private set; } = true;

        public bool IsAppBar { get; private set; } = false;

        public int Screen { get; private set; } = 0;

        public DockEdge DockEdge { get; private set; } = DockEdge.None;

        public double AppBarWidth { get; private set; } = 0;

        protected bool _isHiding { get; set; } = false;

        private bool _canMove { get; set; } = false;

        private bool _hookRegistered { get; set; } = false;

        private bool _wasTopMost { get; set; } = false;

        private bool _settingAppBar { get; set; } = false;

        private int _callbackID { get; set; }

        private CancellationTokenSource _cancelReposition { get; set; }
    }
}