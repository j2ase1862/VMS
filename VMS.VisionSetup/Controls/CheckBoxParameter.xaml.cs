using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Controls
{
    public partial class CheckBoxParameter : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(CheckBoxParameter), new PropertyMetadata(""));

        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(CheckBoxParameter),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ToolTypeProperty =
            DependencyProperty.Register(nameof(ToolType), typeof(string), typeof(CheckBoxParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty ParameterNameProperty =
            DependencyProperty.Register(nameof(ParameterName), typeof(string), typeof(CheckBoxParameter), new PropertyMetadata(null));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
        public string? ToolType { get => (string?)GetValue(ToolTypeProperty); set => SetValue(ToolTypeProperty, value); }
        public string? ParameterName { get => (string?)GetValue(ParameterNameProperty); set => SetValue(ParameterNameProperty, value); }

        public CheckBoxParameter()
        {
            InitializeComponent();
        }
    }
}
