using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// BlobTool 회전 ROI(RectangleAffineROI) 샘플링 방향 검증.
    ///
    /// 캔버스 규약: RectangleAffineROI.GetCorners()는 양의 Angle에서 화면상 시계방향(CW)으로
    /// 회전된 꼭짓점을 반환한다 (y-down 좌표계, x' = x·cos − y·sin).
    /// 툴 실행이 이 규약과 같은 영역을 분석하는지 — 즉 사용자가 그린 회전 ROI 안의 blob은
    /// 검출되고, 거울(−Angle) 영역이나 축 정렬 영역만의 blob은 검출되지 않아야 한다.
    /// </summary>
    public class BlobToolRotatedROITests
    {
        // ROI: 중심 (250,250), 240x80, 30° 회전
        private const double Cx = 250, Cy = 250, W = 240, H = 80, AngleDeg = 30;

        // 회전 폭 축 방향 +90px 지점 → 회전 ROI 내부(로컬 (90,0)),
        // 축 정렬 rect(y 210~290) 밖(y=295), 거울(−30°) ROI 밖(로컬 y≈77.9)
        private static readonly Point DotInsideRotated = new(328, 295);

        // 거울 대칭 지점 → 거울(−30°) ROI 내부, 회전 ROI 밖, 축 정렬 rect 밖(y=205)
        private static readonly Point DotInsideMirror = new(328, 205);

        private static Mat MakeImage()
        {
            var img = new Mat(500, 500, MatType.CV_8UC1, Scalar.Black);
            Cv2.Circle(img, DotInsideRotated, 8, Scalar.White, -1);
            Cv2.Circle(img, DotInsideMirror, 8, Scalar.White, -1);
            return img;
        }

        private static BlobTool MakeTool() => new()
        {
            UseROI = true,
            ROI = new Rect((int)(Cx - W / 2), (int)(Cy - H / 2), (int)W, (int)H),
            ROIAngle = AngleDeg,
            ROICenterX = Cx,
            ROICenterY = Cy,
            UseInternalThreshold = true,
            ThresholdValue = 128,
            MinArea = 50
        };

        private static void AssertDetectsDrawnRegionOnly(VisionResult result)
        {
            Assert.Equal(1, result.Data["BlobCount"]);
            double foundX = (double)result.Data["CenterX"];
            double foundY = (double)result.Data["CenterY"];
            // 검출 blob 중심이 회전 ROI 안의 점(328,295) 근처여야 한다.
            Assert.True(System.Math.Abs(foundX - DotInsideRotated.X) < 10
                     && System.Math.Abs(foundY - DotInsideRotated.Y) < 10,
                $"검출 좌표 ({foundX:F1},{foundY:F1}) — 기대: 회전 ROI 내부 dot ({DotInsideRotated.X},{DotInsideRotated.Y}). " +
                $"거울 영역 dot ({DotInsideMirror.X},{DotInsideMirror.Y})이 잡혔다면 회전 부호가 반대다.");
        }

        [Fact]
        public void RotatedROI_SavedAngle_DetectsBlobInsideDrawnRegionOnly()
        {
            // 레시피 로드 경로: AssociatedROIShape 없음 → ROIAngle/ROICenter 사용
            using var img = MakeImage();
            var tool = MakeTool();

            var result = tool.Execute(img);

            AssertDetectsDrawnRegionOnly(result);
        }

        [Fact]
        public void RotatedROI_LiveShape_DetectsBlobInsideDrawnRegionOnly()
        {
            // 라이브 편집 경로: AssociatedROIShape의 Angle 사용
            using var img = MakeImage();
            var tool = MakeTool();
            tool.AssociatedROIShape = new RectangleAffineROI(Cx, Cy, W, H, AngleDeg);

            var result = tool.Execute(img);

            AssertDetectsDrawnRegionOnly(result);
        }
    }
}
