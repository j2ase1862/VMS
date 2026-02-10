using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VMS.VisionSetup.Controls
{
    public class ValueFormatConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is double val && values[1] is string fmt)
                return val.ToString(fmt);
            return values[0]?.ToString() ?? "";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class SliderParameter : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderParameter), new PropertyMetadata(""));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(SliderParameter),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SliderParameter), new PropertyMetadata(0.0));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SliderParameter), new PropertyMetadata(100.0));

        public static readonly DependencyProperty TickFrequencyProperty =
            DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(SliderParameter), new PropertyMetadata(1.0));

        public static readonly DependencyProperty SnapToTickProperty =
            DependencyProperty.Register(nameof(SnapToTick), typeof(bool), typeof(SliderParameter), new PropertyMetadata(false));

        public static readonly DependencyProperty ValueFormatProperty =
            DependencyProperty.Register(nameof(ValueFormat), typeof(string), typeof(SliderParameter), new PropertyMetadata("F0"));

        public static readonly DependencyProperty ToolTypeProperty =
            DependencyProperty.Register(nameof(ToolType), typeof(string), typeof(SliderParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty ParameterNameProperty =
            DependencyProperty.Register(nameof(ParameterName), typeof(string), typeof(SliderParameter), new PropertyMetadata(null));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double TickFrequency { get => (double)GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }
        public bool SnapToTick { get => (bool)GetValue(SnapToTickProperty); set => SetValue(SnapToTickProperty, value); }
        public string ValueFormat { get => (string)GetValue(ValueFormatProperty); set => SetValue(ValueFormatProperty, value); }
        public string? ToolType { get => (string?)GetValue(ToolTypeProperty); set => SetValue(ToolTypeProperty, value); }
        public string? ParameterName { get => (string?)GetValue(ParameterNameProperty); set => SetValue(ParameterNameProperty, value); }

        public SliderParameter()
        {
            InitializeComponent();
        }
    }
}
