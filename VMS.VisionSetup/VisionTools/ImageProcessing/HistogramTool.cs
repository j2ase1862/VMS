using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    /// <summary>
    /// 히스토그램 분석 및 처리 도구
    /// (Cognex VisionPro CogHistogramTool 대체)
    /// </summary>
    public class HistogramTool : VisionToolBase
    {
        private HistogramOperation _operation = HistogramOperation.Analyze;
        public HistogramOperation Operation
        {
            get => _operation;
            set => SetProperty(ref _operation, value);
        }

        private double _clipLimit = 2.0;
        public double ClipLimit
        {
            get => _clipLimit;
            set => SetProperty(ref _clipLimit, value);
        }

        private int _tileGridWidth = 8;
        public int TileGridWidth
        {
            get => _tileGridWidth;
            set => SetProperty(ref _tileGridWidth, Math.Max(1, value));
        }

        private int _tileGridHeight = 8;
        public int TileGridHeight
        {
            get => _tileGridHeight;
            set => SetProperty(ref _tileGridHeight, Math.Max(1, value));
        }

        public HistogramTool()
        {
            Name = "Histogram";
            ToolType = "HistogramTool";
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

                switch (Operation)
                {
                    case HistogramOperation.Analyze:
                        // 히스토그램 계산
                        Mat hist = new Mat();
                        int[] histSize = { 256 };
                        Rangef[] ranges = { new Rangef(0, 256) };
                        Cv2.CalcHist(new Mat[] { grayImage }, new int[] { 0 }, null, hist, 1, histSize, ranges);

                        // 히스토그램 시각화
                        outputImage = DrawHistogram(hist);

                        // 통계 계산
                        Cv2.MinMaxLoc(grayImage, out double minVal, out double maxVal);
                        Scalar mean = Cv2.Mean(grayImage);
                        Scalar stddev;
                        Cv2.MeanStdDev(grayImage, out _, out stddev);

                        result.Data["MinValue"] = minVal;
                        result.Data["MaxValue"] = maxVal;
                        result.Data["MeanValue"] = mean.Val0;
                        result.Data["StdDev"] = stddev.Val0;

                        hist.Dispose();
                        break;

                    case HistogramOperation.Equalize:
                        // 히스토그램 평활화
                        Cv2.EqualizeHist(grayImage, outputImage);
                        result.Data["Method"] = "EqualizeHist";
                        break;

                    case HistogramOperation.CLAHE:
                        // CLAHE (Contrast Limited Adaptive Histogram Equalization)
                        using (var clahe = Cv2.CreateCLAHE(ClipLimit, new Size(TileGridWidth, TileGridHeight)))
                        {
                            clahe.Apply(grayImage, outputImage);
                        }
                        result.Data["Method"] = "CLAHE";
                        result.Data["ClipLimit"] = ClipLimit;
                        result.Data["TileGrid"] = $"{TileGridWidth}x{TileGridHeight}";
                        break;
                }

                // ROI가 사용된 경우 원본 이미지 크기로 결과 적용
                // (Analyze 모드는 히스토그램 시각화이므로 제외)
                Mat finalOutput;
                if (UseROI && ROI.Width > 0 && ROI.Height > 0 && Operation != HistogramOperation.Analyze)
                {
                    finalOutput = ApplyROIResult(inputImage, outputImage);
                    outputImage.Dispose();
                }
                else
                {
                    finalOutput = outputImage;
                }

                result.Success = true;
                result.Message = $"{Operation} 처리 완료";
                result.OutputImage = finalOutput;
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
                result.Message = $"Histogram 처리 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        private Mat DrawHistogram(Mat hist)
        {
            int histWidth = 512;
            int histHeight = 400;
            int binWidth = (int)Math.Round((double)histWidth / 256);

            Mat histImage = new Mat(histHeight, histWidth, MatType.CV_8UC3, new Scalar(20, 20, 20));

            // 정규화
            Cv2.Normalize(hist, hist, 0, histHeight, NormTypes.MinMax);

            // 히스토그램 그리기
            for (int i = 1; i < 256; i++)
            {
                Cv2.Line(histImage,
                    new Point(binWidth * (i - 1), histHeight - (int)hist.At<float>(i - 1)),
                    new Point(binWidth * i, histHeight - (int)hist.At<float>(i)),
                    new Scalar(0, 255, 0), 2);
            }

            return histImage;
        }

        public override VisionToolBase Clone()
        {
            return new HistogramTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI,
                Operation = this.Operation,
                ClipLimit = this.ClipLimit,
                TileGridWidth = this.TileGridWidth,
                TileGridHeight = this.TileGridHeight
            };
        }
    }

    public enum HistogramOperation
    {
        Analyze,    // 히스토그램 분석
        Equalize,   // 평활화
        CLAHE       // 적응형 히스토그램 평활화
    }
}
