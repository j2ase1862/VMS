using OpenCvSharp;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// BlobTool 면적 판정 — 검출 0개 처리.
    ///
    /// 과거 `if (UseAreaJudgment && blobs.Count > 0)` 가드 때문에 blob 이 하나도 없으면
    /// 면적 판정이 통째로 스킵되어 "미검출 = PASS" 가 됐다 (2026-08-18 현장, Count/TotalArea
    /// 0 인데 Pass=true). 0개도 TotalArea=0 으로 기준 범위와 비교해야 한다.
    /// </summary>
    public class BlobToolAreaJudgmentZeroTests
    {
        private static Mat MakeEmptyImage() =>
            new(160, 640, MatType.CV_8UC1, Scalar.Black);

        private static Mat MakeSingleBlobImage()
        {
            // 흰 원 r=15 → 면적 ~707 (기본 MinArea=100 통과)
            var img = new Mat(160, 640, MatType.CV_8UC1, Scalar.Black);
            Cv2.Circle(img, new Point(80, 80), 15, Scalar.White, -1);
            return img;
        }

        [Fact]
        public void AreaJudgment_ZeroBlobs_OutOfRange_Fails()
        {
            // 기대 700±300 인데 검출 0개(TotalArea=0) → NG 여야 한다.
            using var img = MakeEmptyImage();
            var tool = new BlobTool
            {
                UseAreaJudgment = true,
                ExpectedArea = 700,
                AreaTolerancePlus = 300,
                AreaToleranceMinus = 300
            };

            var result = tool.Execute(img);

            Assert.Equal(0, result.Data["BlobCount"]);
            Assert.Equal(0.0, (double)result.Data["TotalArea"]);
            Assert.False(result.Success);
            Assert.False((bool)result.Data["AreaJudgment"]);
        }

        [Fact]
        public void AreaJudgment_ZeroBlobs_RangeIncludesZero_Passes()
        {
            // 기준 범위가 0 을 포함(기대 0 +500/−0)하면 0개 검출도 합법적 OK.
            using var img = MakeEmptyImage();
            var tool = new BlobTool
            {
                UseAreaJudgment = true,
                ExpectedArea = 0,
                AreaTolerancePlus = 500,
                AreaToleranceMinus = 0
            };

            var result = tool.Execute(img);

            Assert.True(result.Success);
            Assert.True((bool)result.Data["AreaJudgment"]);
        }

        [Fact]
        public void AreaJudgment_BlobInRange_StillPasses()
        {
            // 정상 케이스 회귀 — 가드 제거가 합격 경로를 건드리지 않아야 한다.
            using var img = MakeSingleBlobImage();
            var tool = new BlobTool
            {
                UseAreaJudgment = true,
                ExpectedArea = 700,
                AreaTolerancePlus = 300,
                AreaToleranceMinus = 300
            };

            var result = tool.Execute(img);

            Assert.True(result.Success);
            Assert.True((bool)result.Data["AreaJudgment"]);
        }

        [Fact]
        public void NoJudgment_ZeroBlobs_StillFails()
        {
            // 판정 미사용 시 기존 규칙 유지 — 검출 0개 = 실패 (Success = Count > 0).
            using var img = MakeEmptyImage();
            var tool = new BlobTool();

            var result = tool.Execute(img);

            Assert.False(result.Success);
        }
    }
}
