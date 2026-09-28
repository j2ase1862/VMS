using System;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// ShapeMatch 회전 탐색 회귀 (2026-09-28 GS 시험자료 작성 중 발견).
    /// - 밝은 배경 위 부품을 비스듬히(33°) 놓으면 못 찾던 문제: 회전 템플릿의 빈 모서리를 검은색으로 채워
    ///   모서리 전체가 불일치가 되던 것 → 템플릿 테두리 중앙값(배경)으로 채움.
    /// - 각도 간격 2° 에서 학습 이미지조차 점수 0.90·배율 1.05 로 나오던 문제: 거친 격자가 -180° 부터 8° 씩이라
    ///   0° 를 건너뛰고, 정밀 탐색이 좌상단 기준이라 배율이 바뀌면 위치 여유(4px)를 벗어남 → 0° 기준 격자 + 중심 기준.
    /// </summary>
    public class ShapeMatchRotationTests
    {
        private const int Background = 200;
        private static readonly Point2f PartCenter = new(300, 300);

        /// <summary>밝은 배경(200) 위 어두운 T 자 부품(가로 막대 + 아래 기둥 + 한쪽 귀).</summary>
        private static Mat Scene()
        {
            var img = new Mat(600, 600, MatType.CV_8UC1, Scalar.All(Background));
            Cv2.Rectangle(img, new Rect(240, 260, 120, 30), Scalar.All(40), -1);   // 가로 막대
            Cv2.Rectangle(img, new Rect(285, 290, 30, 70), Scalar.All(40), -1);    // 기둥
            Cv2.Circle(img, new Point(250, 305), 10, Scalar.All(60), -1);           // 비대칭 귀
            Cv2.GaussianBlur(img, img, new Size(3, 3), 0);
            return img;
        }

        /// <summary>부품 둘레 여백 20px 학습 영역.</summary>
        private static readonly Rect TrainRoi = new(220, 240, 160, 140);

        private static Mat Rotated(Mat src, double angle)
        {
            using var m = Cv2.GetRotationMatrix2D(PartCenter, angle, 1.0);
            var dst = new Mat();
            Cv2.WarpAffine(src, dst, m, src.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(Background));
            return dst;
        }

        private static ShapeMatchTool Trained(Mat scene, double angleStep)
        {
            var tool = new ShapeMatchTool
            {
                AngleStep = angleStep, MinScale = 0.9, MaxScale = 1.1, ScaleStep = 0.05,
                ScoreThreshold = 0.8, ROI = TrainRoi, UseROI = true,
            };
            Assert.True(tool.TrainFromImage(scene));
            return tool;
        }

        [Theory]
        [InlineData(33)]
        [InlineData(-70)]
        [InlineData(125)]
        public void Execute_ObliqueRotationOnBrightBackground_Found(double angle)
        {
            using var scene = Scene();
            var tool = Trained(scene, 5);
            using var search = Rotated(scene, angle);

            var r = tool.Execute(search);

            Assert.True(r.Success, r.Message);
            Assert.True((double)r.Data["Score"] > 0.9, $"점수 {(double)r.Data["Score"]:F3}");
            // 학습 영역 중심(300, 310)이 장면 회전(중심 300, 300)으로 옮겨간 자리
            using var m = Cv2.GetRotationMatrix2D(PartCenter, angle, 1.0);
            double rx = TrainRoi.X + TrainRoi.Width / 2.0, ry = TrainRoi.Y + TrainRoi.Height / 2.0;
            double ex = m.At<double>(0, 0) * rx + m.At<double>(0, 1) * ry + m.At<double>(0, 2);
            double ey = m.At<double>(1, 0) * rx + m.At<double>(1, 1) * ry + m.At<double>(1, 2);
            Assert.Equal(ex, (double)r.Data["CenterX"], 3.0);
            Assert.Equal(ey, (double)r.Data["CenterY"], 3.0);
            // 도구 각도 = 템플릿을 돌린 각도(GetRotationMatrix2D 규약)라 장면 회전과 같은 부호
            double err = Math.Abs(((double)r.Data["Angle"] - angle + 540) % 360 - 180);
            Assert.True(err <= 2.5, $"각도 {(double)r.Data["Angle"]:F1}° (기대 {angle}°)");
            r.ReleaseMats();
        }

        [Fact]
        public void Execute_TwoInstancesAtDifferentAngles_BothFound()
        {
            using var scene = Scene();
            var tool = Trained(scene, 5);
            tool.MaxInstances = 5;

            // 부품 두 개: 왼쪽 -15°, 오른쪽 125° (1200×600 장면)
            using var search = new Mat(600, 1200, MatType.CV_8UC1, Scalar.All(Background));
            using (var a = Rotated(scene, -15)) a.CopyTo(new Mat(search, new Rect(0, 0, 600, 600)));
            using (var b = Rotated(scene, 125)) b.CopyTo(new Mat(search, new Rect(600, 0, 600, 600)));

            var r = tool.Execute(search);

            Assert.True(r.Success, r.Message);
            Assert.Equal(2, (int)r.Data["MatchCount"]);
            for (int i = 0; i < 2; i++)
                Assert.True((double)r.Data[$"Match{i}_Score"] > 0.9, $"#{i} 점수 {(double)r.Data[$"Match{i}_Score"]:F3}");
            r.ReleaseMats();
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(7)]
        public void Execute_TrainingImage_ExactPose(double angleStep)
        {
            using var scene = Scene();
            var tool = Trained(scene, angleStep);

            var r = tool.Execute(scene);

            Assert.True(r.Success, r.Message);
            Assert.True((double)r.Data["Score"] > 0.99, $"점수 {(double)r.Data["Score"]:F3}");
            Assert.Equal(0.0, (double)r.Data["Angle"], 6);
            Assert.Equal(1.0, (double)r.Data["Scale"], 6);
            r.ReleaseMats();
        }

        [Theory]
        [InlineData(2, 181)]   // ±180 이 격자 위
        [InlineData(5, 73)]
        [InlineData(7, 52)]    // ±175 + 별도 180
        public void AngleGrid_AnchoredAtZero(double step, int count)
        {
            var grid = ShapeMatchTool.AngleGrid(step);
            Assert.Contains(0.0, grid);
            Assert.Contains(180.0, grid);
            Assert.Equal(count, grid.Count);
        }

        [Fact]
        public void BorderMedian_IsBackground()
        {
            using var scene = Scene();
            using var crop = new Mat(scene, TrainRoi);
            Assert.Equal(Background, ShapeMatchTool.BorderMedian(crop));
        }
    }
}
