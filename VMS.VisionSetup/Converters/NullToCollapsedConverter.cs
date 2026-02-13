using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VMS.VisionSetup.Converters
{
    /// <summary>
    /// null -> Collapsed, non-null -> Visible
    /// </summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            if (parameter is string s && s == "Invert")
                isNull = !isNull;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
