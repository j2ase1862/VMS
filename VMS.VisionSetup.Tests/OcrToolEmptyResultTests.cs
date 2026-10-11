using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.Identification;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// OCR 결과 오버레이 · 동봉 OCR 데이터.
    /// ① 인식 결과가 빈 문자열이면 오버레이의 Cv2.PutText 가 ArgumentNullException 을 던져
    ///   판정 메시지 대신 "OCR 실패: Value cannot be null." 로 보고되던 결함의 회귀 시험 (엔진 불필요 — CI 에서도 돈다).
    /// ② 빌드가 동봉한 tessdata 로 기본 경로에서 인식되는지 (CI 는 OCR 데이터를 받지 않으므로 건너뜀).
    /// </summary>
    public class OcrToolEmptyResultTests
    {
        private readonly ITestOutputHelper _out;
        public OcrToolEmptyResultTests(ITestOutputHelper o) => _out = o;

        [Theory]
        [InlineData("")]
        [InlineData("   \n ")]
        public void Overlay_EmptyRecognition_DoesNotThrow(string text)
        {
            using var input = new Mat(200, 600, MatType.CV_8UC3, Scalar.All(230));
            using var overlay = input.Clone();
            var tool = new OCRTool();
            var empty = new OCRTool.OcrResultData(text, 0f, new List<OCRTool.OcrWord>());

            var ex = Record.Exception(() => tool.DrawOCROverlay(overlay, input, empty, success: false,
                useAffineROI: false, centerX: 0, centerY: 0, roiW: 0, roiH: 0, angle: 0));

            Assert.Null(ex);
        }

        [Fact]
        public void PrintedText_IsRecognized_FromBundledTessdata()
        {
            // 기본 경로(실행 폴더 tessdata\) — TessdataPath 를 비워 설치본과 같은 조건으로 읽는다
            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata", "eng.traineddata")))
            {
                _out.WriteLine("SKIP — tessdata 없음(CI)");
                return;
            }

            using var img = new Mat(160, 900, MatType.CV_8UC3, Scalar.All(235));
            Cv2.PutText(img, "LOT 260928", new Point(40, 110), HersheyFonts.HersheySimplex, 2.6, Scalar.All(20), 6);
            var tool = new OCRTool { DrawOverlay = true };

            var r = tool.Execute(img);

            Assert.True(r.Success, r.Message);
            Assert.Equal("LOT 260928", r.Data["RecognizedText"]);
            Assert.NotNull(r.OverlayImage);
        }

        [Fact]
        public void PaddleOcr_BundledModels_RecognizeWithoutDownload()
        {
            // 동봉 경로(실행 폴더 models\ppocr\)에 세 파일이 다 있으면 엔진은 다운로드를 시도하지 않는다
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "ppocr");
            if (!File.Exists(Path.Combine(dir, "ch_PP-OCRv4_det_infer.onnx")))
            {
                _out.WriteLine("SKIP — PP-OCR 모델 없음(CI)");
                return;
            }

            using var img = new Mat(160, 900, MatType.CV_8UC3, Scalar.All(235));
            Cv2.PutText(img, "LOT 260928", new Point(40, 110), HersheyFonts.HersheySimplex, 2.6, Scalar.All(20), 6);
            var tool = new OCRTool { OcrEngine = OcrEngineType.PPOcrOnnx, DrawOverlay = true };

            var r = tool.Execute(img);

            _out.WriteLine(r.Message);
            Assert.True(r.Success, r.Message);
            Assert.Contains("260928", (string)r.Data["RecognizedText"]);
        }
    }
}
