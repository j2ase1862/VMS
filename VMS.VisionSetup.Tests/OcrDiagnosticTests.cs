using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VMS.VisionSetup.VisionTools.Identification;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 라벨 OCR 진단 — LOT '9876543', EXP '31/03/2099' 인식 시도.
    /// 어두운 배경 + 밝은 글자 (반전 필요), PP-OCRv4 ONNX 엔진 사용.
    /// </summary>
    public class OcrDiagnosticTests
    {
        private const string ImageFolder = @"D:\참고 이미지\딥러닝 이미지\라벨 ocr";
        private static readonly string ExpectedLot = "9876543";
        private static readonly string ExpectedExp = "31/03/2099";

        private readonly ITestOutputHelper _out;
        public OcrDiagnosticTests(ITestOutputHelper output) { _out = output; }

        /// <summary>
        /// 전체 이미지에 PP-OCR 적용 — Detection이 자동으로 텍스트 영역 찾음.
        /// MaxSideLen을 키워 작은 글자 인식률 향상.
        /// </summary>
        [Fact]
        public void PaddleOcr_FullImage_FindLotExp()
        {
            if (!Directory.Exists(ImageFolder))
            {
                _out.WriteLine($"SKIP — folder not found: {ImageFolder}");
                return;
            }

            var files = Directory.GetFiles(ImageFolder, "*.jpg")
                .Concat(Directory.GetFiles(ImageFolder, "*.png"))
                .OrderBy(f => f).ToArray();

            int lotHit = 0, expHit = 0;

            foreach (var path in files)
            {
                using var img = Cv2.ImRead(path, ImreadModes.Color);
                if (img.Empty()) continue;

                var tool = new OCRTool
                {
                    OcrEngine = OcrEngineType.PPOcrOnnx,
                    MaxSideLen = 1600,
                    ConfidenceThreshold = 30,
                    AutoPreprocess = false,
                    DrawOverlay = false
                };
                var result = tool.Execute(img);
                string text = result.Data.TryGetValue("RecognizedText", out var t) ? t?.ToString() ?? "" : "";
                bool foundLot = text.Contains(ExpectedLot);
                bool foundExp = text.Contains(ExpectedExp) || text.Contains("31/03/2099") || text.Contains("31 03 2099");
                if (foundLot) lotHit++;
                if (foundExp) expHit++;

                _out.WriteLine($"{Path.GetFileName(path)}: LOT={(foundLot ? "Y" : "N")} EXP={(foundExp ? "Y" : "N")} | success={result.Success}");
                _out.WriteLine($"  msg: {result.Message}");
                _out.WriteLine($"  → \"{text.Replace("\n", " ")}\"");
            }

            _out.WriteLine($"\nLOT hit: {lotHit}/{files.Length}");
            _out.WriteLine($"EXP hit: {expHit}/{files.Length}");
        }

        /// <summary>
        /// 합성 텍스트로 PP-OCR sanity check — 모델이 정상 작동하는지 검증.
        /// </summary>
        [Fact]
        public void PaddleOcr_SyntheticText_Sanity()
        {
            // 흰 배경 + 검은 텍스트 (PP-OCR 학습 분포의 표준)
            using var img = new Mat(200, 600, MatType.CV_8UC3, Scalar.White);
            Cv2.PutText(img, "HELLO 12345", new Point(20, 80),
                HersheyFonts.HersheySimplex, 2.0, Scalar.Black, 4);
            Cv2.PutText(img, "31/03/2099", new Point(20, 160),
                HersheyFonts.HersheySimplex, 2.0, Scalar.Black, 4);

            PaddleOcrOnnxEngine.DebugLogger = msg => _out.WriteLine(msg);
            using var engine = new PaddleOcrOnnxEngine();
            var results = engine.Run(img);
            _out.WriteLine($"Synthetic text detection: {results.Count} regions");
            foreach (var r in results)
                _out.WriteLine($"  [{r.Confidence:F2}] \"{r.Text}\"");
            PaddleOcrOnnxEngine.DebugLogger = null;
        }

        /// <summary>
        /// 숫자/날짜 whitelist 적용 시 '/' ↔ '7' 등 혼동 차단 검증.
        /// </summary>
        [Fact]
        public void PaddleOcr_Whitelist_DateFormat()
        {
            if (!Directory.Exists(ImageFolder)) { _out.WriteLine("SKIP"); return; }

            var files = Directory.GetFiles(ImageFolder, "*.jpg").OrderBy(f => f).ToArray();
            int expHit = 0, lotHit = 0;

            foreach (var path in files)
            {
                using var img = Cv2.ImRead(path, ImreadModes.Color);
                if (img.Empty()) continue;

                var tool = new OCRTool
                {
                    OcrEngine = OcrEngineType.PPOcrOnnx,
                    MaxSideLen = 1600,
                    ConfidenceThreshold = 30,
                    CharacterWhitelist = "0123456789/", // 날짜 + LOT 전용
                    AutoPreprocess = false,
                    DrawOverlay = false
                };
                var result = tool.Execute(img);
                string text = result.Data.TryGetValue("RecognizedText", out var t) ? t?.ToString() ?? "" : "";
                bool foundLot = text.Contains(ExpectedLot);
                bool foundExp = text.Contains(ExpectedExp);
                if (foundLot) lotHit++;
                if (foundExp) expHit++;
                _out.WriteLine($"{Path.GetFileName(path)}: LOT={(foundLot ? "Y" : "N")} EXP={(foundExp ? "Y" : "N")}");
                _out.WriteLine($"  → \"{text.Replace("\n", " ")}\"");
            }
            _out.WriteLine($"\nLOT: {lotHit}/{files.Length}  EXP: {expHit}/{files.Length}");
        }

        /// <summary>
        /// PP-OCR 엔진 직접 호출로 디버그. MaxSideLen 다양화.
        /// </summary>
        [Fact]
        public void PaddleOcr_DirectEngine_Debug()
        {
            if (!Directory.Exists(ImageFolder)) { _out.WriteLine("SKIP"); return; }

            string testFile = Path.Combine(ImageFolder, "11_55_07_382.jpg");
            using var img = Cv2.ImRead(testFile, ImreadModes.Color);
            _out.WriteLine($"Image: {img.Width}x{img.Height}");

            int[] maxSides = { 960, 1280, 1600, 2048 };
            foreach (var ms in maxSides)
            {
                using var engine = new PaddleOcrOnnxEngine();
                engine.MaxSideLen = ms;
                var results = engine.Run(img);
                _out.WriteLine($"MaxSideLen={ms}: {results.Count} regions");
                foreach (var r in results.Take(20))
                    _out.WriteLine($"  [{r.Confidence:F2}] \"{r.Text}\"");
            }

            // 이미지 밝기 분석
            using var gray = img.CvtColor(ColorConversionCodes.BGR2GRAY);
            double mean = gray.Mean().Val0;
            _out.WriteLine($"\nMean brightness: {mean:F1}/255");

            // 자른 라벨 영역 (가운데 흰색 라벨)
            int x = (int)(img.Width * 0.05), y = (int)(img.Height * 0.30);
            int w = (int)(img.Width * 0.75), h = (int)(img.Height * 0.55);
            w = Math.Min(w, img.Width - x); h = Math.Min(h, img.Height - y);
            using var labelOnly = new Mat(img, new Rect(x, y, w, h));
            using var engine2 = new PaddleOcrOnnxEngine();
            engine2.MaxSideLen = 1280;
            var labelResults = engine2.Run(labelOnly);
            _out.WriteLine($"\nLabel-only ROI ({w}x{h}): {labelResults.Count} regions");
            foreach (var r in labelResults.Take(20))
                _out.WriteLine($"  [{r.Confidence:F2}] \"{r.Text}\"");
        }

        /// <summary>
        /// LOT/EXP 영역만 잘라 InvertImage + Tesseract 패턴으로 시도.
        /// PP-OCR이 잘 안 잡는다면 일반 OCR 보조 경로 검증.
        /// (Tessdata 없어도 PP-OCR 사용으로 동일 ROI 테스트)
        /// </summary>
        [Fact]
        public void PaddleOcr_LotExpROI_PerImage()
        {
            if (!Directory.Exists(ImageFolder))
            {
                _out.WriteLine($"SKIP — folder not found: {ImageFolder}");
                return;
            }

            var files = Directory.GetFiles(ImageFolder, "*.jpg")
                .Concat(Directory.GetFiles(ImageFolder, "*.png"))
                .OrderBy(f => f).ToArray();

            int lotHit = 0, expHit = 0;

            foreach (var path in files)
            {
                using var img = Cv2.ImRead(path, ImreadModes.Color);
                if (img.Empty()) continue;

                // LOT/EXP 영역 대략 위치 (1024x1280 이미지 기준, 라벨 가운데 검은 박스)
                // 정확한 위치는 이미지마다 다르나, 라벨이 중앙 부근 → 너그러운 ROI
                int x = (int)(img.Width * 0.05);
                int y = (int)(img.Height * 0.45);
                int w = (int)(img.Width * 0.7);
                int h = (int)(img.Height * 0.25);
                w = Math.Min(w, img.Width - x);
                h = Math.Min(h, img.Height - y);
                using var roi = new Mat(img, new Rect(x, y, w, h));

                var tool = new OCRTool
                {
                    OcrEngine = OcrEngineType.PPOcrOnnx,
                    MaxSideLen = 1600,
                    ConfidenceThreshold = 30,
                    AutoPreprocess = false,
                    DrawOverlay = false
                };
                var result = tool.Execute(roi);
                string text = result.Data.TryGetValue("RecognizedText", out var t) ? t?.ToString() ?? "" : "";
                bool foundLot = text.Contains(ExpectedLot);
                bool foundExp = text.Contains(ExpectedExp) || text.Replace(" ", "").Contains("31/03/2099");
                if (foundLot) lotHit++;
                if (foundExp) expHit++;

                _out.WriteLine($"{Path.GetFileName(path)}: LOT={(foundLot ? "Y" : "N")} EXP={(foundExp ? "Y" : "N")}");
                _out.WriteLine($"  → \"{text.Replace("\n", " ")}\"");
            }

            _out.WriteLine($"\nLOT hit: {lotHit}/{files.Length}");
            _out.WriteLine($"EXP hit: {expHit}/{files.Length}");
        }
    }
}
