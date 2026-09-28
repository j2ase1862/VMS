using System;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// FeatureMatch 배율 탐색 회귀 (2026-09-28 GS 시험자료 작성 중 발견).
    /// 투표를 배율 범위 중심(1.0) 하나로만 해서, 실물 배율이 다르면 표가 에지가 몰린 쪽으로 밀려
    /// 피크가 중심에서 벗어났다(455×349 템플릿, 1.08 배에서 12px) — 정련 반경(8px) 밖이라
    /// 1.04 배는 엉뚱한 자리(점수 0.61), 1.08 배는 미검출. 템플릿 크기로 정한 투표 배율 격자 +
    /// 정련 창 이동(언덕 오르기)으로 수정.
    /// </summary>
    public class FeatureMatchScaleTests
    {
        private const int Background = 200;
        private static readonly Point2f PartCenter = new(400, 400);

        /// <summary>밝은 배경 위 어두운 T 자 부품(비대칭 — 에지가 위쪽 막대에 몰림), 약 320×230.</summary>
        private static Mat Scene()
        {
            var img = new Mat(800, 800, MatType.CV_8UC1, Scalar.All(Background));
            Cv2.Rectangle(img, new Rect(240, 290, 320, 70), Scalar.All(40), -1);   // 가로 막대
            Cv2.Rectangle(img, new Rect(360, 360, 80, 160), Scalar.All(40), -1);   // 기둥
            Cv2.Circle(img, new Point(300, 385), 22, Scalar.All(60), -1);           // 귀
            Cv2.Circle(img, new Point(500, 385), 22, Scalar.All(60), -1);
            Cv2.Rectangle(img, new Rect(225, 300, 15, 50), Scalar.All(120), -1);   // 양끝 캡
            Cv2.Rectangle(img, new Rect(560, 300, 15, 50), Scalar.All(120), -1);
            Cv2.GaussianBlur(img, img, new Size(3, 3), 0);
            return img;
        }

        private static readonly Rect TrainRoi = new(205, 270, 390, 270);

        private static FeatureMatchTool Trained(Mat scene)
        {
            var tool = new FeatureMatchTool
            {
                AngleStart = -180, AngleExtent = 360, MinScale = 0.9, MaxScale = 1.1, ScaleStep = 0.01,
                ScoreThreshold = 0.5, ROI = TrainRoi, UseROI = true,
            };
            using var crop = new Mat(scene, TrainRoi);
            Assert.True(tool.TrainPattern(crop));
            return tool;
        }

        [Theory]
        [InlineData(0.90)]
        [InlineData(0.94)]
        [InlineData(1.04)]
        [InlineData(1.06)]
        [InlineData(1.08)]
        [InlineData(1.10)]
        public void Execute_ScaledPart_FoundWithExactScale(double scale)
        {
            using var scene = Scene();
            var tool = Trained(scene);
            var model = tool.Models[0];

            // 학습 중심 기준으로 배율만 바꾼 장면 — 중심은 제자리
            var c = new Point2f((float)model.TrainedCenterX, (float)model.TrainedCenterY);
            using var m = Cv2.GetRotationMatrix2D(c, 0, scale);
            using var search = new Mat();
            Cv2.WarpAffine(scene, search, m, scene.Size(), InterpolationFlags.Cubic, BorderTypes.Replicate);

            var r = tool.Execute(search);

            Assert.True(r.Success, r.Message);
            Assert.True((double)r.Data["Score"] > 0.9, $"점수 {(double)r.Data["Score"]:F3}");
            Assert.Equal(scale, (double)r.Data["Scale"], 0.01);
            Assert.Equal(c.X, (double)r.Data["CenterX"], 2.0);
            Assert.Equal(c.Y, (double)r.Data["CenterY"], 2.0);
            r.ReleaseMats();
        }

        [Fact]
        public void VoteScaleGrid_CoversRangeWithBoundedShift()
        {
            // 반경 72 코스 px(455×349 템플릿, 레벨 3), 밀림 한도 2px → 간격 ≤ 0.0556 → 0.9~1.1 을 5개로
            var grid = FeatureMatchTool.VoteScaleGrid(0.9, 1.1, 72, 2);
            Assert.Equal(5, grid.Count);
            Assert.Equal(0.9, grid[0], 9);
            Assert.Equal(1.1, grid[^1], 9);
            for (int i = 1; i < grid.Count; i++)
                Assert.True((grid[i] - grid[i - 1]) / 2 * 72 <= 2 + 1e-9);
        }

        [Theory]
        [InlineData(1.0, 1.0, 1)]    // 범위 없음 → 중심 1개
        [InlineData(0.98, 1.02, 2)]  // 좁은 범위도 양끝을 덮는다
        [InlineData(0.5, 2.0, 9)]    // 넓은 범위는 상한 9개
        public void VoteScaleGrid_Count(double min, double max, int expected)
        {
            Assert.Equal(expected, FeatureMatchTool.VoteScaleGrid(min, max, 72, 2).Count);
        }
    }
}
