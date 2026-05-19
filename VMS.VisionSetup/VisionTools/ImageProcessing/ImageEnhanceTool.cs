using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    public enum ImageEnhanceMode
    {
        /// <summary>Laplacian 3x3 sharpening — 단순 고대비 강화.</summary>
        Sharpen,
        /// <summary>Unsharp Mask — 원본 + amount × (원본 − Gaussian blur). 산업 표준.</summary>
        UnsharpMask
    }

    /// <summary>
    /// 이미지 선명도 강화 도구. OCR/CodeReader 전단 전처리에 유용.
    /// Cognex CogIPOneImageTool의 Sharpen / Unsharp 연산자 대응.
    /// </summary>
    public class ImageEnhanceTool : VisionToolBase
    {
        private ImageEnhanceMode _mode = ImageEnhanceMode.UnsharpMask;
        public ImageEnhanceMode Mode
        {
            get => _mode;
            set => SetProperty(ref _mode, value);
        }

        private double _amount = 1.0;
        /// <summary>강화 강도. Sharpen은 커널 가중치, UnsharpMask는 (1+amount)·원본 - amount·blur 계수.</summary>
        public double Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, Math.Clamp(value, 0.1, 5.0));
        }

        private int _blurKernelSize = 5;
        /// <summary>UnsharpMask 전용 — Gaussian blur 커널 (홀수, 3~31).</summary>
        public int BlurKernelSize
        {
            get => _blurKernelSize;
            set
            {
                int v = Math.Max(3, value | 1); // 홀수 강제
                SetProperty(ref _blurKernelSize, Math.Min(v, 31));
            }
        }

        private int _threshold;
        /// <summary>UnsharpMask 전용 — 차이가 이 값 미만이면 변경 안 함 (잡음 억제). 0이면 비활성.</summary>
        public int Threshold
        {
            get => _threshold;
            set => SetProperty(ref _threshold, Math.Clamp(value, 0, 255));
        }

        public ImageEnhanceTool()
        {
            Name = "Image Enhance";
            ToolType = "ImageEnhanceTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                using var work = GetROIImage(inputImage);
                Mat enhanced = Mode switch
                {
                    ImageEnhanceMode.Sharpen => ApplySharpen(work, Amount),
                    ImageEnhanceMode.UnsharpMask => ApplyUnsharpMask(work, BlurKernelSize, Amount, Threshold),
                    _ => work.Clone()
                };

                result.OutputImage = ApplyROIResult(inputImage, enhanced);
                enhanced.Dispose();
                result.OverlayImage = result.OutputImage.Clone();
                result.Success = true;
                result.Message = $"Enhanced ({Mode}, amount={Amount:F2})";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Image Enhance 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        private static Mat ApplySharpen(Mat src, double amount)
        {
            // 중심 5, 가장자리 -1의 라플라시안 → amount로 강도 조절
            float c = (float)(1 + 4 * amount);
            float e = (float)(-amount);
            float[] kernel = { 0, e, 0, e, c, e, 0, e, 0 };
            using var k = Mat.FromPixelData(3, 3, MatType.CV_32FC1, kernel);
            var dst = new Mat();
            Cv2.Filter2D(src, dst, src.Depth(), k);
            return dst;
        }

        private static Mat ApplyUnsharpMask(Mat src, int blurSize, double amount, int threshold)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(src, blurred, new Size(blurSize, blurSize), 0);

            // dst = (1 + amount) * src − amount * blurred  (= src + amount * (src − blurred))
            var dst = new Mat();
            Cv2.AddWeighted(src, 1.0 + amount, blurred, -amount, 0, dst);

            if (threshold > 0)
            {
                // 차이가 threshold 미만인 픽셀은 원본 복원 (잡음 영역 sharp 회피)
                using var diff = new Mat();
                Cv2.Absdiff(src, blurred, diff);
                using var diffGray = diff.Channels() > 1
                    ? diff.CvtColor(ColorConversionCodes.BGR2GRAY) : diff.Clone();
                using var mask = new Mat();
                Cv2.Threshold(diffGray, mask, threshold, 255, ThresholdTypes.BinaryInv);
                src.CopyTo(dst, mask); // mask>0인 곳은 src 복원
            }
            return dst;
        }

        public override List<string> GetAvailableResultKeys() => new() { "Success" };

        public override VisionToolBase Clone()
        {
            var clone = new ImageEnhanceTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                Mode = this.Mode,
                Amount = this.Amount,
                BlurKernelSize = this.BlurKernelSize,
                Threshold = this.Threshold
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
