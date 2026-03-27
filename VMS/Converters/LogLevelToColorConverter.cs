using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VMS.Interfaces;

namespace VMS.Converters
{
    public class LogLevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LogLevel level)
            {
                return level switch
                {
                    LogLevel.Info => new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                    LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                    LogLevel.Error => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                    LogLevel.Success => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    _ => new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0))
                };
            }
            return new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
