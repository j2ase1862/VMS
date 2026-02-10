using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    /// <summary>
    /// Grayscale 변환 도구 (Cognex VisionPro CogImageConvertTool 대체)
    /// </summary>
    public class GrayscaleTool : VisionToolBase
    {
        public GrayscaleTool()
        {
            Name = "Grayscale";
            ToolType = "GrayscaleTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Mat workImage = GetROIImage(inputImage);
                Mat outputImage = new Mat();

                // 이미 Grayscale인지 확인
                if (workImage.Channels() == 1)
                {
                    outputImage = workImage.Clone();
                }
                else
                {
                    Cv2.CvtColor(workImage, outputImage, ColorConversionCodes.BGR2GRAY);
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
                result.Message = "Grayscale 변환 완료";
                result.OutputImage = finalOutput;
                result.Data["Channels"] = finalOutput.Channels();
                result.Data["Width"] = finalOutput.Width;
                result.Data["Height"] = finalOutput.Height;
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
                result.Message = $"Grayscale 변환 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        public override VisionToolBase Clone()
        {
            return new GrayscaleTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ROI = this.ROI,
                UseROI = this.UseROI
            };
        }
    }
}
