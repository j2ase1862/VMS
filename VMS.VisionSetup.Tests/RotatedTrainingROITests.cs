using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VisionToolBase.GetAlignedROIImage — FeatureMatch 학습 ROI의 RectangleAffineROI 지원.
    ///
    /// 캔버스 규약(GetCorners, +각도=화면 CW)대로 그린 회전 ROI 내용이 수평으로 펴져
    /// 추출되는지 검증한다. 회전 부호가 반대면(과거 BlobTool 버그) 거울(−각도) 영역이
    /// 추출되어 기대 지점이 검은 화면이 된다.
    /// </summary>
    public class RotatedTrainingROITests
    {
        // ROI: 중심 (200,200), 200x60, 30° 회전(화면 CW)
        private const double Cx = 200, Cy = 200, W = 200, H = 60, AngleDeg = 30;

        [Fact]
        public void GetAlignedROIImage_RotatedROI_ExtractsDrawnRegionContent()
        {
            using var img = new Mat(400, 400, MatType.CV_8UC1, Scalar.Black);
            // 회전 폭 축 방향 +80px 지점 = 로컬 (80,0) — 회전 ROI 내부에만 존재하는 마커
            // (80·cos30, 80·sin30) = (69.3, 40) → (269, 240)
            Cv2.Circle(img, new Point(269, 240), 6, Scalar.White, -1);

            var tool = MakeTool(AngleDeg);

            using var crop = tool.GetAlignedROIImage(img);

            Assert.Equal((int)W, crop.Width);
            Assert.Equal((int)H, crop.Height);
            // 로컬 (80,0) → 크롭 좌표 (중심 100,30 기준) = (180, 30)
            Assert.True(crop.At<byte>(30, 180) > 200,
                $"크롭 (180,30) 값 {crop.At<byte>(30, 180)} — 회전 ROI 내용이 추출되지 않았다(부호 반전 의심).");
        }

        [Fact]
        public void GetAlignedROIImage_ZeroAngle_MatchesAxisAlignedCrop()
        {
            using var img = new Mat(400, 400, MatType.CV_8UC1, Scalar.Black);
            Cv2.Circle(img, new Point(280, 200), 6, Scalar.White, -1);

            var tool = MakeTool(angle: 0);

            using var crop = tool.GetAlignedROIImage(img);

            Assert.Equal((int)W, crop.Width);
            Assert.Equal((int)H, crop.Height);
            Assert.True(crop.At<byte>(30, 180) > 200);
        }

        [Fact]
        public void GetAlignedROIImage_LiveShapeAngle_TakesPrecedence()
        {
            using var img = new Mat(400, 400, MatType.CV_8UC1, Scalar.Black);
            Cv2.Circle(img, new Point(269, 240), 6, Scalar.White, -1);

            // 저장 각도는 0이지만 라이브 shape가 30° — 라이브 값을 따라야 한다
            var tool = MakeTool(angle: 0);
            tool.AssociatedROIShape = new RectangleAffineROI(Cx, Cy, W, H, AngleDeg);

            using var crop = tool.GetAlignedROIImage(img);

            Assert.True(crop.At<byte>(30, 180) > 200);
        }

        private static FeatureMatchTool MakeTool(double angle) => new()
        {
            UseROI = true,
            ROI = new Rect((int)(Cx - W / 2), (int)(Cy - H / 2), (int)W, (int)H),
            ROIAngle = angle,
            ROICenterX = Cx,
            ROICenterY = Cy
        };
    }
}
