using System.Collections;
using System.Windows;
using System.Windows.Controls;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Controls
{
    public partial class ParamCodeLink : UserControl
    {
        public static readonly DependencyProperty AvailableParamCodesProperty =
            DependencyProperty.Register(nameof(AvailableParamCodes), typeof(IEnumerable), typeof(ParamCodeLink));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(ParamCodeItem), typeof(ParamCodeLink),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public IEnumerable? AvailableParamCodes
        {
            get => (IEnumerable?)GetValue(AvailableParamCodesProperty);
            set => SetValue(AvailableParamCodesProperty, value);
        }

        public ParamCodeItem? SelectedItem
        {
            get => (ParamCodeItem?)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public ParamCodeLink()
        {
            InitializeComponent();
        }
    }
}
