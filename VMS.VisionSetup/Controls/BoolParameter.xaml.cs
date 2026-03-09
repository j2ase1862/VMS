using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Controls
{
    public partial class BoolParameter : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(BoolParameter), new PropertyMetadata(""));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(bool), typeof(BoolParameter),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ToolTypeProperty =
            DependencyProperty.Register(nameof(ToolType), typeof(string), typeof(BoolParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty ParameterNameProperty =
            DependencyProperty.Register(nameof(ParameterName), typeof(string), typeof(BoolParameter), new PropertyMetadata(null));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public bool Value { get => (bool)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string? ToolType { get => (string?)GetValue(ToolTypeProperty); set => SetValue(ToolTypeProperty, value); }
        public string? ParameterName { get => (string?)GetValue(ParameterNameProperty); set => SetValue(ParameterNameProperty, value); }

        public BoolParameter()
        {
            InitializeComponent();
        }
    }
}
