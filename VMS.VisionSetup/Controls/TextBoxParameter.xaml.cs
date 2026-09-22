using Microsoft.Win32;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

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
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValueChanged));

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

        // ── 글자와 값 사이의 왕복 차단 ─────────────────────────────
        //
        // 종전에는 TextBox.Text 가 Value 에 TwoWay/PropertyChanged 로 묶여 있었다.
        // Value 는 object 이고 바깥에서 double 속성에 다시 묶이므로, "23." 을 치는
        // 순간 double 23 이 되고 그 23 이 글자로 되돌아와 "23" 으로 덮였다.
        // 소수점이 지워지니 이어서 "5" 를 치면 "235" 가 된다 — 입력을 못 하는 정도가
        // 아니라 10배 틀린 값이 조용히 저장됐다 (현장 보고 2026-09-22).
        //
        // 그래서 바인딩을 끊고 방향을 나눈다:
        //   타이핑 → Value  : 글자 그대로 올려 보낸다 (변환은 바깥 바인딩이 한다)
        //   Value → 타이핑  : 입력 중(키보드 포커스)에는 덮어쓰지 않는다
        // 포커스가 떠날 때 최종 값으로 표시를 정규화한다.

        /// <summary>코드가 글자를 바꾸는 중 — TextChanged 가 값을 되쏘지 않게 한다.</summary>
        private bool _syncingText;

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((TextBoxParameter)d).SyncTextFromValue();

        /// <param name="force">
        /// true 면 지금 글자가 같은 값을 뜻하더라도 표시를 값 기준으로 다시 쓴다
        /// (입력이 끝났을 때의 정규화용 — "0023" → "23").
        /// </param>
        private void SyncTextFromValue(bool force = false)
        {
            if (Box == null) return;

            var text = Value?.ToString() ?? string.Empty;
            if (Box.Text == text) return;

            // 지금 글자가 이미 같은 값을 뜻하면 덮어쓰지 않는다.
            // "23." 을 "23" 으로 되돌리면 소수점을 이어 칠 수 없다.
            if (!force && RepresentsSameValue(Box.Text, Value)) return;

            _syncingText = true;
            try { Box.Text = text; }
            finally { _syncingText = false; }
        }

        /// <summary>글자와 값이 같은 수를 뜻하는지 (숫자 칸에서만 성립 — 그 외는 false).</summary>
        private static bool RepresentsSameValue(string text, object? value)
        {
            if (value is null || string.IsNullOrEmpty(text)) return false;

            return TryParseNumber(text, out var typed)
                && TryParseNumber(value.ToString(), out var current)
                && Math.Abs(typed - current) < 1e-12;
        }

        private static bool TryParseNumber(string? s, out double value)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            }
            value = 0;
            return false;
        }

        private void Box_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingText) return;
            SetCurrentValue(ValueProperty, (object)Box.Text);
        }

        private void Box_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // 입력이 끝났으니 실제 값 기준으로 표시를 정리한다
            // (소스가 보정한 값, "23." → "23", "0023" → "23").
            SyncTextFromValue(force: true);
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
