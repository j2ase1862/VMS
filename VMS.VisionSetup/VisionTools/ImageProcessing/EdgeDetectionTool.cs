using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    /// <summary>
    /// Edge 검출 도구
    /// (Cognex VisionPro CogSobelEdgeTool 대체)
    /// </summary>
    public class EdgeDetectionTool : VisionToolBase
    {
        private EdgeDetectionMethod _method = EdgeDetectionMethod.Canny;
        public EdgeDetectionMethod Method
        {
            get => _method;
            set => SetProperty(ref _method, value);
        }

        // Canny 파라미터
        private double _cannyThreshold1 = 50;
        public double CannyThreshold1
        {
            get => _cannyThreshold1;
            set => SetProperty(ref _cannyThreshold1, value);
        }

        private double _cannyThreshold2 = 150;
        public double CannyThreshold2
        {
            get => _cannyThreshold2;
            set => SetProperty(ref _cannyThreshold2, value);
        }

        private int _cannyApertureSize = 3;
        public int CannyApertureSize
        {
            get => _cannyApertureSize;
            set => SetProperty(ref _cannyApertureSize, value % 2 == 0 ? value + 1 : value);
        }

        private bool _l2Gradient = false;
        public bool L2Gradient
        {
            get => _l2Gradient;
            set => SetProperty(ref _l2Gradient, value);
        }

        // Sobel/Scharr 파라미터
        private int _sobelKernelSize = 3;
        public int SobelKernelSize
        {
            get => _sobelKernelSize;
            set => SetProperty(ref _sobelKernelSize, value % 2 == 0 ? value + 1 : value);
        }

        private int _dx = 1;
        public int Dx
        {
            get => _dx;
            set => SetProperty(ref _dx, value);
        }

        private int _dy = 1;
        public int Dy
        {
            get => _dy;
            set => SetProperty(ref _dy, value);
        }

        public EdgeDetectionTool()
        {
            Name = "Edge Detection";
            ToolType = "EdgeDetectionTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat workImage = GetROIImage(inputImage);
                Mat grayImage = new Mat();
                Mat outputImage = new Mat();

                // Grayscale 변환
                if (workImage.Channels() > 1)
                    Cv2.CvtColor(workImage, grayImage, ColorConversionCodes.BGR2GRAY);
                else
                    grayImage = workImage.Clone();

                switch (Method)
                {
                    case EdgeDetectionMethod.Canny:
                        Cv2.Canny(grayImage, outputImage, CannyThreshold1, CannyThreshold2, CannyApertureSize, L2Gradient);
                        break;

                    case EdgeDetectionMethod.Sobel:
                        Mat sobelX = new Mat();
                        Mat sobelY = new Mat();
                        Cv2.Sobel(grayImage, sobelX, MatType.CV_64F, 1, 0, SobelKernelSize);
                        Cv2.Sobel(grayImage, sobelY, MatType.CV_64F, 0, 1, SobelKernelSize);
                        Mat absX = new Mat();
                        Mat absY = new Mat();
                        Cv2.ConvertScaleAbs(sobelX, absX);
                        Cv2.ConvertScaleAbs(sobelY, absY);
                        Cv2.AddWeighted(absX, 0.5, absY, 0.5, 0, outputImage);
                        sobelX.Dispose();
                        sobelY.Dispose();
                        absX.Dispose();
                        absY.Dispose();
                        break;

                    case EdgeDetectionMethod.Scharr:
                        Mat scharrX = new Mat();
                        Mat scharrY = new Mat();
                        Cv2.Scharr(grayImage, scharrX, MatType.CV_64F, 1, 0);
                        Cv2.Scharr(grayImage, scharrY, MatType.CV_64F, 0, 1);
                        Mat absScharrX = new Mat();
                        Mat absScharrY = new Mat();
                        Cv2.ConvertScaleAbs(scharrX, absScharrX);
                        Cv2.ConvertScaleAbs(scharrY, absScharrY);
                        Cv2.AddWeighted(absScharrX, 0.5, absScharrY, 0.5, 0, outputImage);
                        scharrX.Dispose();
                        scharrY.Dispose();
                        absScharrX.Dispose();
                        absScharrY.Dispose();
                        break;

                    case EdgeDetectionMethod.Laplacian:
                        Mat laplacian = new Mat();
                        Cv2.Laplacian(grayImage, laplacian, MatType.CV_64F, SobelKernelSize);
                        Cv2.ConvertScaleAbs(laplacian, outputImage);
                        laplacian.Dispose();
                        break;
                }

                // Edge 픽셀 수 계산
                int edgePixels = Cv2.CountNonZero(outputImage);

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
                result.Message = $"{Method} Edge 검출 완료";
                result.OutputImage = finalOutput;
                result.Data["Method"] = Method.ToString();
                result.Data["EdgePixelCount"] = edgePixels;
                result.Data["EdgePixelRatio"] = (double)edgePixels / (finalOutput.Width * finalOutput.Height);
                if (UseROI)
                {
                    result.Data["ROI"] = $"X:{ROI.X}, Y:{ROI.Y}, W:{ROI.Width}, H:{ROI.Height}";
                }

                grayImage.Dispose();
                if (workImage != inputImage)
                    workImage.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Edge 검출 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        public override VisionToolBase Clone()
        {
            return new EdgeDetectionTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                Method = this.Method,
                CannyThreshold1 = this.CannyThreshold1,
                CannyThreshold2 = this.CannyThreshold2,
                CannyApertureSize = this.CannyApertureSize,
                L2Gradient = this.L2Gradient,
                SobelKernelSize = this.SobelKernelSize,
                Dx = this.Dx,
                Dy = this.Dy
            };
        }
    }

    public enum EdgeDetectionMethod
    {
        Canny,
        Sobel,
        Scharr,
        Laplacian
    }
}
