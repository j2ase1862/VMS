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
                }

                try
                {
                    // 코드 인식
                    var codes = _codeReader.Read(workImage, CodeReaderMode, TryHarder);

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

                    // 판정
                    bool success;
                    if (EnableVerification && codes.Count > 0)
                    {
                        if (UseRegexMatch)
                        {
                            try
                            {
                                success = codes.Any(c => Regex.IsMatch(c.Text, ExpectedText));
                            }
                            catch (RegexParseException)
                            {
                                success = false;
                            }
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

                    result.Data["Success"] = success;
                    result.Success = success;
                    result.Message = codes.Count > 0
                        ? $"코드 {codes.Count}개 검출: {codes[0].Text}"
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
            return new List<string> { "Success", "DecodedText", "CodeFormat", "CodeCount" };
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
                EnableVerification = this.EnableVerification,
                ExpectedText = this.ExpectedText,
                UseRegexMatch = this.UseRegexMatch,
                DrawOverlay = this.DrawOverlay
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
