using VMS.VisionSetup.Models;
using Xunit;
using WpfPoint = System.Windows.Point;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// RectangleAffineROI 꼭짓점 리사이즈 — 반대편 꼭짓점 고정(앵커) 방식.
    ///
    /// 과거에는 중심 고정 대칭 확장이라 한쪽 꼭짓점을 끌면 반대쪽도 같이 움직였다
    /// (2026-08-28 실증 피드백). 잡은 꼭짓점만 마우스를 따라가고 대각 반대편은
    /// 월드 좌표에서 불변이어야 한다. GetCorners 순서: [0]TL [1]TR [2]BR [3]BL.
    /// </summary>
    public class RectangleAffineROIResizeTests
    {
        private const double Eps = 1e-6;

        [Fact]
        public void AxisAligned_DragBottomRight_KeepsTopLeftFixed()
        {
            var roi = new RectangleAffineROI(200, 200, 200, 60, 0);
            // TL (100,170), BR (300,230)

            roi.ResizeByHandle(HandleType.BottomRight, new WpfPoint(340, 250));

            var c = roi.GetCorners();
            Assert.Equal(100, c[0].X, Eps);
            Assert.Equal(170, c[0].Y, Eps);
            Assert.Equal(340, c[2].X, Eps);
            Assert.Equal(250, c[2].Y, Eps);
            Assert.Equal(240, roi.Width, Eps);
            Assert.Equal(80, roi.Height, Eps);
        }

        [Theory]
        [InlineData(HandleType.TopLeft, 0, 2)]
        [InlineData(HandleType.TopRight, 1, 3)]
        [InlineData(HandleType.BottomRight, 2, 0)]
        [InlineData(HandleType.BottomLeft, 3, 1)]
        public void Rotated_DragCorner_KeepsOppositeCornerFixed(HandleType handle, int dragIdx, int anchorIdx)
        {
            var roi = new RectangleAffineROI(200, 200, 200, 60, 30);
            var before = roi.GetCorners();
            var target = new WpfPoint(before[dragIdx].X + 25, before[dragIdx].Y + 15);

            roi.ResizeByHandle(handle, target);

            var after = roi.GetCorners();
            // 반대편 꼭짓점은 월드에서 불변
            Assert.Equal(before[anchorIdx].X, after[anchorIdx].X, Eps);
            Assert.Equal(before[anchorIdx].Y, after[anchorIdx].Y, Eps);
            // 잡은 꼭짓점은 마우스 위치로 이동
            Assert.Equal(target.X, after[dragIdx].X, Eps);
            Assert.Equal(target.Y, after[dragIdx].Y, Eps);
            // 각도는 유지
            Assert.Equal(30, roi.Angle, Eps);
        }

        [Fact]
        public void MinSize_ClampStaysAnchoredAtOppositeCorner()
        {
            var roi = new RectangleAffineROI(200, 200, 200, 60, 0);
            // TL 앵커 (100,170)에 10px 이내로 붙여도 최소 10x10, 앵커는 불변

            roi.ResizeByHandle(HandleType.BottomRight, new WpfPoint(102, 172));

            var c = roi.GetCorners();
            Assert.Equal(10, roi.Width, Eps);
            Assert.Equal(10, roi.Height, Eps);
            Assert.Equal(100, c[0].X, Eps);
            Assert.Equal(170, c[0].Y, Eps);
        }
    }
}
