using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.Identification
{
    /// <summary>
    /// OCV (Optical Character Verification) — 학습된 폰트 라이브러리와 NCC 매칭으로
    /// 각 문자가 학습 폰트와 일치하는지 검증. Cognex OcrMax의 Verify 모드 대응.
    /// 입력 이미지 → 이진화 → Connected Components 분할 → 문자별 라이브러리 매칭 → 개별 점수.
    /// </summary>
    public class OCVTool : VisionToolBase
    {
        public FontLibrary FontLibrary { get; private set; } = new();

        // ── 분할 파라미터 ──

        private int _minCharHeight = 12;
        public int MinCharHeight
        {
            get => _minCharHeight;
            set => SetProperty(ref _minCharHeight, Math.Max(1, value));
        }

        private int _maxCharHeight = 200;
        public int MaxCharHeight
        {
            get => _maxCharHeight;
            set => SetProperty(ref _maxCharHeight, Math.Max(1, value));
        }

        private int _minCharWidth = 4;
        public int MinCharWidth
        {
            get => _minCharWidth;
            set => SetProperty(ref _minCharWidth, Math.Max(1, value));
        }

        private bool _invertImage;
        /// <summary>입력이 어두운 배경 + 밝은 문자인 경우 true. 기본은 밝은 배경 + 어두운 문자.</summary>
        public bool InvertImage
        {
            get => _invertImage;
            set => SetProperty(ref _invertImage, value);
        }

        // ── 매칭 / 검증 ──

        private double _matchThreshold = 0.65;
        /// <summary>NCC 임계값 (0~1). 이 미만 점수는 BadChar로 판정.</summary>
        public double MatchThreshold
        {
            get => _matchThreshold;
            set => SetProperty(ref _matchThreshold, Math.Clamp(value, 0, 1));
        }

        private string _expectedText = string.Empty;
        /// <summary>비어있지 않으면 위치별 char를 비교하여 추가 검증 (길이 불일치 시 자동 FAIL).</summary>
        public string ExpectedText
        {
            get => _expectedText;
            set => SetProperty(ref _expectedText, value);
        }

        // ── 표시 ──

        private bool _drawOverlay = true;
        public bool DrawOverlay
        {
            get => _drawOverlay;
            set => SetProperty(ref _drawOverlay, value);
        }

        public OCVTool()
        {
            Name = "OCV";
            ToolType = "OCVTool";
        }

        /// <summary>
        /// 학습: srcGray ROI를 분할해 knownString의 각 문자와 1:1로 매칭하여 라이브러리에 추가.
        /// 분할 개수가 knownString 길이와 다르면 false 반환.
        /// </summary>
        public bool TrainFromImage(Mat src, string knownString, out string error)
        {
            error = string.Empty;
            if (src == null || src.Empty()) { error = "이미지가 비었습니다."; return false; }
            if (string.IsNullOrEmpty(knownString)) { error = "Known String이 비어있습니다."; return false; }

            using var bin = Binarize(src);
            var segments = SegmentChars(bin);
            if (segments.Count != knownString.Length)
            {
                error = $"분할 개수 {segments.Count} ≠ Known String 길이 {knownString.Length}. " +
                        "ROI를 조정하거나 분할 파라미터를 변경하세요.";
                return false;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                using var patch = new Mat(bin, segments[i]);
                using var normalized = FontLibrary.Normalize(patch);
                Cv2.ImEncode(".png", normalized, out byte[] png);
                FontLibrary.Add(knownString[i].ToString(), png);
            }
            return true;
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat workImage = GetROIImage(inputImage);
                ApplyShapeMaskInPlace(workImage, inputImage, Scalar.White);

                try
                {
                    using var bin = Binarize(workImage);
                    var segments = SegmentChars(bin);

                    var readChars = new List<string>(segments.Count);
                    var perCharScores = new List<double>(segments.Count);
                    int good = 0, bad = 0;
                    bool expectedSet = !string.IsNullOrEmpty(ExpectedText);
                    bool lengthMatch = !expectedSet || segments.Count == ExpectedText.Length;

                    for (int i = 0; i < segments.Count; i++)
                    {
                        using var patch = new Mat(bin, segments[i]);
                        var (ch, score) = FontLibrary.MatchBest(patch);
                        readChars.Add(ch ?? "?");
                        perCharScores.Add(score);

                        bool charPass = score >= MatchThreshold && ch != null;
                        if (charPass && expectedSet && lengthMatch)
                            charPass = ch == ExpectedText[i].ToString();

                        if (charPass) good++; else bad++;
                    }

                    string readText = string.Concat(readChars);
                    double meanScore = perCharScores.Count > 0 ? perCharScores.Average() : 0;

                    result.Data["ReadString"] = readText;
                    result.Data["CharCount"] = segments.Count;
                    result.Data["GoodCharCount"] = good;
                    result.Data["BadCharCount"] = bad;
                    result.Data["MeanScore"] = meanScore;
                    result.Data["MinScore"] = perCharScores.Count > 0 ? perCharScores.Min() : 0;
                    result.Data["AllCharsPass"] = bad == 0 && segments.Count > 0;
                    if (expectedSet) result.Data["LengthMatch"] = lengthMatch;

                    bool success = FontLibrary.Count > 0
                        && segments.Count > 0
                        && bad == 0
                        && (!expectedSet || lengthMatch);

                    result.Success = success;
                    if (FontLibrary.Count == 0)
                        result.Message = "폰트 라이브러리가 비어있습니다. Train으로 문자를 학습하세요.";
                    else if (segments.Count == 0)
                        result.Message = "문자를 검출하지 못했습니다. 분할 파라미터를 조정하세요.";
                    else
                        result.Message = success
                            ? $"OCV PASS: \"{readText}\" (평균 {meanScore:F2})"
                            : $"OCV FAIL: \"{readText}\" — 불일치 {bad}/{segments.Count}";

                    if (DrawOverlay)
                    {
                        Mat overlay = GetColorOverlayBase(inputImage);
                        DrawOverlayGraphics(overlay, inputImage, segments, readChars, perCharScores, expectedSet, lengthMatch);
                        result.OverlayImage = overlay;
                    }
                    result.OutputImage = inputImage.Clone();
                }
                finally
                {
                    if (workImage != inputImage) workImage.Dispose();
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"OCV 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        // ── 이진화 ── (Otsu, InvertImage 옵션 적용 후 항상 black-bg/white-fg 통일)
        private Mat Binarize(Mat src)
        {
            using Mat gray = src.Channels() > 1 ? src.CvtColor(ColorConversionCodes.BGR2GRAY) : src.Clone();
            var bin = new Mat();
            Cv2.Threshold(gray, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            // 문자가 흰색이 되도록 통일 (Cv2.ConnectedComponents는 nonzero를 객체로 본다)
            double mean = bin.Mean().Val0;
            bool charsAreDark = mean > 127;
            if (charsAreDark ^ InvertImage)
                Cv2.BitwiseNot(bin, bin);
            return bin;
        }

        // ── 분할 ── (Connected Components → 크기 필터 → 좌→우 정렬)
        private List<Rect> SegmentChars(Mat bin)
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int n = Cv2.ConnectedComponentsWithStats(bin, labels, stats, centroids,
                PixelConnectivity.Connectivity8);

            var rects = new List<Rect>(n);
            for (int i = 1; i < n; i++)
            {
                int x = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                int y = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                int w = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                int h = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                if (h < MinCharHeight || h > MaxCharHeight || w < MinCharWidth) continue;
                rects.Add(new Rect(x, y, w, h));
            }

            // 한 줄 텍스트 가정 — X 오름차순 정렬. 여러 줄은 (행 Y 클러스터링) 추가 가능.
            rects.Sort((a, b) => a.X.CompareTo(b.X));
            return rects;
        }

        private void DrawOverlayGraphics(Mat overlay, Mat inputImage, List<Rect> segments,
            List<string> chars, List<double> scores, bool expectedSet, bool lengthMatch)
        {
            var adjustedROI = GetAdjustedROI(inputImage);
            int ox = UseROI ? adjustedROI.X : 0;
            int oy = UseROI ? adjustedROI.Y : 0;

            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                bool charPass = scores[i] >= MatchThreshold;
                if (expectedSet && lengthMatch && chars[i] != ExpectedText[i].ToString())
                    charPass = false;
                var color = charPass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

                Cv2.Rectangle(overlay,
                    new Point(s.X + ox, s.Y + oy),
                    new Point(s.X + s.Width + ox, s.Y + s.Height + oy),
                    color, 2);
                Cv2.PutText(overlay, $"{chars[i]}",
                    new Point(s.X + ox, s.Y + oy - 4),
                    HersheyFonts.HersheySimplex, 0.5, color, 1);
                Cv2.PutText(overlay, $"{scores[i]:F2}",
                    new Point(s.X + ox, s.Y + s.Height + oy + 12),
                    HersheyFonts.HersheySimplex, 0.4, color, 1);
            }
        }

        public override List<string> GetAvailableResultKeys() => new()
        {
            "Success", "ReadString", "CharCount", "GoodCharCount", "BadCharCount",
            "MeanScore", "MinScore", "AllCharsPass", "LengthMatch"
        };

        public override VisionToolBase Clone()
        {
            var clone = new OCVTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                MinCharHeight = this.MinCharHeight,
                MaxCharHeight = this.MaxCharHeight,
                MinCharWidth = this.MinCharWidth,
                InvertImage = this.InvertImage,
                MatchThreshold = this.MatchThreshold,
                ExpectedText = this.ExpectedText,
                DrawOverlay = this.DrawOverlay
            };
            // 폰트 라이브러리 깊은 복사 (PNG bytes는 immutable로 취급)
            foreach (var t in FontLibrary.Templates)
                if (t.TemplatePng != null)
                    clone.FontLibrary.Add(t.Char, (byte[])t.TemplatePng.Clone());
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
