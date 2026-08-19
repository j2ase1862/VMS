using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Controls
{
    public partial class ParamCodeLink : UserControl
    {
        public static readonly DependencyProperty AvailableParamCodesProperty =
            DependencyProperty.Register(nameof(AvailableParamCodes), typeof(IEnumerable), typeof(ParamCodeLink));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(ParamCodeItem), typeof(ParamCodeLink),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    static (d, _) => ((ParamCodeLink)d).UpdateBadge()));

        /// <summary>
        /// 이 링크가 붙은 파라미터의 로컬(텍스트박스/슬라이더) 값.
        /// 링크가 걸린 상태에서 Web 값과 다르면 경고 배지로 승격 — 운전은 Web 값을 쓰므로
        /// 로컬 수정이 반영되지 않는다는 사실을 즉시 드러낸다. NaN = 미바인딩(비교 생략).
        /// </summary>
        public static readonly DependencyProperty LocalValueProperty =
            DependencyProperty.Register(nameof(LocalValue), typeof(double), typeof(ParamCodeLink),
                new FrameworkPropertyMetadata(double.NaN,
                    static (d, _) => ((ParamCodeLink)d).UpdateBadge()));

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

        public double LocalValue
        {
            get => (double)GetValue(LocalValueProperty);
            set => SetValue(LocalValueProperty, value);
        }

        public ParamCodeLink()
        {
            InitializeComponent();
        }

        /// <summary>로컬 값과 Web 값이 실질적으로 다른지 (부동소수 오차만 허용하는 엄격 비교).</summary>
        internal static bool IsMismatch(double localValue, double webValue)
        {
            if (double.IsNaN(localValue)) return false;
            return Math.Abs(localValue - webValue) > Math.Max(1e-6, Math.Abs(webValue) * 1e-6);
        }

        private void UpdateBadge()
        {
            var item = SelectedItem;
            var linked = item?.ParamCode != null;

            if (!linked)
            {
                LinkBadge.Visibility = Visibility.Collapsed;
                return;
            }

            LinkBadge.Visibility = Visibility.Visible;

            var webValue = item!.Value;
            var webText = webValue.ToString("0.####", CultureInfo.InvariantCulture);

            if (IsMismatch(LocalValue, webValue))
            {
                var localText = LocalValue.ToString("0.####", CultureInfo.InvariantCulture);
                LinkBadgeText.Text = $"⚠ 로컬 {localText} ≠ Web {webText} — 운전(AUTO RUN)은 Web 값을 사용합니다";
                LinkBadgeText.Foreground = (Brush)FindResource("BrushWarning");
                LinkBadge.Background = (Brush)FindResource("BrushWarningSoft");
                LinkBadge.BorderBrush = (Brush)FindResource("BrushWarning");
                LinkBadge.ToolTip =
                    "이 파라미터는 Web 파라미터에 연동되어 있어 운전 시 매 검사마다 Web 값으로 덮어써집니다.\n" +
                    "여기서 바꾼 값은 이 화면의 테스트 실행에만 쓰입니다 — 운전 판정 기준을 바꾸려면 Web에서 수정하세요.";
            }
            else
            {
                LinkBadgeText.Text = $"Web 연동 중 (#{item.ParamCode}: {webText}) — 운전 값은 Web에서 변경";
                LinkBadgeText.Foreground = (Brush)FindResource("BrushTextMuted");
                LinkBadge.Background = (Brush)FindResource("BrushInfoSoft");
                LinkBadge.BorderBrush = (Brush)FindResource("BrushBorderSubtle");
                LinkBadge.ToolTip =
                    "이 파라미터는 Web 파라미터에 연동되어 있습니다. 운전(AUTO RUN) 판정 기준은 Web에서 수정하세요 —\n" +
                    "약 1분 내 자동 반영되며 검사 정지가 필요 없습니다.";
            }
        }
    }
}
