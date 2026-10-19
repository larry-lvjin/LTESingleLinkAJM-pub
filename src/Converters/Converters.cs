using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LTESingleLinkAJM.Models;

namespace LTESingleLinkAJM.Converters
{
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TestStatus s)
            {
                return s switch
                {
                    TestStatus.Running   => new SolidColorBrush(Color.FromRgb(0x29, 0xB6, 0xF6)),
                    TestStatus.Completed => new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),
                    TestStatus.Failed    => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                    TestStatus.Skipped   => new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26)),
                    _                    => new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE))
                };
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x33, 0x29, 0xB6, 0xF6));
        public Brush FalseBrush { get; set; } = Brushes.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? TrueBrush : FalseBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>true -> Green dot brush, false -> Red dot brush</summary>
    public class BoolToConnectionBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b)
                ? new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A))
                : new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>true -> Visible, false -> Collapsed</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
