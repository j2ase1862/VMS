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
    /// PP-OCR 엔진 — 공백 문자 · 여러 줄 읽는 순서.
    ///
    /// ① 인식 모델 출력의 마지막 번호(공백)를 버려 "LOT 261013-A01" 이 "LOT261013-A01" 로 나왔다.
    /// ② 글자 영역을 윤곽선 검출 순서 그대로 읽어 두 줄이 아래 줄부터 나왔다.
    /// (2026-10-14 GS 딥러닝 시험자료 OCR 촬영본에서 발견)
    /// 정렬 테스트는 모델 없이 돌고, 인식 테스트는 동봉 모델(models\ppocr)이 있을 때만 돈다 — CI 는 OCR 데이터를 받지 않는다.
    /// </summary>
    public class PaddleOcrSpaceAndOrderTests
    {
        private readonly ITestOutputHelper _out;
        public PaddleOcrSpaceAndOrderTests(ITestOutputHelper o) => _out = o;

        private static Point2f[] Box(float x, float y, float w, float h) =>
            new[] { new Point2f(x, y), new Point2f(x + w, y), new Point2f(x + w, y + h), new Point2f(x, y + h) };

        [Fact]
        public void SortReadingOrder_TopLineFirst()
        {
            var top = Box(100, 100, 400, 50);
            var bottom = Box(100, 200, 400, 50);
            var sorted = PaddleOcrOnnxEngine.SortReadingOrder(new List<Point2f[]> { bottom, top });
            Assert.Same(top, sorted[0]);
            Assert.Same(bottom, sorted[1]);
        }

        [Fact]
        public void SortReadingOrder_SameLine_LeftToRight_EvenIfSlightlyHigher()
        {
            // 같은 줄의 오른쪽 단어가 몇 px 위에 있어도(기울어짐) 왼쪽 단어가 먼저
            var left = Box(100, 110, 200, 50);
            var right = Box(350, 100, 200, 50);
            var sorted = PaddleOcrOnnxEngine.SortReadingOrder(new List<Point2f[]> { right, left });
            Assert.Same(left, sorted[0]);
            Assert.Same(right, sorted[1]);
        }

        [Fact]
        public void SortReadingOrder_TwoLinesTwoWords()
        {
            var a = Box(100, 100, 150, 40); var b = Box(300, 104, 150, 40);
            var c = Box(100, 200, 150, 40); var d = Box(300, 196, 150, 40);
            var sorted = PaddleOcrOnnxEngine.SortReadingOrder(new List<Point2f[]> { d, c, b, a });
            Assert.Equal(new[] { a, b, c, d }, sorted);
        }

        private bool ModelsPresent()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "ppocr");
            if (File.Exists(Path.Combine(dir, "ch_PP-OCRv4_rec_infer.onnx"))) return true;
            _out.WriteLine("SKIP — PP-OCR 모델 없음(CI)");
            return false;
        }

        // 한 영역으로 검출되는 크기 — 단어 간격이 넓으면 "LOT" · "260928" 이 따로 검출되고 OCRTool 이 공백으로 잇기 때문에
        // 엔진의 공백 처리를 검증하지 못한다 (Hershey Simplex 2.6 배는 두 영역, Duplex 1.2 배는 한 영역 — 2026-10-14 실측).
        private static Mat OneLine(string text, int width = 500, int height = 160, int y = 100)
        {
            var img = new Mat(height, width, MatType.CV_8UC3, Scalar.All(235));
            Cv2.PutText(img, text, new Point(20, y), HersheyFonts.HersheyDuplex, 1.2, Scalar.All(20), 2);
            return img;
        }

        private static (string text, string words) Read(Mat img, string whitelist = "")
        {
            var tool = new OCRTool { OcrEngine = OcrEngineType.PPOcrOnnx, CharacterWhitelist = whitelist };
            var r = tool.Execute(img);
            return (r.Data.TryGetValue("RecognizedText", out var v) ? Convert.ToString(v) ?? "" : "",
                    r.Data.TryGetValue("Words", out var w) ? Convert.ToString(w) ?? "" : "");
        }

        [Fact]
        public void Recognize_KeepsSpaceInsideOneRegion()
        {
            if (!ModelsPresent()) return;
            using var img = OneLine("LOT 260928");
            var (text, words) = Read(img);
            _out.WriteLine($"'{text}' words=[{words}]");
            Assert.Equal("LOT 260928", words);      // 한 영역 — 공백은 엔진이 낸 것
            Assert.Equal("LOT 260928", text);
        }

        [Fact]
        public void Recognize_Whitelist_WithoutSpace_DropsSpace_WithSpace_Keeps()
        {
            if (!ModelsPresent()) return;
            using var img = OneLine("LOT 260928");
            Assert.Equal("LOT260928", Read(img, "LOT0123456789").text);
            Assert.Equal("LOT 260928", Read(img, "LOT0123456789 ").text);
        }

        [Fact]
        public void Recognize_TwoLines_TopFirst()
        {
            if (!ModelsPresent()) return;
            using var img = new Mat(260, 500, MatType.CV_8UC3, Scalar.All(235));
            Cv2.PutText(img, "QTY 120", new Point(20, 90), HersheyFonts.HersheyDuplex, 1.2, Scalar.All(20), 2);
            Cv2.PutText(img, "NET 235", new Point(20, 200), HersheyFonts.HersheyDuplex, 1.2, Scalar.All(20), 2);
            Assert.Equal("QTY 120 NET 235", Read(img).text);
        }
    }
}
