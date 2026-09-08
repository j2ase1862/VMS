using System;
using System.IO;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// train_dfine.py 가 실제로 export 한 best.onnx 를 DFineOnnxEngine 에 올리는 스모크 테스트.
    /// 가중치는 repo 에 없으므로 환경 변수로 경로를 주면 실행되고, 없으면 조용히 통과한다.
    ///   VMS_DFINE_ONNX  = best.onnx 경로 (필수)
    ///   VMS_DFINE_IMAGE = 검출할 이미지 경로 (선택 — 없으면 회색 이미지)
    ///   VMS_DFINE_EXPECT_BOX = "x1,y1,x2,y2" 기대 박스 (선택 — 주면 최고 점수 박스와 IoU ≥ 0.5 검증)
    /// CI 에서는 스킵되고, GPU PC 에서 학습 직후 수동 검증 용도.
    /// </summary>
    public class DFineRealModelSmokeTests
    {
        [Fact]
        public void Exported_dfine_model_loads_and_detects_in_original_coordinates()
        {
            var modelPath = Environment.GetEnvironmentVariable("VMS_DFINE_ONNX");
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                return; // 실모델 없음 — 스킵

            Assert.Equal(DetectionModelFormat.DFine, DetectionModelFormatProbe.Probe(modelPath));

            using var engine = new DFineOnnxEngine(modelPath);
            Assert.True(engine.IsDeployLayout);
            Assert.True(engine.HasSizesInput);
            Assert.NotEmpty(engine.GetClassNames());

            var imagePath = Environment.GetEnvironmentVariable("VMS_DFINE_IMAGE");
            using var img = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath)
                ? Cv2.ImRead(imagePath, ImreadModes.Color)
                : new Mat(480, 640, MatType.CV_8UC3, Scalar.All(128));
            Assert.False(img.Empty());

            var dets = engine.Detect(img, 640, 0.05f, 0.45f);

            foreach (var d in dets)
            {
                Assert.InRange(d.X, 0, img.Width);
                Assert.InRange(d.Y, 0, img.Height);
                Assert.InRange(d.X + d.Width, 0, img.Width);
                Assert.InRange(d.Y + d.Height, 0, img.Height);
                Assert.InRange(d.Confidence, 0f, 1f);
                Assert.InRange(d.ClassId, 0, engine.GetClassNames().Length - 1);
            }

            var expect = Environment.GetEnvironmentVariable("VMS_DFINE_EXPECT_BOX");
            if (!string.IsNullOrEmpty(expect))
            {
                var p = Array.ConvertAll(expect.Split(','), s => int.Parse(s.Trim()));
                Assert.NotEmpty(dets);
                var top = dets[0]; // GlobalNMS 가 점수 내림차순으로 반환
                float iou = Iou(top.X, top.Y, top.X + top.Width, top.Y + top.Height, p[0], p[1], p[2], p[3]);
                Assert.True(iou >= 0.5f, $"top box ({top.X},{top.Y},{top.X + top.Width},{top.Y + top.Height}) conf={top.Confidence:F2} IoU={iou:F2} vs expect ({expect})");
            }
        }

        private static float Iou(int ax1, int ay1, int ax2, int ay2, int bx1, int by1, int bx2, int by2)
        {
            int iw = Math.Max(0, Math.Min(ax2, bx2) - Math.Max(ax1, bx1));
            int ih = Math.Max(0, Math.Min(ay2, by2) - Math.Max(ay1, by1));
            float inter = iw * ih;
            float union = (ax2 - ax1) * (ay2 - ay1) + (bx2 - bx1) * (by2 - by1) - inter;
            return union <= 0 ? 0 : inter / union;
        }
    }
}
