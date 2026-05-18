using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace VMS.VisionSetup.Converters
{
    /// <summary>
    /// Enum 값을 [Description] 속성 문자열로 변환. 속성 없으면 enum 이름 그대로.
    /// </summary>
    public class EnumDescriptionConverter : IValueConverter
    {
        public static readonly EnumDescriptionConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Enum e) return value?.ToString() ?? string.Empty;
            var field = e.GetType().GetField(e.ToString());
            var attr = field?.GetCustomAttribute<DescriptionAttribute>();
            return attr?.Description ?? e.ToString();
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
