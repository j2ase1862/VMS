using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VMS.AppSetup.Converters
{
    /// <summary>
    /// 페이지 번호를 Visibility로 변환
    /// </summary>
    public class PageToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int currentPage && parameter is string pageStr)
            {
                if (int.TryParse(pageStr, out int targetPage))
                {
                    return currentPage == targetPage ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Enum 값을 bool로 변환 (RadioButton 바인딩용)
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string enumValue = value.ToString()!;
            string targetValue = parameter.ToString()!;

            return enumValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter != null)
            {
                return Enum.Parse(targetType, parameter.ToString()!);
            }
            return Binding.DoNothing;
        }
    }
}
