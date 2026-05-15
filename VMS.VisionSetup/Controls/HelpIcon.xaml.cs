using VMS.VisionSetup.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VMS.VisionSetup.Controls
{
    /// <summary>
    /// 도움말 아이콘 컨트롤
    /// 클릭 시 도구/파라미터에 대한 상세 설명을 팝업으로 표시
    /// </summary>
    public partial class HelpIcon : UserControl
    {
        #region Dependency Properties

        /// <summary>
        /// 도구 타입 (예: "BlurTool", "ThresholdTool")
        /// </summary>
        public static readonly DependencyProperty ToolTypeProperty =
            DependencyProperty.Register(nameof(ToolType), typeof(string), typeof(HelpIcon),
                new PropertyMetadata(null, OnHelpContentChanged));

        public string? ToolType
        {
            get => (string?)GetValue(ToolTypeProperty);
            set => SetValue(ToolTypeProperty, value);
        }

        /// <summary>
        /// 파라미터 이름 (null이면 도구 전체 설명)
        /// </summary>
        public static readonly DependencyProperty ParameterNameProperty =
            DependencyProperty.Register(nameof(ParameterName), typeof(string), typeof(HelpIcon),
                new PropertyMetadata(null, OnHelpContentChanged));

        public string? ParameterName
        {
            get => (string?)GetValue(ParameterNameProperty);
            set => SetValue(ParameterNameProperty, value);
        }

        /// <summary>
        /// 커스텀 제목 (설정 시 기본 제목 대신 사용)
        /// </summary>
        public static readonly DependencyProperty CustomTitleProperty =
            DependencyProperty.Register(nameof(CustomTitle), typeof(string), typeof(HelpIcon),
                new PropertyMetadata(null, OnHelpContentChanged));

        public string? CustomTitle
        {
            get => (string?)GetValue(CustomTitleProperty);
            set => SetValue(CustomTitleProperty, value);
        }

        /// <summary>
        /// 커스텀 설명 (설정 시 HelpContent 대신 사용)
        /// </summary>
        public static readonly DependencyProperty CustomDescriptionProperty =
            DependencyProperty.Register(nameof(CustomDescription), typeof(string), typeof(HelpIcon),
                new PropertyMetadata(null, OnHelpContentChanged));

        public string? CustomDescription
        {
            get => (string?)GetValue(CustomDescriptionProperty);
            set => SetValue(CustomDescriptionProperty, value);
        }

        #endregion

        public HelpIcon()
        {
            InitializeComponent();
            UpdateHelpContent();
        }

        private static void OnHelpContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HelpIcon helpIcon)
            {
                helpIcon.UpdateHelpContent();
            }
        }

        private void UpdateHelpContent()
        {
            // 커스텀 설명이 있으면 사용
            if (!string.IsNullOrEmpty(CustomDescription))
            {
                HelpTitle.Text = CustomTitle ?? "도움말";
                HelpDescription.Text = CustomDescription;
                CognexBorder.Visibility = Visibility.Collapsed;
                UsageBorder.Visibility = Visibility.Collapsed;
                return;
            }

            // 도구 타입이 없으면 기본 메시지
            if (string.IsNullOrEmpty(ToolType))
            {
                HelpTitle.Text = "도움말";
                HelpDescription.Text = "도움말 정보가 없습니다.";
                CognexBorder.Visibility = Visibility.Collapsed;
                UsageBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var toolHelp = HelpContent.GetToolHelp(ToolType);
            if (toolHelp == null)
            {
                HelpTitle.Text = ToolType;
                HelpDescription.Text = "이 도구에 대한 도움말이 아직 준비되지 않았습니다.";
                CognexBorder.Visibility = Visibility.Collapsed;
                UsageBorder.Visibility = Visibility.Collapsed;
                return;
            }

            // 파라미터 도움말인 경우
            if (!string.IsNullOrEmpty(ParameterName))
            {
                var paramHelp = HelpContent.GetParameterHelp(ToolType, ParameterName);
                if (!string.IsNullOrEmpty(paramHelp))
                {
                    HelpTitle.Text = ParameterName;
                    HelpDescription.Text = paramHelp;
                    CognexBorder.Visibility = Visibility.Collapsed;
                    UsageBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    HelpTitle.Text = ParameterName;
                    HelpDescription.Text = "이 파라미터에 대한 설명이 없습니다.";
                    CognexBorder.Visibility = Visibility.Collapsed;
                    UsageBorder.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // 도구 전체 도움말
            HelpTitle.Text = toolHelp.Name;
            HelpDescription.Text = toolHelp.Description;

            // Cognex 동등 도구 표시
            if (!string.IsNullOrEmpty(toolHelp.CognexEquivalent))
            {
                CognexText.Text = toolHelp.CognexEquivalent;
                CognexBorder.Visibility = Visibility.Visible;
            }
            else
            {
                CognexBorder.Visibility = Visibility.Collapsed;
            }

            // 사용 예시 표시
            if (!string.IsNullOrEmpty(toolHelp.Usage))
            {
                UsageText.Text = toolHelp.Usage;
                UsageBorder.Visibility = Visibility.Visible;
            }
            else
            {
                UsageBorder.Visibility = Visibility.Collapsed;
            }
        }

        private bool _isExpanded;
        private const double DefaultMaxHeight = 600;
        private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(300);
        private DispatcherTimer? _openTimer;
        private DispatcherTimer? _closeTimer;

        /// <summary>? 아이콘 진입 — delay 후 Popup 열기. close 타이머는 즉시 취소.</summary>
        private void HelpButton_MouseEnter(object sender, MouseEventArgs e)
        {
            _closeTimer?.Stop();
            if (HelpPopup.IsOpen) return;

            _openTimer ??= new DispatcherTimer { Interval = HoverDelay };
            _openTimer.Tick -= OpenTimer_Tick;
            _openTimer.Tick += OpenTimer_Tick;
            _openTimer.Start();
        }

        private void OpenTimer_Tick(object? sender, EventArgs e)
        {
            _openTimer?.Stop();
            if (HelpButton.IsMouseOver || HelpBorder.IsMouseOver)
                HelpPopup.IsOpen = true;
        }

        /// <summary>? 아이콘에서 떠남 — open 타이머 취소 후 close 타이머 시작.</summary>
        private void HelpButton_MouseLeave(object sender, MouseEventArgs e)
        {
            _openTimer?.Stop();
            StartCloseTimer();
        }

        /// <summary>Popup 영역 진입 — close 타이머 취소 (마우스가 ?에서 Popup으로 이동).</summary>
        private void HelpBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            _closeTimer?.Stop();
        }

        /// <summary>Popup에서 떠남 — close 타이머 시작.</summary>
        private void HelpBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            StartCloseTimer();
        }

        private void StartCloseTimer()
        {
            _closeTimer ??= new DispatcherTimer { Interval = HoverDelay };
            _closeTimer.Tick -= CloseTimer_Tick;
            _closeTimer.Tick += CloseTimer_Tick;
            _closeTimer.Start();
        }

        private void CloseTimer_Tick(object? sender, EventArgs e)
        {
            _closeTimer?.Stop();
            // 두 영역 어디에도 마우스가 없으면 닫음
            if (!HelpButton.IsMouseOver && !HelpBorder.IsMouseOver)
                ClosePopupAndReset();
        }

        /// <summary>X 버튼 — 즉시 닫기 + 타이머 정리 + 확장 상태 리셋.</summary>
        private void CloseHelp_Click(object sender, RoutedEventArgs e)
        {
            _openTimer?.Stop();
            _closeTimer?.Stop();
            ClosePopupAndReset();
        }

        private void ClosePopupAndReset()
        {
            HelpPopup.IsOpen = false;
            HelpButton.IsChecked = false;
            // 다음 열 때 축소 상태로 시작
            if (_isExpanded)
            {
                _isExpanded = false;
                HelpScroll.MaxHeight = DefaultMaxHeight;
            }
        }

        /// <summary>
        /// WPF Popup 안에서는 ScrollViewer의 PreviewMouseWheel이 발동되지 않을 수 있어,
        /// 부모 Border가 휠 이벤트를 받아 ScrollViewer로 직접 위임.
        /// </summary>
        private void HelpBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            HelpScroll.ScrollToVerticalOffset(HelpScroll.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        /// <summary>
        /// 팝업 본문을 좌클릭하면 확장 ↔ 축소 토글.
        /// X 닫기 버튼이나 다른 Button 위에서 시작된 이벤트는 자체 Click이 처리하도록 skip.
        /// </summary>
        private void HelpBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOriginatingFromButton(e.OriginalSource)) return;

            _isExpanded = !_isExpanded;
            HelpScroll.MaxHeight = _isExpanded
                ? SystemParameters.PrimaryScreenHeight * 0.85
                : DefaultMaxHeight;
        }

        private static bool IsOriginatingFromButton(object? source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is System.Windows.Controls.Primitives.ButtonBase) return true;
                current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                    : System.Windows.LogicalTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
