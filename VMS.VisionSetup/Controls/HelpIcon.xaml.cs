using VMS.VisionSetup.Models;
using System.Windows;
using System.Windows.Controls;

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
                TooltipText.Text = CustomDescription;
                CognexBorder.Visibility = Visibility.Collapsed;
                UsageBorder.Visibility = Visibility.Collapsed;
                return;
            }

            // 도구 타입이 없으면 기본 메시지
            if (string.IsNullOrEmpty(ToolType))
            {
                HelpTitle.Text = "도움말";
                HelpDescription.Text = "도움말 정보가 없습니다.";
                TooltipText.Text = "도움말 정보가 없습니다.";
                CognexBorder.Visibility = Visibility.Collapsed;
                UsageBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var toolHelp = HelpContent.GetToolHelp(ToolType);
            if (toolHelp == null)
            {
                HelpTitle.Text = ToolType;
                HelpDescription.Text = "이 도구에 대한 도움말이 아직 준비되지 않았습니다.";
                TooltipText.Text = "도움말 준비 중...";
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
                    TooltipText.Text = paramHelp.Length > 100 ? paramHelp.Substring(0, 100) + "..." : paramHelp;
                    CognexBorder.Visibility = Visibility.Collapsed;
                    UsageBorder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    HelpTitle.Text = ParameterName;
                    HelpDescription.Text = "이 파라미터에 대한 설명이 없습니다.";
                    TooltipText.Text = "설명 없음";
                    CognexBorder.Visibility = Visibility.Collapsed;
                    UsageBorder.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // 도구 전체 도움말
            HelpTitle.Text = toolHelp.Name;
            HelpDescription.Text = toolHelp.Description;
            TooltipText.Text = toolHelp.Description.Length > 100
                ? toolHelp.Description.Substring(0, 100) + "... (클릭하여 더 보기)"
                : toolHelp.Description;

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

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = !HelpPopup.IsOpen;
        }

        private void ClosePopup_Click(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = false;
        }
    }
}
