using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VMS.Converters
{
    public class StatusStateToColorConverter : IValueConverter
    {
        private static readonly Brush WaitingBrush = new SolidColorBrush(Color.FromRgb(0xEB, 0x78, 0x2A));
        private static readonly Brush OkBrush = Brushes.LimeGreen;
        private static readonly Brush NgBrush = Brushes.Red;

        static StatusStateToColorConverter()
        {
            WaitingBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string state)
            {
                return state switch
                {
                    "OK" => OkBrush,
                    "NG" => NgBrush,
                    _ => WaitingBrush
                };
            }
            return WaitingBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
