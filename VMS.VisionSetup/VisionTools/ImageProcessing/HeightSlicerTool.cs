using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace VMS.VisionSetup.VisionTools.ImageProcessing
{
    /// <summary>
    /// 3D Depth 데이터를 특정 높이 기준으로 2D 이미지로 변환하는 도구
    /// </summary>
    public class HeightSlicerTool : VisionToolBase
    {
        private float _minZ = 0;
        public float MinZ
        {
            get => _minZ;
            set => SetProperty(ref _minZ, value);
        }

        private float _maxZ = 1000;
        public float MaxZ
        {
            get => _maxZ;
            set => SetProperty(ref _maxZ, value);
        }

        public HeightSlicerTool()
        {
            Name = "Height Slicer";
            ToolType = "HeightSlicerTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                // 입력 타입 확인 및 변환
                Mat floatInput;
                bool needDisposeFloat = false;

                if (inputImage.Type() == MatType.CV_32FC1)
                {
                    floatInput = inputImage;
                }
                else if (inputImage.Type() == MatType.CV_8UC1)
                {
                    floatInput = new Mat();
                    inputImage.ConvertTo(floatInput, MatType.CV_32FC1);
                    needDisposeFloat = true;
                }
                else
                {
                    result.Success = false;
                    result.Message = $"Slicing 실패: 지원하지 않는 이미지 형식입니다 ({inputImage.Type()})";
                    sw.Stop();
                    ExecutionTime = sw.Elapsed.TotalMilliseconds;
                    LastResult = result;
                    return result;
                }

                Mat workImage = GetROIImage(floatInput);
                Mat mask = new Mat();
                Mat normalized = new Mat();
                Mat outputImage = new Mat();

                // 1. 범위 필터링 (InRange)
                Cv2.InRange(workImage, new Scalar(MinZ), new Scalar(MaxZ), mask);

                // 2. 8비트 정규화 변환
                double range = MaxZ - MinZ;
                double scale = range > 0.0001 ? 255.0 / range : 0;
                double shift = range > 0.0001 ? -MinZ * scale : 0;
                workImage.ConvertTo(normalized, MatType.CV_8UC1, scale, shift);

                // 3. 마스크 적용 및 결과 생성
                normalized.CopyTo(outputImage, mask);

                // ROI 결과 적용 (필요시)
                Mat finalOutput = UseROI ? ApplyROIResult(floatInput, outputImage) : outputImage;

                result.Success = true;
                result.OutputImage = finalOutput;
                result.Data["MinZ"] = MinZ;
                result.Data["MaxZ"] = MaxZ;
                result.Message = $"Slicing 완료 ({MinZ}mm ~ {MaxZ}mm)";

                // 메모리 해제
                mask.Dispose();
                normalized.Dispose();
                if (outputImage != finalOutput) outputImage.Dispose();
                if (workImage != floatInput) workImage.Dispose();
                if (needDisposeFloat) floatInput.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Slicing 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        public override VisionToolBase Clone()
        {
            var clone = new HeightSlicerTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                MinZ = this.MinZ,
                MaxZ = this.MaxZ,
                IsEnabled = this.IsEnabled,
                UseROI = this.UseROI,
                ROI = this.ROI
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
