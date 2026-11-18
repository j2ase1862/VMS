using System;
using System.Windows;

namespace VMS.Controls
{
    /// <summary>
    /// 카메라 타일 영역이 좁아졌을 때(설정 패널을 열었을 때 · 창이 작아졌을 때) 타일을 비례 축소해 보여 주는 규칙 (VM-11).
    ///
    /// <para>
    /// 타일은 저장된 절대 위치 · 크기(layout_config.json)로 놓인다. 종전에는 설정 패널이 열려 카메라 영역이 줄어도
    /// 타일 크기는 그대로라 오른쪽(판정 배지 · 톱니바퀴)이 패널 뒤로 가려졌다. 표시만 줄이고 저장 레이아웃은 바꾸지 않는다.
    /// 축소 중에는 타일 끌기 · 크기 조절을 쉰다 — 화면 좌표와 저장 좌표가 달라 위치가 어긋나므로, 패널을 닫으면 다시 된다.
    /// </para>
    /// </summary>
    public static class TileLayoutScale
    {
        /// <summary>
        /// 축소 배율 — 타일 전체(오른쪽 끝 · 아래 끝)가 영역 안에 들어오는 최대 배율, 1 을 넘지 않는다.
        /// 영역이나 내용 크기를 아직 모르면(0) 1.
        /// </summary>
        public static double Compute(double availableWidth, double availableHeight, double contentRight, double contentBottom)
        {
            double scale = 1.0;
            if (availableWidth > 0 && contentRight > availableWidth)
                scale = Math.Min(scale, availableWidth / contentRight);
            if (availableHeight > 0 && contentBottom > availableHeight)
                scale = Math.Min(scale, availableHeight / contentBottom);
            return scale;
        }

        /// <summary>타일이 축소 표시 중인가 — 자식 타일까지 상속된다(끌기 · 크기 조절 쉬기 판정용).</summary>
        public static readonly DependencyProperty IsScaledProperty =
            DependencyProperty.RegisterAttached(
                "IsScaled", typeof(bool), typeof(TileLayoutScale),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetIsScaled(DependencyObject obj) => (bool)obj.GetValue(IsScaledProperty);
        public static void SetIsScaled(DependencyObject obj, bool value) => obj.SetValue(IsScaledProperty, value);
    }
}
