using System;
using System.IO;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Anomaly 히트맵은 추론한 영역(ROI)에만 겹쳐야 한다.
    ///
    /// 종전에는 ROI 영역의 이상 맵을 화면 전체 크기로 늘려 덮어, 이상 위치가 엉뚱한 곳에 보였다
    /// (2026-10-21 #559 작업 중 발견). 스텁은 이상 맵 [1,1,16,16] 만 낸다(맵 최댓값 = 점수).
    /// </summary>
    public class AnomalyHeatmapRoiTests : IDisposable
    {
        private readonly string _model;

        public AnomalyHeatmapRoiTests()
        {
            _model = Path.Combine(Path.GetTempPath(), $"vms-anomaly-heatmap-{Guid.NewGuid():N}.onnx");
            File.WriteAllBytes(_model, Convert.FromBase64String(AnomalyModelContractTests.MapStub));
        }

        public void Dispose()
        {
            try { File.Delete(_model); } catch { }
        }

        private static Mat Gray200() => new(200, 200, MatType.CV_8UC3, new Scalar(128, 128, 128));

        private AnomalyTool NewTool(Mat overlayBase) => new()
        {
            ModelPath = _model,
            InputSize = 16,
            AnomalyThreshold = 0.99,
            DrawOverlay = true,
            ShowHeatmap = true,
            HeatmapOpacity = 0.5,
            OverlayBaseImage = overlayBase,   // VisionService 싱글턴을 읽지 않게
        };

        private static Vec3b At(Mat m, int x, int y) => m.At<Vec3b>(y, x);

        [Fact]
        public void WithRoi_HeatmapOnlyInsideRoi()
        {
            using var img = Gray200();
            var tool = NewTool(img);
            tool.UseROI = true;
            tool.ROI = new Rect(100, 50, 100, 150);   // 오른쪽 아래

            var r = tool.Execute(img);
            Assert.True(r.Data.ContainsKey("AnomalyScore"), r.Message);
            using var overlay = r.OverlayImage!;

            // ROI 밖(테두리 4px · 왼쪽 위 글자 영역을 피한 곳)은 원본 그대로
            Assert.Equal(new Vec3b(128, 128, 128), At(overlay, 50, 150));
            // ROI 안은 히트맵이 섞였다
            Assert.NotEqual(new Vec3b(128, 128, 128), At(overlay, 150, 120));
        }

        [Fact]
        public void WithoutRoi_HeatmapCoversWholeImage()
        {
            using var img = Gray200();
            var tool = NewTool(img);

            var r = tool.Execute(img);
            using var overlay = r.OverlayImage!;

            Assert.NotEqual(new Vec3b(128, 128, 128), At(overlay, 50, 150));
            Assert.NotEqual(new Vec3b(128, 128, 128), At(overlay, 150, 120));
        }

        [Fact]
        public void RepeatedExecute_DoesNotFail_AfterMapDisposal()
        {
            // 이상 맵 · ROI 헤더를 매번 해제한다 — 같은 도구를 거듭 실행해도 결과가 같아야 한다
            using var img = Gray200();
            var tool = NewTool(img);
            tool.UseROI = true;
            tool.ROI = new Rect(100, 50, 100, 150);

            double? first = null;
            for (int i = 0; i < 5; i++)
            {
                var r = tool.Execute(img);
                Assert.True(r.Data.ContainsKey("AnomalyScore"), r.Message);
                double s = Convert.ToDouble(r.Data["AnomalyScore"]);
                first ??= s;
                Assert.Equal(first.Value, s, 6);
                r.OverlayImage?.Dispose();
            }
        }
    }
}
