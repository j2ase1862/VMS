using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VMS.WeldTeach.Helpers;

/// <summary>bool 반전 (양방향) — 공정 모드 라디오버튼(용접=!IsGrindingMode) 바인딩용.</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : DependencyProperty.UnsetValue;
}

/// <summary>bool 반전 → Visibility — 용접 전용 툴바/패널을 그라인딩 모드에서 숨긴다.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
