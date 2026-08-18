using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// DetectionTool dot 클러스터 판정 — 판정 불능의 fail-closed 처리.
    ///
    /// 과거 각도 검사가 `!CheckAngle || !AngleComputed → AngleOK=true` 라서, 각도 판정을
    /// 켰는데 dot 이 2개 미만이라 각도 계산이 불가하면(미검출 포함) 통과로 삼켰다
    /// (BlobTool 면적 판정과 동일 패턴, 2026-08-18 전수 검토).
    /// </summary>
    public class DetectionToolDotJudgmentTests
    {
        private static Mat MakeEmptyImage() =>
            new(200, 200, MatType.CV_8UC1, Scalar.Black);

        private static DetectionTool MakeToolWithExpectation(bool checkAngle, int expectedCount = 0)
        {
            var tool = new DetectionTool();
            tool.DotExpectations.Add(new DotClusterExpectation
            {
                ClassId = 0,
                ExpectedCount = expectedCount,   // 0 = 개수 검사 생략 (기존 관례 유지)
                CheckAngle = checkAngle,
                ReferenceAngle = 0,
                AngleTolerance = 10
            });
            return tool;
        }

        [Fact]
        public void CheckAngleOn_NoDots_AngleNotComputable_Fails()
        {
            // 각도 판정 활성 + dot 0개(각도 계산 불가) → 판정 불능은 NG 여야 한다.
            using var img = MakeEmptyImage();
            var tool = MakeToolWithExpectation(checkAngle: true);

            var outcome = tool.AnalyzeCluster(img, new Rect(20, 20, 100, 100), classId: 0);

            Assert.Equal(0, outcome.DotCount);
            Assert.False(outcome.AngleComputed);
            Assert.False(outcome.AngleOK);
            Assert.False(outcome.ClusterOK);
        }

        [Fact]
        public void CheckAngleOff_NoDots_AngleSkipped_Passes()
        {
            // 각도 판정을 끈 클래스는 dot 이 없어도 각도 사유로 NG 가 되면 안 된다 (기존 동작).
            using var img = MakeEmptyImage();
            var tool = MakeToolWithExpectation(checkAngle: false);

            var outcome = tool.AnalyzeCluster(img, new Rect(20, 20, 100, 100), classId: 0);

            Assert.True(outcome.AngleOK);
            Assert.True(outcome.ClusterOK);   // ExpectedCount=0 → 개수 검사도 생략
        }

        [Fact]
        public void CountCheckOn_NoDots_Fails()
        {
            // 개수 검사는 원래 0개를 정상 평가한다 — 회귀 고정.
            using var img = MakeEmptyImage();
            var tool = MakeToolWithExpectation(checkAngle: false, expectedCount: 3);

            var outcome = tool.AnalyzeCluster(img, new Rect(20, 20, 100, 100), classId: 0);

            Assert.False(outcome.CountOK);
            Assert.False(outcome.ClusterOK);
        }
    }
}
