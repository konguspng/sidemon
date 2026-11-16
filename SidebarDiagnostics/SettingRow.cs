using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SidebarDiagnostics.Monitoring;

namespace SidebarDiagnostics
{
    /// <summary>
    /// One Windows 11 style settings card: glyph, title, one-line description, control on the right.
    /// </summary>
    public class SettingRow : HeaderedContentControl
    {
        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register("Description", typeof(string), typeof(SettingRow), new PropertyMetadata(null));

        public static readonly DependencyProperty IconProperty = DependencyProperty.Register("Icon", typeof(string), typeof(SettingRow), new PropertyMetadata(null));

        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public string Icon
        {
            get { return (string)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }
    }
}

namespace SidebarDiagnostics.Converters
{
    /// <summary>Provider id -> logo geometry (null when the provider has no logo).</summary>
    public class ProviderIconGeometryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ProviderIcons.GetGeometry(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>Provider id -> logo brush; monochrome logos use dark ink on the light settings window.</summary>
    public class ProviderIconBrushConverter : IValueConverter
    {
        private static readonly Brush _ink = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string _id = value as string;

            if (ProviderIcons.HasColor(_id))
            {
                return ProviderIcons.GetBrush(_id);
            }

            return _ink;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    /// <summary>Null/empty string -> Collapsed.</summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool _empty = value == null || (value is string _s && _s.Length == 0) || (value is int _i && _i == 0);
            return _empty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
