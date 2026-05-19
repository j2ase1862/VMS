using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Views.Common
{
    /// <summary>
    /// Chromeless 윈도우용 공통 타이틀바.
    /// SystemCommands.{Minimize,Maximize,Restore,Close}WindowCommand 를 사용 — 클래스 와이드 바인딩이 App 시작 시 등록됨.
    /// </summary>
    public partial class ChromelessTitleBar : UserControl
    {
        public ChromelessTitleBar()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TitleTextProperty =
            DependencyProperty.Register(nameof(TitleText), typeof(string), typeof(ChromelessTitleBar),
                new PropertyMetadata(string.Empty));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(ChromelessTitleBar),
                new PropertyMetadata(string.Empty, OnStatusTextChanged));

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        private static void OnStatusTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ChromelessTitleBar tb)
                tb.HasStatus = !string.IsNullOrWhiteSpace(e.NewValue as string);
        }

        public static readonly DependencyProperty HasStatusProperty =
            DependencyProperty.Register(nameof(HasStatus), typeof(bool), typeof(ChromelessTitleBar),
                new PropertyMetadata(false));

        public bool HasStatus
        {
            get => (bool)GetValue(HasStatusProperty);
            private set => SetValue(HasStatusProperty, value);
        }

        public static readonly DependencyProperty ShowMinMaxButtonsProperty =
            DependencyProperty.Register(nameof(ShowMinMaxButtons), typeof(bool), typeof(ChromelessTitleBar),
                new PropertyMetadata(true));

        public bool ShowMinMaxButtons
        {
            get => (bool)GetValue(ShowMinMaxButtonsProperty);
            set => SetValue(ShowMinMaxButtonsProperty, value);
        }
    }
}
