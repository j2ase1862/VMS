using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace VMS.Converters
{
    /// <summary>
    /// Converts null to Visibility (null = Collapsed, non-null = Visible)
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter?.ToString() == "Invert";
            bool isNull = value == null;

            if (invert)
                return isNull ? Visibility.Visible : Visibility.Collapsed;

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts bool to Visibility
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter?.ToString() == "Invert";
            bool boolValue = value is bool b && b;

            if (invert)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool invert = parameter?.ToString() == "Invert";
                bool result = visibility == Visibility.Visible;
                return invert ? !result : result;
            }
            return false;
        }
    }

    /// <summary>
    /// Converts bool to color (running = green, stopped = gray)
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning && isRunning)
            {
                return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            }
            return new SolidColorBrush(Color.FromRgb(128, 128, 128)); // Gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
