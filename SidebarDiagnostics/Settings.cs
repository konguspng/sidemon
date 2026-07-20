using System;
using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using SidebarDiagnostics.Utilities;
using SidebarDiagnostics.Monitoring;
using SidebarDiagnostics.Windows;

namespace SidebarDiagnostics.Framework
{
    public sealed class Settings : INotifyPropertyChanged
    {
        public Settings() { }

        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,
            IncludeFields = false
        };

        public void Save()
        {
            if (!Directory.Exists(Paths.LocalApp))
            {
                Directory.CreateDirectory(Paths.LocalApp);
            }

            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
        }

        public void Reload()
        {
            _instance = Load();
        }

        private static Settings Load()
        {
            // a corrupt or truncated settings file must never prevent startup
            try
            {
                if (File.Exists(Paths.SettingsFile))
                {
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile), JsonOptions) ?? new Settings();
                }
            }
            catch (Exception e)
            {
                ErrorLog.Write(e);
            }

            return new Settings();
        }

        public void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private string _changeLog { get; set; } = null;

        public string ChangeLog
        {
            get
            {
                return _changeLog;
            }
            set
            {
                _changeLog = value;

                NotifyPropertyChanged("ChangeLog");
            }
        }

        private bool _initialSetup { get; set; } = true;

        public bool InitialSetup
        {
            get
            {
                return _initialSetup;
            }
            set
            {
                _initialSetup = value;

                NotifyPropertyChanged("InitialSetup");
            }
        }

        private DockEdge _dockEdge { get; set; } = DockEdge.Right;

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

        private int _screenIndex { get; set; } = 0;

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

        private string _culture { get; set; } = Utilities.Culture.DEFAULT;

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

        private bool _useAppBar { get; set; } = true;
        
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

        private bool _alwaysTop { get; set; } = true;

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

        private bool _autoUpdate { get; set; } = true;

        public bool AutoUpdate
        {
            get
            {
                return _autoUpdate;
            }
            set
            {
                _autoUpdate = value;

                NotifyPropertyChanged("AutoUpdate");
            }
        }

        private bool _runAtStartup { get; set; } = true;

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

        private double _uiScale { get; set; } = 1d;

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

        private int _xOffset { get; set; } = 0;

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

        private int _yOffset { get; set; } = 0;

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

        private int _pollingInterval { get; set; } = 1000;

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

        private bool _toolbarMode { get; set; } = true;

        public bool ToolbarMode
        {
            get
            {
                return _toolbarMode;
            }
            set
            {
                _toolbarMode = value;

                NotifyPropertyChanged("ToolbarMode");
            }
        }

        private bool _clickThrough { get; set; } = false;

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

        private bool _showTrayIcon { get; set; } = true;

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

        private bool _collapseMenuBar { get; set; } = false;

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

        private bool _initiallyHidden { get; set; } = false;

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

        private int _sidebarWidth { get; set; } = 180;

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

        private bool _autoBGColor { get; set; } = false;

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

        private bool _glassBackground { get; set; } = false;

        public bool GlassBackground
        {
            get
            {
                return _glassBackground;
            }
            set
            {
                _glassBackground = value;

                NotifyPropertyChanged("GlassBackground");
            }
        }

        private string _bgColor { get; set; } = "#000000";

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

        private double _bgOpacity { get; set; } = 0.85d;

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

        private string _blurAmount { get; set; } = "Standard";

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

        private double _blurStrength { get; set; } = 20.0d;

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

        private string _featherDirection { get; set; } = "Auto";

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

        private double _featherSize { get; set; } = 10.0d;

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


        private TextAlign _textAlign { get; set; } = TextAlign.Left;

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

        private System.Windows.VerticalAlignment _contentVerticalAlign { get; set; } = System.Windows.VerticalAlignment.Top;

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

        private FontSetting _fontSetting { get; set; } = FontSetting.x14;

        public FontSetting FontSetting
        {
            get
            {
                return _fontSetting;
            }
            set
            {
                _fontSetting = value;

                NotifyPropertyChanged("FontSetting");
            }
        }

        private bool _useCardStyle { get; set; } = true;

        public bool UseCardStyle
        {
            get
            {
                return _useCardStyle;
            }
            set
            {
                _useCardStyle = value;

                NotifyPropertyChanged("UseCardStyle");
            }
        }

        private string _accentColor { get; set; } = "#22D3EE";

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

        private int _blurWidth { get; set; } = 180;

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

        private bool _checkForUpdates { get; set; } = true;

        public bool CheckForUpdates
        {
            get
            {
                return _checkForUpdates;
            }
            set
            {
                _checkForUpdates = value;

                NotifyPropertyChanged("CheckForUpdates");
            }
        }

        private string _fontFamilyName { get; set; } = SidebarFonts.DefaultName;

        public string FontFamilyName
        {
            get
            {
                return _fontFamilyName;
            }
            set
            {
                _fontFamilyName = value;

                NotifyPropertyChanged("FontFamilyName");
            }
        }

        private string _fontColor { get; set; } = "#FFFFFF";
        
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

        private string _alertFontColor { get; set; } = "#FF4136";

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

        private bool _showMachineName { get; set; } = false;

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

        private bool _showClock { get; set; } = true;

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

        private bool _clock24HR { get; set; } = false;

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

        private DateSetting _dateSetting { get; set; } = DateSetting.Short;

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

        private MonitorConfig[] _monitorConfig { get; set; } = null;

        public MonitorConfig[] MonitorConfig
        {
            get
            {
                return _monitorConfig;
            }
            set
            {
                _monitorConfig = value;

                NotifyPropertyChanged("MonitorConfig");
            }
        }

        private Hotkey[] _hotkeys { get; set; } = new Hotkey[0];

        public Hotkey[] Hotkeys
        {
            get
            {
                return _hotkeys;
            }
            set
            {
                _hotkeys = value;

                NotifyPropertyChanged("Hotkeys");
            }
        }

        private static Settings _instance { get; set; } = null;

        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }

                return _instance;
            }
        }
    }

    public enum TextAlign : byte
    {
        Left,
        Right
    }

    public sealed class FontOption
    {
        public FontOption(string name, System.Windows.Media.FontFamily family)
        {
            Name = name;
            Family = family;
        }

        public string Name { get; private set; }

        public System.Windows.Media.FontFamily Family { get; private set; }
    }

    public static class SidebarFonts
    {
        public const string DefaultName = "Segoe UI";

        private static readonly Uri _packUri = new Uri("pack://application:,,,/");

        private static FontOption[] _all;

        public static FontOption[] All
        {
            get
            {
                if (_all == null)
                {
                    _all = new FontOption[]
                    {
                        new FontOption(DefaultName, new System.Windows.Media.FontFamily(DefaultName)),
                        new FontOption("Titillium Web", new System.Windows.Media.FontFamily(_packUri, "./Fonts/#Titillium Web")),
                        new FontOption("Rajdhani", new System.Windows.Media.FontFamily(_packUri, "./Fonts/#Rajdhani")),
                        new FontOption("Chakra Petch", new System.Windows.Media.FontFamily(_packUri, "./Fonts/#Chakra Petch")),
                        new FontOption("Share Tech Mono", new System.Windows.Media.FontFamily(_packUri, "./Fonts/#Share Tech Mono"))
                    };
                }

                return _all;
            }
        }

        public static System.Windows.Media.FontFamily GetFamily(string name)
        {
            foreach (FontOption _option in All)
            {
                if (string.Equals(_option.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return _option.Family;
                }
            }

            return All[0].Family;
        }
    }

    public sealed class FontSetting
    {
        public FontSetting() { }

        private FontSetting(int fontSize)
        {
            FontSize = fontSize;
        }

        public override bool Equals(object obj)
        {
            FontSetting _that = obj as FontSetting;

            if (_that == null)
            {
                return false;
            }

            return this.FontSize == _that.FontSize;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public static FontSetting x10
        {
            get
            {
                return new FontSetting(10);
            }
        }

        public static FontSetting x12
        {
            get
            {
                return new FontSetting(12);
            }
        }

        public static FontSetting x14
        {
            get
            {
                return new FontSetting(14);
            }
        }

        public static FontSetting x16
        {
            get
            {
                return new FontSetting(16);
            }
        }

        public static FontSetting x18
        {
            get
            {
                return new FontSetting(18);
            }
        }

        public int FontSize { get; set; }

        [JsonIgnore]
        public int TitleFontSize
        {
            get
            {
                return FontSize + 2;
            }
        }

        [JsonIgnore]
        public int SmallFontSize
        {
            get
            {
                return FontSize - 2;
            }
        }

        [JsonIgnore]
        public int IconSize
        {
            get
            {
                switch (FontSize)
                {
                    case 10:
                        return 18;

                    case 12:
                        return 22;

                    case 14:
                    default:
                        return 24;

                    case 16:
                        return 28;

                    case 18:
                        return 32;
                }
            }
        }

        [JsonIgnore]
        public int BarHeight
        {
            get
            {
                return FontSize - 3;
            }
        }

        [JsonIgnore]
        public int BarWidth
        {
            get
            {
                return BarHeight * 6;
            }
        }

        [JsonIgnore]
        public int BarWidthWide
        {
            get
            {
                return BarHeight * 8;
            }
        }
    }

    public sealed class DateSetting
    {
        public DateSetting() { }

        private DateSetting(string format)
        {
            Format = format;
        }

        public string Format { get; set; }

        [JsonIgnore]
        public string Display
        {
            get
            {
                if (string.Equals(Format, "Disabled", StringComparison.Ordinal))
                {
                    return Resources.SettingsDateFormatDisabled;
                }

                return DateTime.Today.ToString(Format, Culture.CultureInfo);
            }
        }

        public override bool Equals(object obj)
        {
            DateSetting _that = obj as DateSetting;

            if (_that == null)
            {
                return false;
            }

            return string.Equals(this.Format, _that.Format, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public static readonly DateSetting Disabled = new DateSetting("Disabled");
        public static readonly DateSetting Short = new DateSetting("M");
        public static readonly DateSetting Normal = new DateSetting("d");
        public static readonly DateSetting Long = new DateSetting("D");
    }
}
