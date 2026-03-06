using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VMS.Models;

namespace VMS.Converters
{
    public class UserGradeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is UserGrade grade)
            {
                return grade switch
                {
                    UserGrade.Admin => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                    UserGrade.Engineer => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                    UserGrade.Operator => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
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
