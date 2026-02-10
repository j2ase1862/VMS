using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    /// <summary>
    /// 형태학적 처리 도구 (팽창, 침식, 열기, 닫기 등)
    /// (Cognex VisionPro CogBlobTool의 Morphology 설정 대체)
    /// </summary>
    public class MorphologyTool : VisionToolBase
    {
        private MorphologyOperation _operation = MorphologyOperation.Dilate;
        public MorphologyOperation Operation
        {
            get => _operation;
            set => SetProperty(ref _operation, value);
        }

        private MorphShapes _kernelShape = MorphShapes.Rect;
        public MorphShapes KernelShape
        {
            get => _kernelShape;
            set => SetProperty(ref _kernelShape, value);
        }

        private int _kernelWidth = 3;
        public int KernelWidth
        {
            get => _kernelWidth;
            set => SetProperty(ref _kernelWidth, Math.Max(1, value));
        }

        private int _kernelHeight = 3;
        public int KernelHeight
        {
            get => _kernelHeight;
            set => SetProperty(ref _kernelHeight, Math.Max(1, value));
        }

        private int _iterations = 1;
        public int Iterations
        {
            get => _iterations;
            set => SetProperty(ref _iterations, Math.Max(1, value));
        }

        public MorphologyTool()
        {
            Name = "Morphology";
            ToolType = "MorphologyTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat workImage = GetROIImage(inputImage);
                Mat outputImage = new Mat();

                // 커널 생성
                var kernel = Cv2.GetStructuringElement(KernelShape, new Size(KernelWidth, KernelHeight));

                switch (Operation)
                {
                    case MorphologyOperation.Erode:
                        Cv2.Erode(workImage, outputImage, kernel, iterations: Iterations);
                        break;

                    case MorphologyOperation.Dilate:
                        Cv2.Dilate(workImage, outputImage, kernel, iterations: Iterations);
                        break;

                    case MorphologyOperation.Open:
                        Cv2.MorphologyEx(workImage, outputImage, MorphTypes.Open, kernel, iterations: Iterations);
                        break;

                    case MorphologyOperation.Close:
                        Cv2.MorphologyEx(workImage, outputImage, MorphTypes.Close, kernel, iterations: Iterations);
                        break;

                    case MorphologyOperation.Gradient:
                        Cv2.MorphologyEx(workImage, outputImage, MorphTypes.Gradient, kernel, iterations: Iterations);
                        break;

                    case MorphologyOperation.TopHat:
                        Cv2.MorphologyEx(workImage, outputImage, MorphTypes.TopHat, kernel, iterations: Iterations);
                        break;

                    case MorphologyOperation.BlackHat:
                        Cv2.MorphologyEx(workImage, outputImage, MorphTypes.BlackHat, kernel, iterations: Iterations);
                        break;

                    default:
                        outputImage = workImage.Clone();
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
                result.Message = $"{Operation} 처리 완료 (Kernel: {KernelWidth}x{KernelHeight}, Iterations: {Iterations})";
                result.OutputImage = finalOutput;
                result.Data["Operation"] = Operation.ToString();
                result.Data["KernelShape"] = KernelShape.ToString();
                result.Data["KernelSize"] = $"{KernelWidth}x{KernelHeight}";
                result.Data["Iterations"] = Iterations;
                if (UseROI)
                {
                    result.Data["ROI"] = $"X:{ROI.X}, Y:{ROI.Y}, W:{ROI.Width}, H:{ROI.Height}";
                }

                kernel.Dispose();
                if (workImage != inputImage)
                    workImage.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Morphology 처리 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        public override VisionToolBase Clone()
        {
            return new MorphologyTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                Operation = this.Operation,
                KernelShape = this.KernelShape,
                KernelWidth = this.KernelWidth,
                KernelHeight = this.KernelHeight,
                Iterations = this.Iterations
            };
        }
    }

    public enum MorphologyOperation
    {
        Erode,      // 침식
        Dilate,     // 팽창
        Open,       // 열기 (침식 후 팽창)
        Close,      // 닫기 (팽창 후 침식)
        Gradient,   // 그라디언트 (팽창 - 침식)
        TopHat,     // 탑햇 (원본 - 열기)
        BlackHat    // 블랙햇 (닫기 - 원본)
    }
}
