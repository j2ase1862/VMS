using VMS.VisionSetup.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    /// <summary>
    /// Blur 처리 도구 - 여러 블러 타입 지원
    /// (Cognex VisionPro CogImageFilterTool 대체)
    /// </summary>
    public class BlurTool : VisionToolBase
    {
        private BlurType _blurType = BlurType.Gaussian;
        public BlurType BlurType
        {
            get => _blurType;
            set => SetProperty(ref _blurType, value);
        }

        private int _kernelSize = 5;
        public int KernelSize
        {
            get => _kernelSize;
            set => SetProperty(ref _kernelSize, value % 2 == 0 ? value + 1 : value); // 홀수만 허용
        }

        private double _sigmaX = 0;
        public double SigmaX
        {
            get => _sigmaX;
            set => SetProperty(ref _sigmaX, value);
        }

        private double _sigmaY = 0;
        public double SigmaY
        {
            get => _sigmaY;
            set => SetProperty(ref _sigmaY, value);
        }

        // Bilateral Filter용
        private double _sigmaColor = 75;
        public double SigmaColor
        {
            get => _sigmaColor;
            set => SetProperty(ref _sigmaColor, value);
        }

        private double _sigmaSpace = 75;
        public double SigmaSpace
        {
            get => _sigmaSpace;
            set => SetProperty(ref _sigmaSpace, value);
        }

        public BlurTool()
        {
            Name = "Blur";
            ToolType = "BlurTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat workImage = GetROIImage(inputImage);
                Mat outputImage = new Mat();
                var ksize = new Size(KernelSize, KernelSize);

                switch (BlurType)
                {
                    case BlurType.Average:
                        Cv2.Blur(workImage, outputImage, ksize);
                        break;

                    case BlurType.Gaussian:
                        Cv2.GaussianBlur(workImage, outputImage, ksize, SigmaX, SigmaY);
                        break;

                    case BlurType.Median:
                        Cv2.MedianBlur(workImage, outputImage, KernelSize);
                        break;

                    case BlurType.Bilateral:
                        Cv2.BilateralFilter(workImage, outputImage, KernelSize, SigmaColor, SigmaSpace);
                        break;

                    default:
                        Cv2.GaussianBlur(workImage, outputImage, ksize, SigmaX, SigmaY);
                        break;
                }

                // ROI가 사용된 경우 원본 이미지 크기로 결과 적용
                Mat finalOutput;
                if (UseROI && ROI.Width > 0 && ROI.Height > 0)
                {
                    finalOutput = ApplyROIResult(inputImage, outputImage);
                    outputImage.Dispose();
                }
                else
                {
                    finalOutput = outputImage;
                }

                result.Success = true;
                result.Message = $"{BlurType} Blur 완료 (Kernel: {KernelSize})";
                result.OutputImage = finalOutput;
                result.Data["BlurType"] = BlurType.ToString();
                result.Data["KernelSize"] = KernelSize;
                if (UseROI)
                {
                    result.Data["ROI"] = $"X:{ROI.X}, Y:{ROI.Y}, W:{ROI.Width}, H:{ROI.Height}";
                }

                if (workImage != inputImage)
                    workImage.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Blur 처리 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        public override VisionToolBase Clone()
        {
            return new BlurTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                BlurType = this.BlurType,
                KernelSize = this.KernelSize,
                SigmaX = this.SigmaX,
                SigmaY = this.SigmaY,
                SigmaColor = this.SigmaColor,
                SigmaSpace = this.SigmaSpace
            };
        }
    }

    public enum BlurType
    {
        Average,
        Gaussian,
        Median,
        Bilateral
    }
}
