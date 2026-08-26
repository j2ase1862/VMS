using OpenCvSharp;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatch 각도 ±180 경계 회귀 테스트.
    /// 정련 단계가 코스 최적각 ±코스스텝을 탐색하므로 360° 검색(Start -180, Extent 360)
    /// 경계에서 ±180 밖 각도(예: 183°)가 보고될 수 있었음 — 정규화 없이는 각도 판정
    /// (허용 -180~180)이 경계 근처 정상품을 오탐 NG 처리 (2026-08-26 검토에서 발견).
    /// </summary>
    public class FeatureMatchAngleWrapTests
    {
        [Theory]
        [InlineData(183.0, -177.0)]
        [InlineData(-183.0, 177.0)]
        [InlineData(180.0, -180.0)]   // 경계값은 -180 으로 접힘 ([-180, 180) 반개구간)
        [InlineData(-180.0, -180.0)]
        [InlineData(0.0, 0.0)]
        [InlineData(359.0, -1.0)]
        [InlineData(540.0, -180.0)]
        [InlineData(-359.5, 0.5)]
        public void NormalizeAngle_MapsIntoHalfOpenRange(double input, double expected)
        {
            Assert.Equal(expected, FeatureMatchTool.NormalizeAngle(input), 6);
        }

        [Fact]
        public void PoseJudgment_FullRangeLimits_PassesWrappedBoundaryAngle()
        {
            // 360° 운용 시나리오: 허용 -180~180 + 경계 밖 원시 각도 → 정규화 후 판정하면 통과
            var tool = new FeatureMatchTool
            {
                UseAngleJudgment = true,
                AngleLowerLimit = -180,
                AngleUpperLimit = 180
            };

            var raw = 183.2;   // 정련이 만들 수 있는 경계 밖 값
            Assert.NotNull(tool.EvaluatePoseJudgment(raw, 1.0));                             // 미정규화 → 오탐 NG (종전 결함)
            Assert.Null(tool.EvaluatePoseJudgment(FeatureMatchTool.NormalizeAngle(raw), 1.0)); // Execute 경로(정규화 후) → 통과
        }

        [Fact]
        public void Execute_ReportedAngle_IsAlwaysWithinRange()
        {
            // 합성 L자 패턴(회전 방향이 구분되는 형태)을 360° 전체 검색으로 매칭 —
            // 성공 시 보고 각도는 항상 [-180, 180) 안이어야 함
            var tool = new FeatureMatchTool
            {
                AngleStart = -180,
                AngleExtent = 360,
                ScoreThreshold = 0.3
            };

            using var pattern = new Mat(80, 80, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(pattern, new Rect(20, 20, 40, 12), Scalar.White, -1);
            Cv2.Rectangle(pattern, new Rect(20, 20, 12, 40), Scalar.White, -1);
            Assert.True(tool.TrainPattern(pattern), "합성 패턴 학습이 실패하면 테스트 전제가 깨짐");

            using var search = new Mat(240, 240, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(search, new Rect(100, 100, 40, 12), Scalar.White, -1);
            Cv2.Rectangle(search, new Rect(100, 100, 12, 40), Scalar.White, -1);

            var result = tool.Execute(search);
            if (result.Success)
            {
                var angle = (double)result.Data["Angle"];
                Assert.InRange(angle, -180.0, 180.0 - 1e-9);
            }

            result.ReleaseMats();
            result.OverlayImage?.Dispose();
        }
    }
}
