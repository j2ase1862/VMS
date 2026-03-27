using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;

namespace VMS.VisionSetup.Views.Sequence
{
    public partial class SequenceNodePropertiesView : UserControl
    {
        /// <summary>InputCheckMode enum values for ComboBox</summary>
        public static Array InputCheckModes => Enum.GetValues(typeof(InputCheckMode));

        /// <summary>PlcDataType enum values for ComboBox</summary>
        public static Array PlcDataTypes => Enum.GetValues(typeof(PlcDataType));

        public SequenceNodePropertiesView()
        {
            InitializeComponent();
        }
    }

    /// <summary>
    /// 노드 타입에 따라 해당 설정 패널 Visibility를 결정하는 컨버터
    /// ConverterParameter에 보여줄 NodeType 이름을 지정
    /// </summary>
    public class NodeTypeVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SequenceNodeType nodeType && parameter is string expected)
            {
                return nodeType.ToString() == expected ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
