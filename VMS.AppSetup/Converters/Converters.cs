using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VMS.Camera.Models;

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

    /// <summary>
    /// 카메라 제조사 열거형 → 화면 표시 이름("Mech_Mind" → "Mech-Mind", "Allied_Vision" → "Allied Vision").
    /// VisionSetup Camera Manager 와 같은 <see cref="CameraManufacturerNames"/> 를 쓴다.
    /// 표시 전용 — 콤보의 SelectedItem 은 열거형 값 그대로라 설정 파일 저장 값은 바뀌지 않는다.
    /// </summary>
    public class CameraManufacturerDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value switch
            {
                CameraManufacturer m => CameraManufacturerNames.ToDisplayName(m),
                null => string.Empty,
                _ => CameraManufacturerNames.ToDisplayName(value.ToString())
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// null → false, 그 외 → true. Remove 버튼 IsEnabled / 폼 IsEnabled 바인딩에 사용.
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
