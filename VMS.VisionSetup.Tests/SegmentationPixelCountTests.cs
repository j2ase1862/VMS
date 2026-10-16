using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// SegmentationTool 클래스별 화소 수 · 면적 비율 — 입력(ROI) 이미지 크기 기준.
    ///
    /// 종전에는 화소 수를 모델 출력 해상도(512²)에서 세고 비율은 원본 크기(2448×2048)로 나눠, 비율 합이
    /// 0.05 쯤으로 나왔다 (2026-10-16 GS 시맨틱 분할 시험자료). 또 ROI 를 쓰면 ROI 크기 라벨맵을 화면 전체로
    /// 늘려 오버레이를 엉뚱한 자리에 그렸다.
    /// 스텁은 Identity 그래프 input[1,3,32,32] → logits[1,3,32,32] (생성: onnx.helper, 도구 InputSize 하한이 32) —
    /// 순수 색 영역은 그 색 채널이 argmax 가 된다. 초록은 BGR·RGB 어느 순서든 채널 1 이다.
    /// </summary>
    public class SegmentationPixelCountTests
    {
        private const string IdentityStub = "CAcSDXZtcy10ZXN0LXN0dWI6ZAoZCgVpbnB1dBIGbG9naXRzIghJZGVudGl0eRIEc3R1YlofCgVpbnB1dBIWChQIARIQCgIIAQoCCAMKAgggCgIIIGIgCgZsb2dpdHMSFgoUCAESEAoCCAEKAggDCgIIIAoCCCBCBAoAEA0=";

        private sealed class TempModel : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vms-seg-{Guid.NewGuid():N}.onnx");
            public TempModel() => File.WriteAllBytes(Path, Convert.FromBase64String(IdentityStub));
            public void Dispose() { try { File.Delete(Path); } catch { } }
        }

        // 160×80 — 왼쪽 1/4 파랑 · 가운데 1/2 초록 · 오른쪽 1/4 빨강 (32 칸 격자 = 5 px 단위에 경계가 맞아 축소·확대 후에도 정확)
        private static Mat ThreeBands()
        {
            var img = new Mat(80, 160, MatType.CV_8UC3, new Scalar(0, 255, 0));
            img[new Rect(0, 0, 40, 80)].SetTo(new Scalar(255, 0, 0));
            img[new Rect(120, 0, 40, 80)].SetTo(new Scalar(0, 0, 255));
            return img;
        }

        private static long[] Pixels(VMS.VisionSetup.Models.VisionResult r, int n) =>
            Enumerable.Range(0, n).Select(c => Convert.ToInt64(r.Data[$"Class{c}_Pixels"])).ToArray();

        private static double[] Ratios(VMS.VisionSetup.Models.VisionResult r, int n) =>
            Enumerable.Range(0, n).Select(c => Convert.ToDouble(r.Data[$"Class{c}_AreaRatio"])).ToArray();

        [Fact]
        public void PixelCounts_AreInInputResolution_AndRatiosSumToOne()
        {
            using var m = new TempModel();
            using var img = ThreeBands();
            var tool = new SegmentationTool { ModelPath = m.Path, InputSize = 32, UseImageNetNormalization = false, ShowOverlay = false, OverlayBaseImage = img };

            var r = tool.Execute(img);

            Assert.True(r.Success, r.Message);
            var px = Pixels(r, 3);
            Assert.Equal(160L * 80, px.Sum());
            Assert.Equal(new long[] { 3200, 3200, 6400 }, px.OrderBy(v => v).ToArray());
            Assert.Equal(6400, px[1]);                       // 초록 = 채널 1
            Assert.Equal(1.0, Ratios(r, 3).Sum(), 6);
            Assert.Equal(0.5, Ratios(r, 3)[1], 6);
        }

        [Fact]
        public void WithRoi_CountsAndRatiosUseRoiArea()
        {
            using var m = new TempModel();
            using var img = ThreeBands();
            var tool = new SegmentationTool
            {
                ModelPath = m.Path, InputSize = 32, UseImageNetNormalization = false, ShowOverlay = false, OverlayBaseImage = img,
                ROI = new Rect(40, 0, 80, 80), UseROI = true,   // 초록 영역만
            };

            var r = tool.Execute(img);

            Assert.True(r.Success, r.Message);
            var px = Pixels(r, 3);
            Assert.Equal(80L * 80, px.Sum());
            Assert.Equal(6400, px[1]);
            Assert.Equal(1.0, Ratios(r, 3)[1], 6);
            Assert.Equal(1.0, Convert.ToDouble(r.Data["ForegroundRatio"]), 6);   // 배경 0 → 초록 전부 전경
        }

        [Fact]
        public void WithRoi_OverlayIsDrawnOnlyInsideRoi()
        {
            using var m = new TempModel();
            using var img = ThreeBands();
            var roi = new Rect(40, 0, 80, 80);
            var tool = new SegmentationTool
            {
                ModelPath = m.Path, InputSize = 32, UseImageNetNormalization = false, ShowOverlay = true, OverlayOpacity = 0.5,
                OverlayBaseImage = img, ROI = roi, UseROI = true, BackgroundClass = 0,
            };

            var r = tool.Execute(img);

            Assert.True(r.Success, r.Message);
            using var overlay = r.OverlayImage!;
            Assert.Equal(img.Size(), overlay.Size());
            // ROI 밖은 원본 그대로, ROI 안은 초록 클래스 색이 섞인다
            Assert.Equal(img.At<Vec3b>(40, 10), overlay.At<Vec3b>(40, 10));
            Assert.Equal(img.At<Vec3b>(40, 150), overlay.At<Vec3b>(40, 150));
            Assert.NotEqual(img.At<Vec3b>(40, 80), overlay.At<Vec3b>(40, 80));
        }
    }
}
