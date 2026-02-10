using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Controls
{
    public partial class EnumComboBoxParameter : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(EnumComboBoxParameter), new PropertyMetadata(""));

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(EnumComboBoxParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedValueProperty =
            DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(EnumComboBoxParameter),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ToolTypeProperty =
            DependencyProperty.Register(nameof(ToolType), typeof(string), typeof(EnumComboBoxParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty ParameterNameProperty =
            DependencyProperty.Register(nameof(ParameterName), typeof(string), typeof(EnumComboBoxParameter), new PropertyMetadata(null));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
        public object? SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }
        public string? ToolType { get => (string?)GetValue(ToolTypeProperty); set => SetValue(ToolTypeProperty, value); }
        public string? ParameterName { get => (string?)GetValue(ParameterNameProperty); set => SetValue(ParameterNameProperty, value); }

        public EnumComboBoxParameter()
        {
            InitializeComponent();
        }
    }
}
