using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.VisionTools.Calibration
{
    /// <summary>
    /// 캘리브레이션 적용 도구 (Cognex CogImageRectifyTool 대체).
    /// VisionService.CurrentCalibrationMetadata에서 가져온 카메라 행렬·왜곡 계수로 cv::undistort,
    /// 그리고 NPoint 호모그래피가 있으면 평면 정사영을 추가 적용.
    /// 캘리브레이션이 없으면 입력 이미지를 그대로 통과 (실패가 아닌 pass-through).
    /// </summary>
    public class ImageRectifyTool : VisionToolBase
    {
        private bool _undistort = true;
        public bool Undistort
        {
            get => _undistort;
            set => SetProperty(ref _undistort, value);
        }

        private bool _applyHomography;
        public bool ApplyHomography
        {
            get => _applyHomography;
            set => SetProperty(ref _applyHomography, value);
        }

        public ImageRectifyTool()
        {
            Name = "Image Rectify";
            ToolType = "ImageRectifyTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var meta = VisionService.Instance.CurrentCalibrationMetadata;
                if (meta == null)
                {
                    // 캘리브레이션이 없으면 pass-through (파이프라인 깨지지 않게)
                    result.Success = true;
                    result.Message = "No calibration loaded — pass-through.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                Mat current = inputImage.Clone();
                bool didUndistort = false;
                bool didHomography = false;

                // 1) 렌즈 왜곡 보정
                if (Undistort && meta.CameraMatrix != null && meta.DistortionCoeffs != null)
                {
                    using var K = meta.ToCameraMatrixMat();
                    using var D = meta.ToDistortionMat();
                    if (K != null && D != null)
                    {
                        var undistorted = new Mat();
                        Cv2.Undistort(current, undistorted, K, D);
                        current.Dispose();
                        current = undistorted;
                        didUndistort = true;
                    }
                }

                // 2) 평면 호모그래피 (NPoint 캘리브레이션에서만 채워짐)
                if (ApplyHomography && meta.HomographyPx2Mm != null)
                {
                    using var H = meta.ToHomographyMat();
                    if (H != null)
                    {
                        var warped = new Mat();
                        Cv2.WarpPerspective(current, warped, H, inputImage.Size());
                        current.Dispose();
                        current = warped;
                        didHomography = true;
                    }
                }

                result.Success = true;
                result.Message = (didUndistort, didHomography) switch
                {
                    (true, true)   => "Undistorted + perspective rectified.",
                    (true, false)  => "Undistorted.",
                    (false, true)  => "Perspective rectified.",
                    _              => "No operation applied (check toggles or calibration data)."
                };
                result.OutputImage = current;
                result.Data["DidUndistort"] = didUndistort;
                result.Data["DidHomography"] = didHomography;
                result.Data["PixelSizeMm"] = meta.PixelSizeMm;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Image rectify failed: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "DidUndistort", "DidHomography", "PixelSizeMm"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new ImageRectifyTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                Undistort = this.Undistort,
                ApplyHomography = this.ApplyHomography
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
