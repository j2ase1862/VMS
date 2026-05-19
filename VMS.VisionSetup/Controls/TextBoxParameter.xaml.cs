using Microsoft.Win32;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VMS.VisionSetup.Controls
{
    /// <summary>null이 아니면 true</summary>
    public class NotNullToBoolConverter : IValueConverter
    {
        public static readonly NotNullToBoolConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value != null;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public partial class TextBoxParameter : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(TextBoxParameter), new PropertyMetadata(""));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(object), typeof(TextBoxParameter),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty ToolTypeProperty =
            DependencyProperty.Register(nameof(ToolType), typeof(string), typeof(TextBoxParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty ParameterNameProperty =
            DependencyProperty.Register(nameof(ParameterName), typeof(string), typeof(TextBoxParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty BrowseFilterProperty =
            DependencyProperty.Register(nameof(BrowseFilter), typeof(string), typeof(TextBoxParameter), new PropertyMetadata(null));

        public static readonly DependencyProperty BrowseFolderProperty =
            DependencyProperty.Register(nameof(BrowseFolder), typeof(bool), typeof(TextBoxParameter), new PropertyMetadata(false));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string? ToolType { get => (string?)GetValue(ToolTypeProperty); set => SetValue(ToolTypeProperty, value); }
        public string? ParameterName { get => (string?)GetValue(ParameterNameProperty); set => SetValue(ParameterNameProperty, value); }

        /// <summary>
        /// 파일 탐색기 필터. 설정하면 탐색 버튼이 표시됩니다.
        /// 예: "ONNX Models (*.onnx)|*.onnx|All Files (*.*)|*.*"
        /// </summary>
        public string? BrowseFilter { get => (string?)GetValue(BrowseFilterProperty); set => SetValue(BrowseFilterProperty, value); }

        /// <summary>
        /// 폴더 탐색 모드. true면 OpenFolderDialog 사용 (BrowseFilter 무시).
        /// </summary>
        public bool BrowseFolder { get => (bool)GetValue(BrowseFolderProperty); set => SetValue(BrowseFolderProperty, value); }

        public TextBoxParameter()
        {
            InitializeComponent();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            string? current = Value as string;
            if (BrowseFolder)
            {
                var dlg = new OpenFolderDialog { Title = Label ?? "폴더 선택" };
                if (!string.IsNullOrEmpty(current) && System.IO.Directory.Exists(current))
                    dlg.InitialDirectory = current;
                if (dlg.ShowDialog() == true)
                    SetCurrentValue(ValueProperty, (object)dlg.FolderName);
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = Label ?? "파일 선택",
                Filter = BrowseFilter ?? "All Files (*.*)|*.*"
            };

            if (!string.IsNullOrEmpty(current))
            {
                try { dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current) ?? ""; }
                catch { /* ignore */ }
            }

            if (dialog.ShowDialog() == true)
                SetCurrentValue(ValueProperty, (object)dialog.FileName);
        }
    }
}
