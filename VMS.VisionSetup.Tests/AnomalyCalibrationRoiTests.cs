using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Anomaly 자동 임계값 캘리브레이션은 Execute 와 같은 영역(ROI)을 채점해야 한다.
    ///
    /// 종전에는 UseROI 와 무관하게 원본 전체로 채점해, ROI 를 쓰는 레시피에서는 임계값이 검사 점수와
    /// 다른 눈금으로 잡혔다 (2026-10-16 GS 딥러닝 시험자료 — 칸 크롭 이미지로 피해 갔던 결함 ②).
    /// 점수 스텁은 입력 평균 밝기(ImageNet 정규화)를 점수로 내므로, ROI 안과 밖의 밝기를 다르게 두면
    /// 어느 영역을 채점했는지 점수로 드러난다.
    /// </summary>
    public class AnomalyCalibrationRoiTests : IDisposable
    {
        private const int Size = 16;
        private static readonly Rect LeftHalf = new(0, 0, 100, 100);

        private readonly string _dir;
        private readonly string _model;
        private readonly List<string> _images = new();

        public AnomalyCalibrationRoiTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"vms-anomaly-calib-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
            _model = Path.Combine(_dir, "score.onnx");
            File.WriteAllBytes(_model, Convert.FromBase64String(AnomalyModelContractTests.ScoreStub));

            var calib = Path.Combine(_dir, "good");
            Directory.CreateDirectory(calib);
            // 200×100: 왼쪽 절반(ROI)은 장마다 다른 회색, 오른쪽 절반은 밝은 160 — 전체로 채점하면 점수가 크게 오른다
            foreach (var level in new[] { 120, 125, 130, 135 })
            {
                using var img = new Mat(100, 200, MatType.CV_8UC3, new Scalar(160, 160, 160));
                img[LeftHalf].SetTo(new Scalar(level, level, level));
                var path = Path.Combine(calib, $"good_{level}.png");
                Cv2.ImWrite(path, img);
                _images.Add(path);
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private AnomalyTool NewTool() => new()
        {
            ModelPath = _model,
            InputSize = Size,
            CalibrationFolder = Path.Combine(_dir, "good"),
            CalibrationSigma = 1.0,
            DrawOverlay = false,
        };

        /// <summary>검사(Execute)가 같은 이미지에 매기는 점수로 mean + 1σ (모집단 표준편차)</summary>
        private double ThresholdFromExecute(AnomalyTool tool)
        {
            var scores = _images.Select(p =>
            {
                using var img = Cv2.ImRead(p, ImreadModes.Color);
                var r = tool.Execute(img);
                Assert.True(r.Data.ContainsKey("AnomalyScore"), r.Message);
                return Convert.ToDouble(r.Data["AnomalyScore"]);
            }).ToList();
            double mean = scores.Average();
            double std = Math.Sqrt(scores.Sum(s => (s - mean) * (s - mean)) / scores.Count);
            return mean + std;
        }

        [Fact]
        public async Task WithRoi_ThresholdMatchesExecuteScores()
        {
            var tool = NewTool();
            tool.UseROI = true;
            tool.ROI = LeftHalf;

            Assert.True(await tool.CalibrateThresholdAsync(), tool.CalibrationReport);
            double calibrated = tool.AnomalyThreshold;

            Assert.Equal(ThresholdFromExecute(tool), calibrated, 4);
            Assert.Contains("ROI (0,0 100×100)", tool.CalibrationReport);

            // 원본 전체로 채점한 값과는 분명히 달라야 한다 (종전 동작이면 이쪽이 나왔다)
            tool.UseROI = false;
            Assert.True(ThresholdFromExecute(tool) > calibrated + 0.1);
        }

        [Fact]
        public async Task WithoutRoi_ThresholdUsesWholeImage()
        {
            var tool = NewTool();

            Assert.True(await tool.CalibrateThresholdAsync(), tool.CalibrationReport);

            Assert.Equal(ThresholdFromExecute(tool), tool.AnomalyThreshold, 4);
            Assert.Contains("이미지 전체", tool.CalibrationReport);
        }

        [Fact]
        public async Task FixtureMovedRoi_UsesReferencePoseRoi()
        {
            var tool = NewTool();
            tool.UseROI = true;
            tool.ROI = new Rect(100, 0, 100, 100);   // Fixture 가 마지막 실행에서 옮겨 둔 위치
            tool.FixtureBaseROI = LeftHalf;          // 기준 자세 ROI
            tool.HasFixtureBaseROI = true;

            Assert.True(await tool.CalibrateThresholdAsync(), tool.CalibrationReport);

            var reference = NewTool();
            reference.UseROI = true;
            reference.ROI = LeftHalf;
            Assert.Equal(ThresholdFromExecute(reference), tool.AnomalyThreshold, 4);
        }

        [Fact]
        public async Task RoiOutsideImages_FailsWithExplanation_AndKeepsThreshold()
        {
            var tool = NewTool();
            tool.AnomalyThreshold = 0.42;
            tool.UseROI = true;
            tool.ROI = new Rect(500, 500, 50, 50);

            Assert.False(await tool.CalibrateThresholdAsync());
            Assert.Contains("ROI 가 캘리브레이션 이미지 밖", tool.CalibrationReport);
            Assert.Equal(0.42, tool.AnomalyThreshold);
        }

        [Fact]
        public async Task RoiPartlyOutside_ScoresOverlapAndWarns()
        {
            var tool = NewTool();
            tool.UseROI = true;
            tool.ROI = new Rect(-20, 0, 120, 100);   // 겹치는 부분 = 왼쪽 절반

            Assert.True(await tool.CalibrateThresholdAsync(), tool.CalibrationReport);
            Assert.Equal(ThresholdFromExecute(tool), tool.AnomalyThreshold, 4);
            Assert.Contains("ROI 일부가 이미지 밖 4장", tool.CalibrationReport);
        }
    }
}
