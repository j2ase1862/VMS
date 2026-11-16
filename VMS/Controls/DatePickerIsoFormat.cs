using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VMS.Controls
{
    /// <summary>
    /// DatePicker 글 칸을 yyyy-MM-dd 로 보여 주는 동작 (생산 이력 창 From · To, VM-17).
    ///
    /// <para>
    /// DatePicker 는 OS 문화권의 짧은 날짜 형식을 쓴다 — 영어(미국) 윈도에서는 11/6/2026 으로 나와,
    /// 같은 창의 목록 · 제목 · 일별 표(yyyy-MM-dd)와 어긋났다. 형식 속성이 따로 없어,
    /// 날짜가 정해질 때마다 글 칸만 같은 형식으로 다시 쓴다. 입력한 "2026-11-06" 은 어느 문화권에서도 그대로 읽힌다.
    /// 화면 표시만 바꾸는 순수 UI 동작이다.
    /// </para>
    /// </summary>
    public static class DatePickerIsoFormat
    {
        public const string Format = "yyyy-MM-dd";

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(DatePickerIsoFormat),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DatePicker picker) return;

            picker.Loaded -= OnLoaded;
            picker.SelectedDateChanged -= OnSelectedDateChanged;
            if (e.NewValue is not true) return;

            picker.Loaded += OnLoaded;
            picker.SelectedDateChanged += OnSelectedDateChanged;
            if (picker.IsLoaded) Apply(picker);
        }

        private static void OnLoaded(object sender, RoutedEventArgs e) => Apply((DatePicker)sender);

        private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is DatePicker picker) Apply(picker);
        }

        private static void Apply(DatePicker picker)
        {
            if (picker.Template?.FindName("PART_TextBox", picker) is not DatePickerTextBox textBox) return;
            var text = ToText(picker.SelectedDate);
            if (text != null && !string.Equals(textBox.Text, text, StringComparison.Ordinal))
                textBox.Text = text;
        }

        /// <summary>글 칸에 쓸 문자열. 날짜가 없으면 null(건드리지 않음).</summary>
        internal static string? ToText(DateTime? date) => date?.ToString(Format, System.Globalization.CultureInfo.InvariantCulture);
    }
}
