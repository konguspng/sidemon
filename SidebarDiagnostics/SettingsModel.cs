using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using SidebarDiagnostics.Utilities;
using SidebarDiagnostics.Monitoring;
using SidebarDiagnostics.Windows;
using SidebarDiagnostics.Framework;

namespace SidebarDiagnostics.Models
{
    public sealed class MappedScalarProperty
    {
        public string Name { get; }
        public PropertyInfo SettingsProperty { get; }
        public PropertyInfo ModelProperty { get; }

        internal MappedScalarProperty(string name, PropertyInfo settingsProperty, PropertyInfo modelProperty)
        {
            Name = name;
            SettingsProperty = settingsProperty;
            ModelProperty = modelProperty;
        }

        public void CopyToModel(Framework.Settings source, SettingsModel target)
        {
            ModelProperty.SetValue(target, SettingsProperty.GetValue(source));
        }

        public void CopyToSettings(SettingsModel source, Framework.Settings target)
        {
            SettingsProperty.SetValue(target, ModelProperty.GetValue(source));
        }
    }

    public class SettingsModel : INotifyPropertyChanged
    {
        // Single list of plain scalar settings (bool, int, double, string, enum) copied 1:1
        // between Framework.Settings and SettingsModel. Non-trivial settings (ScreenIndex
        // fallback, Culture, UIScale/PreferredUIScale/MaxUIScale, RunAtStartup, background
        // mode flags GlassBackground/AutoBGColor, FontSetting/FontOption, DateSetting,
        // MonitorConfig/HardwareOC, Hotkeys, and live statuses) remain explicit.
        public static readonly string[] SimpleMappedPropertyNames = new string[]
        {
            nameof(DockEdge),
            nameof(AutoFitScale),
            nameof(XOffset),
            nameof(YOffset),
            nameof(PollingInterval),
            nameof(UseAppBar),
            nameof(AlwaysTop),
            nameof(ClickThrough),
            nameof(ShowTrayIcon),
            nameof(SidebarWidth),
            nameof(BGColor),
            nameof(BGOpacity),
            nameof(GlassBlurAmount),
            nameof(BlurAmount),
            nameof(BlurStrength),
            nameof(BlurWidth),
            nameof(FeatherDirection),
            nameof(FeatherSize),
            nameof(TextAlign),
            nameof(ContentVerticalAlign),
            nameof(ShowFpsOverlay),
            nameof(FpsOverlayCorner),
            nameof(FpsOverlayOpacity),
            nameof(UseCardStyle),
            nameof(ShowAccentBars),
            nameof(AccentColor),
            nameof(FontColor),
            nameof(AlertFontColor),
            nameof(AlertBlink),
            nameof(CollapseMenuBar),
            nameof(InitiallyHidden),
            nameof(ShowMachineName),
            nameof(ShowClock),
            nameof(Clock24HR)
        };

        public static readonly MappedScalarProperty[] SimpleMappedProperties = BuildSimpleMappings();

        private static MappedScalarProperty[] BuildSimpleMappings()
        {
            Type _settingsType = typeof(Framework.Settings);
            Type _modelType = typeof(SettingsModel);
            List<MappedScalarProperty> _list = new List<MappedScalarProperty>(SimpleMappedPropertyNames.Length);

            foreach (string _name in SimpleMappedPropertyNames)
            {
                PropertyInfo _sp = _settingsType.GetProperty(_name, BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo _mp = _modelType.GetProperty(_name, BindingFlags.Public | BindingFlags.Instance);

                if (_sp == null || _mp == null || !_sp.CanRead || !_sp.CanWrite || !_mp.CanRead || !_mp.CanWrite || _sp.PropertyType != _mp.PropertyType)
                {
                    throw new InvalidOperationException("SettingsModel simple mapping mismatch for property: " + _name);
                }

                _list.Add(new MappedScalarProperty(_name, _sp, _mp));
            }

            return _list.ToArray();
        }

        public void LoadSimpleSettings(Framework.Settings source)
        {
            foreach (MappedScalarProperty _prop in SimpleMappedProperties)
            {
                _prop.CopyToModel(source, this);
            }
        }

        public void SaveSimpleSettings(Framework.Settings target)
        {
            foreach (MappedScalarProperty _prop in SimpleMappedProperties)
            {
                _prop.CopyToSettings(this, target);
            }
        }

        public SettingsModel(Sidebar sidebar)
        {
            DockEdgeItems = new DockItem[2]
            {
                new DockItem() { Text = Resources.SettingsDockLeft, Value = DockEdge.Left },
                new DockItem() { Text = Resources.SettingsDockRight, Value = DockEdge.Right }
            };

            Monitor[] _monitors = Monitor.GetMonitors();

            ScreenItems = _monitors.Select((s, i) => new ScreenItem() { Index = i, Text = string.Format("#{0}", i + 1) }).ToArray();

            if (Framework.Settings.Instance.ScreenIndex < _monitors.Length)
            {
                ScreenIndex = Framework.Settings.Instance.ScreenIndex;
            }
            else
            {
                ScreenIndex = _monitors.Select((s, i) => new { s, i }).FirstOrDefault(x => x.s.IsPrimary)?.i ?? 0;
            }

            CultureItems = Utilities.Culture.GetAll();
            Culture = Framework.Settings.Instance.Culture;

            TextAlignItems = new TextAlignItem[2]
            {
                new TextAlignItem() { Text = Resources.SettingsTextAlignLeft, Value = TextAlign.Left },
                new TextAlignItem() { Text = Resources.SettingsTextAlignRight, Value = TextAlign.Right }
            };

            ContentVerticalAlignItems = new VerticalAlignItem[3]
            {
                new VerticalAlignItem() { Text = Resources.SettingsUiVerticalAlignTop, Value = System.Windows.VerticalAlignment.Top },
                new VerticalAlignItem() { Text = Resources.SettingsUiVerticalAlignMiddle, Value = System.Windows.VerticalAlignment.Center },
                new VerticalAlignItem() { Text = Resources.SettingsUiVerticalAlignBottom, Value = System.Windows.VerticalAlignment.Bottom }
            };

            OverlayCornerItems = new OverlayCornerItem[4]
            {
                new OverlayCornerItem() { Text = Resources.SettingsUiCornerTopLeft, Value = OverlayCorner.TopLeft },
                new OverlayCornerItem() { Text = Resources.SettingsUiCornerTopRight, Value = OverlayCorner.TopRight },
                new OverlayCornerItem() { Text = Resources.SettingsUiCornerBottomLeft, Value = OverlayCorner.BottomLeft },
                new OverlayCornerItem() { Text = Resources.SettingsUiCornerBottomRight, Value = OverlayCorner.BottomRight }
            };

            FontSettingItems = new FontSetting[5]
            {
                FontSetting.x10,
                FontSetting.x12,
                FontSetting.x14,
                FontSetting.x16,
                FontSetting.x18
            };

            FontFamilyItems = SidebarFonts.All;

            DateSettingItems = new DateSetting[4]
            {
                DateSetting.Disabled,
                DateSetting.Short,
                DateSetting.Normal,
                DateSetting.Long
            };

            LoadSimpleSettings(Framework.Settings.Instance);

            UIScale = Framework.Settings.Instance.PreferredUIScale > 0d ? Framework.Settings.Instance.PreferredUIScale : Framework.Settings.Instance.UIScale;
            MaxUIScale = Framework.Settings.Instance.MaxUIScale;
            RunAtStartup = Framework.Settings.Instance.RunAtStartup;
            PawnIODriverInstalled = PawnIO.IsInstalled;
            AutoBGColor = Framework.Settings.Instance.AutoBGColor;
            GlassBackground = Framework.Settings.Instance.GlassBackground;
            RTSSDetected = SidebarDiagnostics.Overlay.RTSSReader.IsAvailable;
            FontSetting = Framework.Settings.Instance.FontSetting;
            FontOption = FontFamilyItems.FirstOrDefault(f => string.Equals(f.Name, Framework.Settings.Instance.FontFamilyName, StringComparison.OrdinalIgnoreCase)) ?? FontFamilyItems[0];
            DateSetting = Framework.Settings.Instance.DateSetting;

            ObservableCollection<MonitorConfig> _config = new ObservableCollection<MonitorConfig>(Framework.Settings.Instance.MonitorConfig.Select(c => c.Clone()).OrderByDescending(c => c.Order));

            if (sidebar.Ready)
            {
                foreach (MonitorConfig _record in _config)
                {
                    _record.HardwareOC = new ObservableCollection<HardwareConfig>(
                        from hw in sidebar.Model.MonitorManager.GetHardware(_record.Type)
                        join config in _record.Hardware on hw.ID equals config.ID into merged
                        from newhw in merged.DefaultIfEmpty(hw).Select(newhw => { newhw.ActualName = hw.ActualName; newhw.Status = hw.Status; if (_record.Type == MonitorType.AIUsage) { UsageRegistry.SetCustomPath(newhw.ID, newhw.CustomPath); newhw.Bars = UsageRegistry.GetBarToggles(newhw); newhw.Status = UsageRegistry.GetStatusText(newhw.ID); newhw.AccountStatus = UsageRegistry.GetAccountText(newhw.ID); if (UsageRegistry.IsSigningIn(newhw.ID)) newhw.RenewStatus = Resources.UsageWaitingForSignIn; } if (string.IsNullOrEmpty(newhw.Name)) { newhw.Name = hw.ActualName; } return newhw; })
                        orderby newhw.Order descending, newhw.Name ascending
                        select newhw
                        );
                }
            }
            else
            {
                // sidebar still loading: show the saved hardware list so Apply keeps it
                foreach (MonitorConfig _record in _config)
                {
                    if (_record.HardwareOC == null)
                    {
                        _record.HardwareOC = new ObservableCollection<HardwareConfig>(
                            (_record.Hardware ?? new HardwareConfig[0]).Select(h => { if (string.IsNullOrEmpty(h.Name)) { h.Name = h.ActualName; } if (_record.Type == MonitorType.AIUsage) { h.AccountStatus = UsageRegistry.GetAccountText(h.ID); } return h; }).OrderByDescending(h => h.Order));
                    }
                }
            }

            MonitorConfig = _config;
            SelectedMonitorConfig = _config.FirstOrDefault(c => c.Enabled) ?? _config.FirstOrDefault();

            if (Framework.Settings.Instance.Hotkeys != null)
            {
                ToggleKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.Toggle);
                ShowKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.Show);
                HideKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.Hide);
                ReloadKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.Reload);
                CloseKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.Close);
                CycleEdgeKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.CycleEdge);
                CycleScreenKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.CycleScreen);
                ReserveSpaceKey = Framework.Settings.Instance.Hotkeys.FirstOrDefault(k => k.Action == Hotkey.KeyAction.ReserveSpace);
            }

            IsChanged = false;
            _initialized = true;
        }

        public void Save()
        {

            bool _runAtStartupChanged = RunAtStartup != Framework.Settings.Instance.RunAtStartup;

            SaveSimpleSettings(Framework.Settings.Instance);

            Framework.Settings.Instance.ScreenIndex = ScreenIndex;
            Framework.Settings.Instance.Culture = Culture;
            Framework.Settings.Instance.UIScale = Math.Round(UIScale, 2);
            Framework.Settings.Instance.PreferredUIScale = Math.Round(UIScale, 2);
            Framework.Settings.Instance.RunAtStartup = RunAtStartup;
            Framework.Settings.Instance.AutoBGColor = AutoBGColor;
            Framework.Settings.Instance.GlassBackground = GlassBackground;
            Framework.Settings.Instance.FontSetting = FontSetting;
            Framework.Settings.Instance.FontFamilyName = FontOption != null ? FontOption.Name : SidebarFonts.DefaultName;
            Framework.Settings.Instance.DateSetting = DateSetting;

            MonitorConfig[] _config = MonitorConfig.Select(c => c.Clone()).ToArray();

            for (int i = 0; i < _config.Length; i++)
            {
                // HardwareOC is null when Settings was opened while the sidebar was still
                // loading/reloading (sidebar.Ready == false): keep the saved hardware list
                // instead of crashing on Apply (ArgumentNullException 'source').
                HardwareConfig[] _hardware = _config[i].HardwareOC != null
                    ? _config[i].HardwareOC.ToArray()
                    : (_config[i].Hardware ?? new HardwareConfig[0]);

                for (int v = 0; v < _hardware.Length; v++)
                {
                    _hardware[v].Order = Convert.ToByte(_hardware.Length - v);

                    if (string.IsNullOrEmpty(_hardware[v].Name) || string.Equals(_hardware[v].Name, _hardware[v].ActualName, StringComparison.Ordinal))
                    {
                        _hardware[v].Name = null;
                    }
                }

                _config[i].Hardware = _hardware;
                _config[i].HardwareOC = null;

                _config[i].Order = Convert.ToByte(_config.Length - i);
            }

            Framework.Settings.Instance.MonitorConfig = _config;

            List<Hotkey> _hotkeys = new List<Hotkey>();

            if (ToggleKey != null)
            {
                _hotkeys.Add(ToggleKey);
            }
            
            if (ShowKey != null)
            {
                _hotkeys.Add(ShowKey);
            }

            if (HideKey != null)
            {
                _hotkeys.Add(HideKey);
            }

            if (ReloadKey != null)
            {
                _hotkeys.Add(ReloadKey);
            }

            if (CloseKey != null)
            {
                _hotkeys.Add(CloseKey);
            }

            if (CycleEdgeKey != null)
            {
                _hotkeys.Add(CycleEdgeKey);
            }

            if (CycleScreenKey != null)
            {
                _hotkeys.Add(CycleScreenKey);
            }

            if (ReserveSpaceKey != null)
            {
                _hotkeys.Add(ReserveSpaceKey);
            }

            Framework.Settings.Instance.Hotkeys = _hotkeys.ToArray();

            Framework.Settings.Instance.Save();

            App.RefreshIcon();

            if (RunAtStartup)
            {
                bool _enabled = Startup.EnableStartupTask();

                if (_runAtStartupChanged)
                {
                    if (_enabled)
                    {
                        MessageBox.Show(Resources.SettingsUiStartupEnabledMsg, Resources.SettingsUiStartupEnabledTitle, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                    }
                    else
                    {
                        MessageBox.Show(Resources.SettingsUiStartupEnableFailedMsg, Resources.SettingsRunAtStartup, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                    }
                }
            }
            else
            {
                bool _disabled = Startup.DisableStartupTask();

                if (_runAtStartupChanged && !_disabled)
                {
                    MessageBox.Show(Resources.SettingsUiStartupDisableFailedMsg, Resources.SettingsRunAtStartup, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                }
            }

            IsChanged = false;
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            if (propertyName != "IsChanged")
            {
                IsChanged = true;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Child_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // the live provider only learns the new path on Save (the sidebar is rebuilt
            // then); until then say so instead of showing a stale detection result
            if (sender is HardwareConfig _hw && string.Equals(e.PropertyName, "CustomPath", StringComparison.Ordinal))
            {
                _hw.Status = Resources.UsageAppliesAfterSave;
            }

            IsChanged = true;
        }

        private void Child_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            IsChanged = true;
        }

        private bool _isChanged { get; set; } = false;

        public bool IsChanged
        {
            get
            {
                return _isChanged;
            }
            set
            {
                _isChanged = value;

                NotifyPropertyChanged("IsChanged");
            }
        }

        private DockEdge _dockEdge { get; set; }

        public DockEdge DockEdge
        {
            get
            {
                return _dockEdge;
            }
            set
            {
                _dockEdge = value;

                NotifyPropertyChanged("DockEdge");
            }
        }

        private DockItem[] _dockEdgeItems { get; set; }

        public DockItem[] DockEdgeItems
        {
            get
            {
                return _dockEdgeItems;
            }
            set
            {
                _dockEdgeItems = value;

                NotifyPropertyChanged("DockEdgeItems");
            }
        }

        private int _screenIndex { get; set; }

        public int ScreenIndex
        {
            get
            {
                return _screenIndex;
            }
            set
            {
                _screenIndex = value;

                NotifyPropertyChanged("ScreenIndex");
            }
        }

        private ScreenItem[] _screenItems { get; set; }

        public ScreenItem[] ScreenItems
        {
            get
            {
                return _screenItems;
            }
            set
            {
                _screenItems = value;

                NotifyPropertyChanged("ScreenItems");
            }
        }

        private string _culture { get; set; }

        public string Culture
        {
            get
            {
                return _culture;
            }
            set
            {
                _culture = value;

                NotifyPropertyChanged("Culture");
            }
        }

        private CultureItem[] _cultureItems { get; set; }

        public CultureItem[] CultureItems
        {
            get
            {
                return _cultureItems;
            }
            set
            {
                _cultureItems = value;

                NotifyPropertyChanged("CultureItems");
            }
        }

        private double _uiScale { get; set; }

        private bool _autoFitScale;

        public bool AutoFitScale
        {
            get
            {
                return _autoFitScale;
            }
            set
            {
                _autoFitScale = value;

                NotifyPropertyChanged("AutoFitScale");
            }
        }

        public double UIScale
        {
            get
            {
                return _uiScale;
            }
            set
            {
                _uiScale = value;

                NotifyPropertyChanged("UIScale");
            }
        }

        private double _maxUIScale { get; set; } = 3d;

        public double MaxUIScale
        {
            get
            {
                return _maxUIScale;
            }
            set
            {
                _maxUIScale = value;

                NotifyPropertyChanged("MaxUIScale");
                NotifyPropertyChanged("UIScaleCapped");
            }
        }

        public bool UIScaleCapped
        {
            get
            {
                return MaxUIScale < 2.999d;
            }
        }

        private int _xOffset { get; set; }

        public int XOffset
        {
            get
            {
                return _xOffset;
            }
            set
            {
                _xOffset = value;

                NotifyPropertyChanged("XOffset");
            }
        }

        private int _yOffset { get; set; }

        public int YOffset
        {
            get
            {
                return _yOffset;
            }
            set
            {
                _yOffset = value;

                NotifyPropertyChanged("YOffset");
            }
        }

        private int _pollingInterval { get; set; }

        public int PollingInterval
        {
            get
            {
                return _pollingInterval;
            }
            set
            {
                _pollingInterval = value;

                NotifyPropertyChanged("PollingInterval");
            }
        }

        private bool _useAppBar { get; set; }

        public bool UseAppBar
        {
            get
            {
                return _useAppBar;
            }
            set
            {
                _useAppBar = value;

                NotifyPropertyChanged("UseAppBar");
            }
        }

        private bool _alwaysTop { get; set; }

        public bool AlwaysTop
        {
            get
            {
                return _alwaysTop;
            }
            set
            {
                _alwaysTop = value;

                NotifyPropertyChanged("AlwaysTop");
            }
        }

        private bool _clickThrough { get; set; }

        public bool ClickThrough
        {
            get
            {
                return _clickThrough;
            }
            set
            {
                _clickThrough = value;

                NotifyPropertyChanged("ClickThrough");
            }
        }

        private bool _showTrayIcon { get; set; }

        public bool ShowTrayIcon
        {
            get
            {
                return _showTrayIcon;
            }
            set
            {
                _showTrayIcon = value;

                NotifyPropertyChanged("ShowTrayIcon");
            }
        }

        private bool _runAtStartup { get; set; }

        public bool RunAtStartup
        {
            get
            {
                return _runAtStartup;
            }
            set
            {
                _runAtStartup = value;

                NotifyPropertyChanged("RunAtStartup");
            }
        }

        // not a persisted setting: reflects live driver state so the General tab can
        // offer a manual install trigger when the automatic startup prompt was
        // missed, declined, or (as happened once) never managed to appear at all
        private bool _pawnIODriverInstalled { get; set; }

        public bool PawnIODriverInstalled
        {
            get
            {
                return _pawnIODriverInstalled;
            }
            set
            {
                _pawnIODriverInstalled = value;

                NotifyPropertyChanged("PawnIODriverInstalled");
            }
        }

        private bool _pawnIODriverInstalling { get; set; }

        public bool PawnIODriverInstalling
        {
            get
            {
                return _pawnIODriverInstalling;
            }
            set
            {
                _pawnIODriverInstalling = value;

                NotifyPropertyChanged("PawnIODriverInstalling");
            }
        }

        private bool _hasUpdate { get; set; }

        public bool HasUpdate
        {
            get { return _hasUpdate; }
            set { _hasUpdate = value; NotifyPropertyChanged("HasUpdate"); }
        }

        private string _availableUpdateText { get; set; }

        public string AvailableUpdateText
        {
            get { return _availableUpdateText; }
            set { _availableUpdateText = value; NotifyPropertyChanged("AvailableUpdateText"); }
        }

        public bool IsReportFeatureAvailable
        {
            get
            {
#if HAS_REPORT_SECRET
                return true;
#else
                return false;
#endif
            }
        }

        private int _sidebarWidth { get; set; }

        public int SidebarWidth
        {
            get
            {
                return _sidebarWidth;
            }
            set
            {
                _sidebarWidth = value;

                NotifyPropertyChanged("SidebarWidth");
            }
        }

        private bool _autoBGColor { get; set; }

        public bool AutoBGColor
        {
            get
            {
                return _autoBGColor;
            }
            set
            {
                _autoBGColor = value;

                NotifyPropertyChanged("AutoBGColor");
            }
        }

        private bool _glassBackground { get; set; }

        public bool GlassBackground
        {
            get
            {
                return _glassBackground;
            }
            set
            {
                bool _turnedOn = value && !_glassBackground;

                _glassBackground = value;

                NotifyPropertyChanged("GlassBackground");

                // a heavy tint hides the blur entirely; ease it when the user opts in
                if (_turnedOn && _initialized && BGOpacity > 0.6d)
                {
                    BGOpacity = 0.35d;
                }
            }
        }

        private bool _initialized = false;

        // one background style at a time: solid color, accent color, or desktop blur
        public bool BGModeSolid
        {
            get
            {
                return !GlassBackground && !AutoBGColor;
            }
            set
            {
                if (value)
                {
                    GlassBackground = false;
                    AutoBGColor = false;
                    NotifyBGMode();
                }
            }
        }

        public bool BGModeAccent
        {
            get
            {
                return !GlassBackground && AutoBGColor;
            }
            set
            {
                if (value)
                {
                    GlassBackground = false;
                    AutoBGColor = true;
                    NotifyBGMode();
                }
            }
        }

        public bool BGModeBlur
        {
            get
            {
                return GlassBackground;
            }
            set
            {
                if (value)
                {
                    GlassBackground = true;
                    AutoBGColor = false;

                    // glass blends into the desktop, which is incompatible with
                    // floating above other windows
                    if (AlwaysTop)
                    {
                        AlwaysTop = false;
                    }

                    NotifyBGMode();
                }
            }
        }

        private void NotifyBGMode()
        {
            NotifyPropertyChanged("BGModeSolid");
            NotifyPropertyChanged("BGModeAccent");
            NotifyPropertyChanged("BGModeBlur");
        }

        private string _bgColor { get; set; }

        public string BGColor
        {
            get
            {
                return _bgColor;
            }
            set
            {
                _bgColor = value;

                NotifyPropertyChanged("BGColor");
            }
        }

        private double _bgOpacity { get; set; }

        public double BGOpacity
        {
            get
            {
                return _bgOpacity;
            }
            set
            {
                _bgOpacity = value;

                NotifyPropertyChanged("BGOpacity");
            }
        }

        private double _glassBlurAmount { get; set; } = 0.50d;

        public double GlassBlurAmount
        {
            get
            {
                return _glassBlurAmount;
            }
            set
            {
                _glassBlurAmount = Math.Max(0d, Math.Min(1d, value));

                NotifyPropertyChanged("GlassBlurAmount");
            }
        }

        public string[] BlurAmountItems { get; } = new string[] { "Standard", "Strong" };

        private string _blurAmount { get; set; }

        public string BlurAmount
        {
            get
            {
                return _blurAmount;
            }
            set
            {
                _blurAmount = value;

                NotifyPropertyChanged("BlurAmount");
            }
        }

        private double _blurStrength { get; set; }

        public double BlurStrength
        {
            get
            {
                return _blurStrength;
            }
            set
            {
                _blurStrength = value;

                NotifyPropertyChanged("BlurStrength");
            }
        }

        private int _blurWidth { get; set; }

        public int BlurWidth
        {
            get
            {
                return _blurWidth;
            }
            set
            {
                _blurWidth = value;

                NotifyPropertyChanged("BlurWidth");
            }
        }

        public string[] FeatherDirectionItems { get; } = new string[] { "Auto", "Left", "Right", "Top", "Bottom", "None" };

        private string _featherDirection { get; set; }

        public string FeatherDirection
        {
            get
            {
                return _featherDirection;
            }
            set
            {
                _featherDirection = value;

                NotifyPropertyChanged("FeatherDirection");
            }
        }

        private double _featherSize { get; set; }

        public double FeatherSize
        {
            get
            {
                return _featherSize;
            }
            set
            {
                _featherSize = value;

                NotifyPropertyChanged("FeatherSize");
            }
        }


        private TextAlign _textAlign { get; set; }

        public TextAlign TextAlign
        {
            get
            {
                return _textAlign;
            }
            set
            {
                _textAlign = value;

                NotifyPropertyChanged("TextAlign");
            }
        }

        private TextAlignItem[] _textAlignItems { get; set; }

        public TextAlignItem[] TextAlignItems
        {
            get
            {
                return _textAlignItems;
            }
            set
            {
                _textAlignItems = value;

                NotifyPropertyChanged("TextAlignItems");
            }
        }

        private System.Windows.VerticalAlignment _contentVerticalAlign { get; set; }

        public System.Windows.VerticalAlignment ContentVerticalAlign
        {
            get
            {
                return _contentVerticalAlign;
            }
            set
            {
                _contentVerticalAlign = value;

                NotifyPropertyChanged("ContentVerticalAlign");
            }
        }

        private VerticalAlignItem[] _contentVerticalAlignItems { get; set; }

        public VerticalAlignItem[] ContentVerticalAlignItems
        {
            get
            {
                return _contentVerticalAlignItems;
            }
            set
            {
                _contentVerticalAlignItems = value;

                NotifyPropertyChanged("ContentVerticalAlignItems");
            }
        }

        private bool _showFpsOverlay { get; set; }

        public bool ShowFpsOverlay
        {
            get
            {
                return _showFpsOverlay;
            }
            set
            {
                _showFpsOverlay = value;

                NotifyPropertyChanged("ShowFpsOverlay");
            }
        }

        private OverlayCorner _fpsOverlayCorner { get; set; }

        public OverlayCorner FpsOverlayCorner
        {
            get
            {
                return _fpsOverlayCorner;
            }
            set
            {
                _fpsOverlayCorner = value;

                NotifyPropertyChanged("FpsOverlayCorner");
            }
        }

        private OverlayCornerItem[] _overlayCornerItems { get; set; }

        public OverlayCornerItem[] OverlayCornerItems
        {
            get
            {
                return _overlayCornerItems;
            }
            set
            {
                _overlayCornerItems = value;

                NotifyPropertyChanged("OverlayCornerItems");
            }
        }

        private double _fpsOverlayOpacity { get; set; }

        public double FpsOverlayOpacity
        {
            get
            {
                return _fpsOverlayOpacity;
            }
            set
            {
                _fpsOverlayOpacity = value;

                NotifyPropertyChanged("FpsOverlayOpacity");
            }
        }

        private bool _rtssDetected { get; set; }

        public bool RTSSDetected
        {
            get
            {
                return _rtssDetected;
            }
            set
            {
                _rtssDetected = value;

                NotifyPropertyChanged("RTSSDetected");
            }
        }

        private FontSetting _fontSetting { get; set; }

        public FontSetting FontSetting
        {
            get
            {
                return _fontSetting;
            }
            set
            {
                _fontSetting = value;

                NotifyPropertyChanged("FontSize");
            }
        }

        private FontSetting[] _fontSettingItems { get;  set;}

        public FontSetting[] FontSettingItems
        {
            get
            {
                return _fontSettingItems;
            }
            set
            {
                _fontSettingItems = value;

                NotifyPropertyChanged("FontSizeItems");
            }
        }

        private bool _useCardStyle { get; set; }

        public bool UseCardStyle { get { return _useCardStyle; } set { _useCardStyle = value; NotifyPropertyChanged("UseCardStyle"); NotifyPropertyChanged("AccentColorEnabled");
            }
        }

        private bool _showAccentBars { get; set; }

        public bool AccentColorEnabled => ShowAccentBars || UseCardStyle;

        public bool ShowAccentBars { get { return _showAccentBars; } set { _showAccentBars = value; NotifyPropertyChanged("ShowAccentBars"); NotifyPropertyChanged("AccentColorEnabled");
            }
        }

        private string _accentColor { get; set; }

        public string AccentColor
        {
            get
            {
                return _accentColor;
            }
            set
            {
                _accentColor = value;

                NotifyPropertyChanged("AccentColor");
            }
        }

        private FontOption _fontOption { get; set; }

        public FontOption FontOption
        {
            get
            {
                return _fontOption;
            }
            set
            {
                _fontOption = value;

                NotifyPropertyChanged("FontOption");
            }
        }

        private FontOption[] _fontFamilyItems { get; set; }

        public FontOption[] FontFamilyItems
        {
            get
            {
                return _fontFamilyItems;
            }
            set
            {
                _fontFamilyItems = value;

                NotifyPropertyChanged("FontFamilyItems");
            }
        }

        private string _fontColor { get; set; }

        public string FontColor
        {
            get
            {
                return _fontColor;
            }
            set
            {
                _fontColor = value;

                NotifyPropertyChanged("FontColor");
            }
        }

        private string _alertFontColor { get; set; }

        public string AlertFontColor
        {
            get
            {
                return _alertFontColor;
            }
            set
            {
                _alertFontColor = value;

                NotifyPropertyChanged("AlertFontColor");
            }
        }

        private bool _alertBlink { get; set; } = true;
        
        public bool AlertBlink
        {
            get
            {
                return _alertBlink;
            }
            set
            {
                _alertBlink = value;

                NotifyPropertyChanged("AlertBlink");
            }
        }

        private DateSetting _dateSetting { get; set; }

        public DateSetting DateSetting
        {
            get
            {
                return _dateSetting;
            }
            set
            {
                _dateSetting = value;

                NotifyPropertyChanged("DateSetting");
            }
        }

        private DateSetting[] _dateSettingItems { get; set; }

        public DateSetting[] DateSettingItems
        {
            get
            {
                return _dateSettingItems;
            }
            set
            {
                _dateSettingItems = value;

                NotifyPropertyChanged("DateSettingItems");
            }
        }

        private bool _collapseMenuBar { get; set; }

        public bool CollapseMenuBar
        {
            get
            {
                return _collapseMenuBar;
            }
            set
            {
                _collapseMenuBar = value;

                NotifyPropertyChanged("CollapseMenuBar");
            }
        }

        private bool _initiallyHidden { get; set; }
        
        public bool InitiallyHidden
        {
            get
            {
                return _initiallyHidden;
            }
            set
            {
                _initiallyHidden = value;

                NotifyPropertyChanged("InitiallyHidden");
            }
        }

        private bool _showMachineName { get; set; } = true;

        public bool ShowMachineName
        {
            get
            {
                return _showMachineName;
            }
            set
            {
                _showMachineName = value;

                NotifyPropertyChanged("ShowMachineName");
            }
        }

        private bool _showClock { get; set; }

        public bool ShowClock
        {
            get
            {
                return _showClock;
            }
            set
            {
                _showClock = value;

                NotifyPropertyChanged("ShowClock");
            }
        }

        private bool _clock24HR { get; set; }

        public bool Clock24HR
        {
            get
            {
                return _clock24HR;
            }
            set
            {
                _clock24HR = value;

                NotifyPropertyChanged("Clock24HR");
            }
        }

        private ObservableCollection<MonitorConfig> _monitorConfig { get; set; }

        public ObservableCollection<MonitorConfig> MonitorConfig
        {
            get
            {
                return _monitorConfig;
            }
            set
            {
                _monitorConfig = value;

                _monitorConfig.CollectionChanged += Child_CollectionChanged;

                foreach (MonitorConfig _config in _monitorConfig)
                {
                    _config.PropertyChanged += Child_PropertyChanged;

                    if (_config.HardwareOC != null)
                    {
                        _config.HardwareOC.CollectionChanged += Child_CollectionChanged;

                        foreach (HardwareConfig _hardware in _config.HardwareOC)
                        {
                            _hardware.PropertyChanged += Child_PropertyChanged;
                        }
                    }

                    foreach (MetricConfig _metric in _config.Metrics)
                    {
                        _metric.PropertyChanged += Child_PropertyChanged;
                    }

                    foreach (ConfigParam _param in _config.Params)
                    {
                        _param.PropertyChanged += Child_PropertyChanged;
                    }
                }

                NotifyPropertyChanged("MonitorConfig");
            }
        }

        private MonitorConfig _selectedMonitorConfig { get; set; }

        public MonitorConfig SelectedMonitorConfig
        {
            get
            {
                return _selectedMonitorConfig;
            }
            set
            {
                _selectedMonitorConfig = value;

                NotifyPropertyChanged("SelectedMonitorConfig");
            }
        }

        private Hotkey _toggleKey { get; set; }

        public Hotkey ToggleKey
        {
            get
            {
                return _toggleKey;
            }
            set
            {
                _toggleKey = value;

                NotifyPropertyChanged("ToggleKey");
            }
        }

        private Hotkey _showKey { get; set; }

        public Hotkey ShowKey
        {
            get
            {
                return _showKey;
            }
            set
            {
                _showKey = value;

                NotifyPropertyChanged("ShowKey");
            }
        }

        private Hotkey _hideKey { get; set; }

        public Hotkey HideKey
        {
            get
            {
                return _hideKey;
            }
            set
            {
                _hideKey = value;

                NotifyPropertyChanged("HideKey");
            }
        }

        private Hotkey _reloadKey { get; set; }

        public Hotkey ReloadKey
        {
            get
            {
                return _reloadKey;
            }
            set
            {
                _reloadKey = value;

                NotifyPropertyChanged("ReloadKey");
            }
        }

        private Hotkey _closeKey { get; set; }

        public Hotkey CloseKey
        {
            get
            {
                return _closeKey;
            }
            set
            {
                _closeKey = value;

                NotifyPropertyChanged("CloseKey");
            }
        }

        private Hotkey _cycleEdgeKey { get; set; }

        public Hotkey CycleEdgeKey
        {
            get
            {
                return _cycleEdgeKey;
            }
            set
            {
                _cycleEdgeKey = value;

                NotifyPropertyChanged("CycleEdgeKey");
            }
        }

        private Hotkey _cycleScreenKey { get; set; }

        public Hotkey CycleScreenKey
        {
            get
            {
                return _cycleScreenKey;
            }
            set
            {
                _cycleScreenKey = value;

                NotifyPropertyChanged("CycleScreenKey");
            }
        }

        private Hotkey _reserveSpaceKey { get; set; }

        public Hotkey ReserveSpaceKey
        {
            get
            {
                return _reserveSpaceKey;
            }
            set
            {
                _reserveSpaceKey = value;

                NotifyPropertyChanged("ReserveSpaceKey");
            }
        }
    }

    public class DockItem
    {
        public DockEdge Value { get; set; }

        public string Text { get; set; }
    }

    public class ScreenItem
    {
        public int Index { get; set; }

        public string Text { get; set; }
    }

    public class TextAlignItem
    {
        public TextAlign Value { get; set; }

        public string Text { get; set; }
    }

    public class VerticalAlignItem
    {
        public System.Windows.VerticalAlignment Value { get; set; }

        public string Text { get; set; }
    }

    public class OverlayCornerItem
    {
        public OverlayCorner Value { get; set; }

        public string Text { get; set; }
    }
}