using System.Globalization;
using System.Windows.Data;

namespace VMS.WeldTeach.Helpers;

/// <summary>ListBox AlternationIndex(0-base) → 사용자 표기 순번(1-base).</summary>
public class IndexPlusOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString() : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
