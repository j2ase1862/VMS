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

        public static readonly DependencyProperty ShowRegistryPickerProperty =
            DependencyProperty.Register(nameof(ShowRegistryPicker), typeof(bool), typeof(TextBoxParameter), new PropertyMetadata(false));

        public static readonly DependencyProperty RegistryTaskTypeProperty =
            DependencyProperty.Register(nameof(RegistryTaskType), typeof(string), typeof(TextBoxParameter), new PropertyMetadata(null));

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

        /// <summary>
        /// true 면 [레지스트리…] 버튼이 함께 뜬다. 모델 경로 칸에만 켠다.
        /// 고르면 파일 경로가 아니라 model://… 참조가 저장된다.
        /// </summary>
        public bool ShowRegistryPicker { get => (bool)GetValue(ShowRegistryPickerProperty); set => SetValue(ShowRegistryPickerProperty, value); }

        /// <summary>레지스트리 목록을 이 작업 유형으로 걸러 보여 준다 (detection·classification·anomaly·segmentation·ocr).</summary>
        public string? RegistryTaskType { get => (string?)GetValue(RegistryTaskTypeProperty); set => SetValue(RegistryTaskTypeProperty, value); }

        public TextBoxParameter()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 레지스트리에서 모델을 골라 참조를 넣는다. 사람이 GUID 를 타이핑하지 않게 하려는 것이다.
        /// 설정 확인·안내·창 열기는 DialogService 가 한다 (코드 비하인드에는 다이얼로그 로직을 두지 않는다).
        /// </summary>
        private void RegistryButton_Click(object sender, RoutedEventArgs e)
        {
            var picked = App.Dialogs?.ShowModelRegistryPickerDialog(RegistryTaskType);
            if (picked is not null) SetCurrentValue(ValueProperty, (object)picked);
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
