using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace VMS.VisionSetup.VisionTools.CodeReading
{
    /// <summary>
    /// 코드 리더 도구 (QR, 1D Barcode, DataMatrix 등)
    /// </summary>
    public class CodeReaderTool : VisionToolBase
    {
        private readonly ICodeReader _codeReader;

        // ── 검출 설정 ──

        private CodeReaderMode _codeReaderMode = CodeReaderMode.Auto;
        public CodeReaderMode CodeReaderMode
        {
            get => _codeReaderMode;
            set => SetProperty(ref _codeReaderMode, value);
        }

        private int _maxCodeCount = 10;
        public int MaxCodeCount
        {
            get => _maxCodeCount;
            set => SetProperty(ref _maxCodeCount, Math.Max(1, value));
        }

        private bool _tryHarder = true;
        public bool TryHarder
        {
            get => _tryHarder;
            set => SetProperty(ref _tryHarder, value);
        }

        private bool _useLocalization = true;
        /// <summary>
        /// DataMatrix 후보 영역 사전 탐색 활성화 (DataMatrix/Auto 모드 한정).
        /// ZXing 직접 디코딩이 잡음(텍스트 등)에 묻혀 실패하는 경우 OpenCV 휴리스틱으로
        /// DM 후보 bbox를 먼저 찾아 영역별 디코딩 fallback. 깨끗한 이미지는 추가 시간 미미.
        /// </summary>
        public bool UseLocalization
        {
            get => _useLocalization;
            set => SetProperty(ref _useLocalization, value);
        }

        // ── 판정 설정 ──

        private bool _enableVerification = false;
        public bool EnableVerification
        {
            get => _enableVerification;
            set => SetProperty(ref _enableVerification, value);
        }

        private string _expectedText = string.Empty;
        public string ExpectedText
        {
            get => _expectedText;
            set => SetProperty(ref _expectedText, value);
        }

        private bool _useRegexMatch = false;
        public bool UseRegexMatch
        {
            get => _useRegexMatch;
            set => SetProperty(ref _useRegexMatch, value);
        }

        // ── GS1 / 품질 등급 ──

        private bool _parseGs1;
        /// <summary>
        /// 디코딩된 문자열이 GS1 데이터(FNC1 포함 또는 GS1 심볼 식별자 접두사)인 경우 AI 분리 파싱.
        /// Result에 Gs1Formatted, Gs1{AI} 키 추가.
        /// </summary>
        public bool ParseGs1
        {
            get => _parseGs1;
            set => SetProperty(ref _parseGs1, value);
        }

        private bool _enableQualityGrading;
        /// <summary>
        /// ISO/IEC 15415 간소화 품질 등급 계산 (DataMatrix 한정).
        /// 활성화 시 OverallGrade·SC·MOD·FPD·AN·PPM·SymbolSize 결과 키 노출.
        /// </summary>
        public bool EnableQualityGrading
        {
            get => _enableQualityGrading;
            set => SetProperty(ref _enableQualityGrading, value);
        }

        private CodeQualityGrade _minPassGrade = CodeQualityGrade.F;
        /// <summary>
        /// 품질 등급 활성화 시 PASS 판정의 최소 OverallGrade.
        /// 기본 F = 게이팅 비활성화 (디코딩 성공이면 PASS, 등급은 정보 제공 용도).
        /// 등급 자체를 PASS 조건에 포함하려면 C 이상으로 상향.
        /// </summary>
        public CodeQualityGrade MinPassGrade
        {
            get => _minPassGrade;
            set => SetProperty(ref _minPassGrade, value);
        }

        // ── 표시 설정 ──

        private bool _drawOverlay = true;
        public bool DrawOverlay
        {
            get => _drawOverlay;
            set => SetProperty(ref _drawOverlay, value);
        }

        public CodeReaderTool() : this(new ZXingCodeReader()) { }

        public CodeReaderTool(ICodeReader codeReader)
        {
            _codeReader = codeReader;
            Name = "CodeReader";
            ToolType = "CodeReaderTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                // 회전된 ROI 여부 판단: ROIAngle이 설정되어 있으면 WarpAffine 정규화 사용
                bool useAffineROI = UseROI && Math.Abs(ROIAngle) > 0.1
                    && ROIWidth > 0 && ROIHeight > 0;

                Mat workImage;
                double affineAngle = 0;
                double affineCenterX = 0, affineCenterY = 0;
                int affineW = 0, affineH = 0;

                if (useAffineROI)
                {
                    // WarpAffine으로 회전된 ROI 영역을 수평 정규화하여 추출
                    affineCenterX = ROICenterX;
                    affineCenterY = ROICenterY;
                    affineW = ROIWidth;
                    affineH = ROIHeight;
                    affineAngle = ROIAngle;

                    workImage = ExtractAffineROI(inputImage, affineCenterX, affineCenterY,
                        affineW, affineH, affineAngle);
                }
                else
                {
                    // 기본 축 정렬 ROI crop
                    workImage = GetROIImage(inputImage);
                    // 비사각형 ROI(Circle/Polygon)인 경우 도형 밖을 흰색으로 가려 인식 영역 한정
                    ApplyShapeMaskInPlace(workImage, inputImage, Scalar.White);
                }

                try
                {
                    // 1) 1차: 전체 ROI 직접 디코딩
                    var codes = _codeReader.Read(workImage, CodeReaderMode, TryHarder);

                    // 2) 2차 (fallback): DataMatrix 후보 영역 사전 탐색 후 영역별 디코딩.
                    //    DM/Auto 모드 + UseLocalization 활성화 시. 1차에서 검출된 텍스트는 중복 제거.
                    //    각 후보에 대해 orig → 2x → CLAHE → CLAHE+2x 다단계 fallback 적용
                    //    (작은 DM은 모듈당 픽셀 부족으로 ZXing이 실패 → 업스케일로 회복).
                    if (UseLocalization &&
                        (CodeReaderMode == CodeReaderMode.DataMatrix || CodeReaderMode == CodeReaderMode.Auto))
                    {
                        var candidates = DataMatrixLocator.FindCandidates(workImage);
                        foreach (var rect in candidates)
                        {
                            if (rect.Width <= 0 || rect.Height <= 0) continue;
                            using var sub = new Mat(workImage, rect);
                            var subCodes = DecodeCandidateWithFallbacks(sub);
                            foreach (var code in subCodes)
                            {
                                if (codes.Any(c => string.Equals(c.Text, code.Text, StringComparison.Ordinal)))
                                    continue;
                                // sub 좌표 → workImage 좌표로 평행이동 (스케일은 fallback 내부에서 이미 역변환)
                                code.Points = code.Points
                                    .Select(p => new Point2f(p.X + rect.X, p.Y + rect.Y))
                                    .ToArray();
                                codes.Add(code);
                            }
                        }
                    }

                    // MaxCodeCount 제한
                    if (codes.Count > MaxCodeCount)
                        codes = codes.Take(MaxCodeCount).ToList();

                    // 결과 데이터 저장
                    result.Data["CodeCount"] = codes.Count;

                    if (codes.Count > 0)
                    {
                        result.Data["DecodedText"] = codes.Count == 1
                            ? codes[0].Text
                            : string.Join(", ", codes.Select(c => c.Text));
                        result.Data["CodeFormat"] = codes.Count == 1
                            ? codes[0].Format
                            : string.Join(", ", codes.Select(c => c.Format));
                    }
                    else
                    {
                        result.Data["DecodedText"] = string.Empty;
                        result.Data["CodeFormat"] = string.Empty;
                    }

                    // GS1 파싱 (첫 번째 GS1 형식 코드 대상)
                    if (ParseGs1 && codes.Count > 0)
                    {
                        var gs1Source = codes.FirstOrDefault(c => Gs1Parser.LooksLikeGs1(c.Text));
                        if (gs1Source != null)
                        {
                            var elements = Gs1Parser.Parse(gs1Source.Text);
                            result.Data["Gs1Formatted"] = Gs1Parser.Format(elements);
                            result.Data["Gs1ElementCount"] = elements.Count;
                            foreach (var el in elements)
                                result.Data[$"Gs1_{el.AI}"] = el.Value;
                        }
                    }

                    // 품질 등급 (DataMatrix 한정)
                    DataMatrixQualityReport? quality = null;
                    if (EnableQualityGrading && codes.Count > 0)
                    {
                        var dm = codes.FirstOrDefault(c =>
                            c.Format.Equals("DATA_MATRIX", StringComparison.OrdinalIgnoreCase) && c.Points.Length >= 3);
                        if (dm != null)
                        {
                            using Mat gray = workImage.Channels() > 1
                                ? workImage.CvtColor(ColorConversionCodes.BGR2GRAY)
                                : workImage.Clone();
                            quality = DataMatrixQualityGrader.Grade(gray, dm.Points, decoded: true);
                            result.Data["OverallGrade"] = quality.OverallGrade.ToString();
                            result.Data["SymbolContrast"] = quality.SymbolContrast;
                            result.Data["Modulation"] = quality.Modulation;
                            result.Data["FixedPatternDamage"] = quality.FixedPatternDamage;
                            result.Data["AxialNonuniformity"] = quality.AxialNonuniformity;
                            result.Data["PixelsPerModule"] = quality.PixelsPerModule;
                            result.Data["SymbolSize"] = quality.SymbolSize;
                        }
                    }

                    // 판정
                    bool success;
                    if (EnableVerification && codes.Count > 0)
                    {
                        if (UseRegexMatch)
                        {
                            try { success = codes.Any(c => Regex.IsMatch(c.Text, ExpectedText)); }
                            catch (RegexParseException) { success = false; }
                        }
                        else
                        {
                            success = codes.Any(c => c.Text == ExpectedText);
                        }
                    }
                    else
                    {
                        success = codes.Count > 0;
                    }

                    // 품질 등급 게이팅
                    if (success && quality != null && (int)quality.OverallGrade < (int)MinPassGrade)
                        success = false;

                    result.Data["Success"] = success;
                    result.Success = success;
                    result.Message = codes.Count > 0
                        ? (quality != null
                            ? $"코드 {codes.Count}개 검출: {codes[0].Text} | {quality.OverallGrade}"
                            : $"코드 {codes.Count}개 검출: {codes[0].Text}")
                        : "코드를 찾을 수 없습니다";

                    // 오버레이 그리기
                    if (DrawOverlay)
                    {
                        Mat overlayImage = GetColorOverlayBase(inputImage);

                        for (int i = 0; i < codes.Count; i++)
                        {
                            var code = codes[i];
                            var color = success ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

                            if (code.Points.Length >= 2)
                            {
                                Point[] pts;

                                if (useAffineROI)
                                {
                                    // 정규화 좌표 → 원본 이미지 좌표 역변환
                                    pts = code.Points.Select(p =>
                                        TransformPointToOriginal(p.X, p.Y,
                                            affineCenterX, affineCenterY,
                                            affineW, affineH, affineAngle)).ToArray();
                                }
                                else
                                {
                                    // 축 정렬 ROI 오프셋 적용
                                    var adjustedROI = GetAdjustedROI(inputImage);
                                    int offsetX = UseROI ? adjustedROI.X : 0;
                                    int offsetY = UseROI ? adjustedROI.Y : 0;
                                    pts = code.Points.Select(p =>
                                        new Point((int)(p.X + offsetX), (int)(p.Y + offsetY))).ToArray();
                                }

                                if (pts.Length >= 3)
                                {
                                    Cv2.Polylines(overlayImage, new[] { pts }, true, color, 2);
                                }
                                else
                                {
                                    int x1 = pts.Min(p => p.X);
                                    int y1 = pts.Min(p => p.Y);
                                    int x2 = pts.Max(p => p.X);
                                    int y2 = pts.Max(p => p.Y);
                                    Cv2.Rectangle(overlayImage, new Point(x1, y1), new Point(x2, y2), color, 2);
                                }

                                var textPos = new Point(pts[0].X, pts[0].Y - 10);
                                if (textPos.Y < 15) textPos.Y = pts[0].Y + 20;
                                Cv2.PutText(overlayImage, $"{code.Format}: {code.Text}",
                                    textPos, HersheyFonts.HersheySimplex, 0.5, color, 1);
                            }
                        }

                        // 품질 등급 / GS1 정보 상단 배지
                        int badgeY = 22;
                        if (quality != null)
                        {
                            var gradeColor = (int)quality.OverallGrade >= (int)MinPassGrade
                                ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
                            Cv2.PutText(overlayImage, quality.FormatSummary(),
                                new Point(10, badgeY), HersheyFonts.HersheySimplex, 0.5, gradeColor, 1);
                            badgeY += 20;
                        }
                        if (ParseGs1 && result.Data.TryGetValue("Gs1Formatted", out var gs1Obj))
                        {
                            string gs1Text = gs1Obj?.ToString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(gs1Text))
                                Cv2.PutText(overlayImage, "GS1: " + gs1Text,
                                    new Point(10, badgeY), HersheyFonts.HersheySimplex, 0.5,
                                    new Scalar(255, 255, 0), 1);
                        }

                        result.OverlayImage = overlayImage;
                    }

                    result.OutputImage = inputImage.Clone();
                }
                finally
                {
                    if (workImage != inputImage)
                        workImage.Dispose();
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"코드 인식 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        /// <summary>
        /// 후보 영역 sub-image에 대해 다단계 fallback 디코딩.
        /// 시도 순: 원본 → 2x 업스케일 → CLAHE → CLAHE+2x.
        /// 2x 스케일 시 반환되는 Points는 원본 좌표계로 역변환하여 반환.
        /// </summary>
        private List<CodeResult> DecodeCandidateWithFallbacks(Mat sub)
        {
            // 1) 원본
            var results = _codeReader.Read(sub, CodeReaderMode.DataMatrix, TryHarder);
            if (results.Count > 0) return results;

            // 2) 2x cubic 업스케일 — 작은 DM 모듈 회복
            using var sub2x = new Mat();
            Cv2.Resize(sub, sub2x, new Size(sub.Width * 2, sub.Height * 2),
                0, 0, InterpolationFlags.Cubic);
            results = _codeReader.Read(sub2x, CodeReaderMode.DataMatrix, TryHarder);
            if (results.Count > 0)
            {
                ScalePointsInPlace(results, 0.5f);
                return results;
            }

            // 3) CLAHE — 저대비/레이저 마킹 보조
            using var gray = sub.Channels() > 1
                ? sub.CvtColor(ColorConversionCodes.BGR2GRAY) : sub.Clone();
            using var clahe = Cv2.CreateCLAHE(4.0, new Size(8, 8));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);
            results = _codeReader.Read(enhanced, CodeReaderMode.DataMatrix, TryHarder);
            if (results.Count > 0) return results;

            // 4) CLAHE + 2x
            using var enh2x = new Mat();
            Cv2.Resize(enhanced, enh2x, new Size(enhanced.Width * 2, enhanced.Height * 2),
                0, 0, InterpolationFlags.Cubic);
            results = _codeReader.Read(enh2x, CodeReaderMode.DataMatrix, TryHarder);
            if (results.Count > 0)
            {
                ScalePointsInPlace(results, 0.5f);
                return results;
            }

            return new List<CodeResult>();
        }

        private static void ScalePointsInPlace(List<CodeResult> results, float factor)
        {
            foreach (var c in results)
                c.Points = c.Points.Select(p => new Point2f(p.X * factor, p.Y * factor)).ToArray();
        }

        /// <summary>
        /// WarpAffine을 사용하여 회전된 ROI 영역을 수평 정규화된 이미지로 추출.
        /// 회전을 제거하여 코드가 똑바로 서 있는 상태로 만듦.
        /// </summary>
        private static Mat ExtractAffineROI(Mat image, double centerX, double centerY,
            int width, int height, double angle)
        {
            var center = new Point2f((float)centerX, (float)centerY);

            // 역회전 행렬: 이미지를 -angle만큼 회전하여 ROI를 수평 정렬
            using var rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);

            // 전체 이미지에 역회전 적용
            using var rotated = new Mat();
            Cv2.WarpAffine(image, rotated, rotMat, image.Size(),
                InterpolationFlags.Linear, BorderTypes.Replicate);

            // 수평 정렬된 상태에서 중심 기준 직사각형 crop
            int x = (int)(centerX - width / 2.0);
            int y = (int)(centerY - height / 2.0);

            // 이미지 범위 클램프
            int x1 = Math.Clamp(x, 0, rotated.Width);
            int y1 = Math.Clamp(y, 0, rotated.Height);
            int x2 = Math.Clamp(x + width, 0, rotated.Width);
            int y2 = Math.Clamp(y + height, 0, rotated.Height);

            if (x2 - x1 <= 0 || y2 - y1 <= 0)
                return image.Clone();

            return new Mat(rotated, new Rect(x1, y1, x2 - x1, y2 - y1)).Clone();
        }

        /// <summary>
        /// 정규화된 ROI 내부 좌표를 원본 이미지 좌표로 역변환.
        /// (정규화 이미지 좌표 → ROI 로컬 좌표 → 회전 적용 → 원본 이미지 좌표)
        /// </summary>
        private static Point TransformPointToOriginal(float px, float py,
            double centerX, double centerY, int width, int height, double angle)
        {
            // 정규화 이미지의 좌상단은 ROI 중심 기준 (-width/2, -height/2)
            double localX = px - width / 2.0;
            double localY = py - height / 2.0;

            // 원래 각도만큼 회전 (정규화 시 -angle 했으므로 +angle로 복원)
            double rad = angle * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            double origX = localX * cos - localY * sin + centerX;
            double origY = localX * sin + localY * cos + centerY;

            return new Point((int)origX, (int)origY);
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "DecodedText", "CodeFormat", "CodeCount",
                "Gs1Formatted", "Gs1ElementCount",
                "OverallGrade", "SymbolContrast", "Modulation",
                "FixedPatternDamage", "AxialNonuniformity",
                "PixelsPerModule", "SymbolSize"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new CodeReaderTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                CodeReaderMode = this.CodeReaderMode,
                MaxCodeCount = this.MaxCodeCount,
                TryHarder = this.TryHarder,
                UseLocalization = this.UseLocalization,
                EnableVerification = this.EnableVerification,
                ExpectedText = this.ExpectedText,
                UseRegexMatch = this.UseRegexMatch,
                DrawOverlay = this.DrawOverlay,
                ParseGs1 = this.ParseGs1,
                EnableQualityGrading = this.EnableQualityGrading,
                MinPassGrade = this.MinPassGrade
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
