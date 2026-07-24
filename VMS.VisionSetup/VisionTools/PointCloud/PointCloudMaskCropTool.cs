using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.VisionTools.PointCloud
{
    /// <summary>
    /// 2D 마스크 → 3D 점군 크롭 도구 (2D 세그멘테이션과 3D 분석을 잇는 다리).
    /// 입력 이미지(Image 연결)를 이진 마스크로 해석해, 마스크에 해당하는 점만
    /// VisionService.CurrentPointCloud에서 남긴다.
    /// 마스크 소스: YoloSegTool(OutputMaskImage), ThresholdTool, HeightSlicerTool 등
    /// 0이 아닌 픽셀을 가진 모든 그레이스케일 출력.
    ///
    /// 좌표 매핑: organized 점군은 X/Y에 뎁스맵 픽셀 좌표가 저장되므로
    /// (MechMindCameraAcquisition — X=col·stride, Y=row·stride) 인접 점에서 stride를 복원해
    /// 마스크 해상도와 뎁스맵 해상도가 달라도 비율로 맞춘다.
    /// unorganized 점군은 마스크와 같은 픽셀 공간이라고 가정한다(크롭/필터 이후 재적용 등).
    /// </summary>
    public class PointCloudMaskCropTool : VisionToolBase
    {
        private bool _invertMask;
        /// <summary>true면 마스크 바깥의 점을 남김 (배경 추출).</summary>
        public bool InvertMask
        {
            get => _invertMask;
            set => SetProperty(ref _invertMask, value);
        }

        private int _minMaskValue = 1;
        /// <summary>포함 판정 최소 픽셀값 (1~255). 이 값 이상인 픽셀만 마스크로 취급.</summary>
        public int MinMaskValue
        {
            get => _minMaskValue;
            set => SetProperty(ref _minMaskValue, Math.Clamp(value, 1, 255));
        }

        private int _dilatePixels;
        /// <summary>마스크 팽창 (px). 세그멘테이션 마스크가 객체 경계를 약간 못 덮을 때 보정.</summary>
        public int DilatePixels
        {
            get => _dilatePixels;
            set => SetProperty(ref _dilatePixels, Math.Clamp(value, 0, 50));
        }

        private bool _skipInvalidZ = true;
        /// <summary>Z=0 점 제외. Mech-Mind grab은 측정 실패 픽셀을 Z=0으로 저장하므로 기본 켬.</summary>
        public bool SkipInvalidZ
        {
            get => _skipInvalidZ;
            set => SetProperty(ref _skipInvalidZ, value);
        }

        public PointCloudMaskCropTool()
        {
            Name = "PointCloud Mask Crop";
            ToolType = "PointCloudMaskCropTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var src = VisionService.Instance.CurrentPointCloud;
                if (src == null || src.PointCount == 0)
                {
                    result.Success = false;
                    result.Message = "No point cloud available. Acquire from a 3D camera first.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                // 마스크 준비: 그레이 변환 → (옵션) 팽창 → 연속 버퍼 추출
                using var mask = PrepareMask(inputImage);
                mask.GetArray(out byte[] maskData);
                int maskW = mask.Width, maskH = mask.Height;
                double coverage = 100.0 * Cv2.CountNonZero(mask) / (maskW * (double)maskH);

                var (scaleX, scaleY) = ComputeMaskScale(src, maskW, maskH);

                int inputCount = src.PointCount;
                var positions = src.Positions;
                var srcColors = src.Colors;
                bool hasColors = srcColors != null && srcColors.Length >= inputCount;

                var kept = new List<int>(inputCount / 4);
                for (int i = 0; i < inputCount; i++)
                {
                    var p = positions[i];
                    if (SkipInvalidZ && p.Z == 0f) continue;

                    int mx = (int)MathF.Round(p.X * scaleX);
                    int my = (int)MathF.Round(p.Y * scaleY);
                    bool inside = mx >= 0 && mx < maskW && my >= 0 && my < maskH
                        && maskData[my * maskW + mx] >= MinMaskValue;
                    if (inside != InvertMask)
                        kept.Add(i);
                }

                result.Data["InputPoints"] = inputCount;
                result.Data["OutputPoints"] = kept.Count;
                result.Data["KeptRatio"] = inputCount > 0 ? (double)kept.Count / inputCount : 0.0;
                result.Data["MaskCoveragePercent"] = coverage;

                if (kept.Count == 0)
                {
                    // 원본 점군 유지 — 후속 도구가 빈 점군으로 오동작하지 않게
                    result.Success = false;
                    result.Message = $"No point inside mask (coverage {coverage:F1}%). Point cloud unchanged.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                var outPositions = new Vector3[kept.Count];
                var outColors = hasColors
                    ? new System.Windows.Media.Color[kept.Count]
                    : Array.Empty<System.Windows.Media.Color>();
                for (int k = 0; k < kept.Count; k++)
                {
                    int i = kept[k];
                    outPositions[k] = positions[i];
                    if (hasColors)
                        outColors[k] = srcColors![i];
                }

                VisionService.Instance.CurrentPointCloud = new PointCloudData
                {
                    Name = $"{src.Name}_MaskCrop",
                    Positions = outPositions,
                    Colors = outColors,
                    PointCount = kept.Count,
                    Intrinsics = src.Intrinsics
                };

                result.OutputImage = inputImage.Clone(); // 마스크 pass-through
                result.Success = true;
                result.Message = $"Cropped {inputCount} → {kept.Count} points "
                    + $"(mask {maskW}x{maskH}, coverage {coverage:F1}%"
                    + $"{(InvertMask ? ", inverted" : "")}"
                    + $"{(DilatePixels > 0 ? $", dilate {DilatePixels}px" : "")})";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Mask crop failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        private Mat PrepareMask(Mat inputImage)
        {
            Mat gray = inputImage.Channels() > 1
                ? inputImage.CvtColor(ColorConversionCodes.BGR2GRAY)
                : inputImage.Clone();

            if (DilatePixels > 0)
            {
                int k = DilatePixels * 2 + 1;
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
                Cv2.Dilate(gray, gray, kernel);
            }

            if (!gray.IsContinuous())
            {
                var cont = gray.Clone();
                gray.Dispose();
                gray = cont;
            }
            return gray;
        }

        /// <summary>
        /// 점군 좌표(뎁스맵 픽셀) → 마스크 픽셀 배율.
        /// organized면 인접 점 간격으로 stride를 복원해 뎁스맵 해상도를 추정하고
        /// (실제 폭과 최대 stride-1 오차 — 서브픽셀 수준), unorganized면 동일 공간 가정(1:1).
        /// </summary>
        private static (float scaleX, float scaleY) ComputeMaskScale(PointCloudData src, int maskW, int maskH)
        {
            if (!src.IsOrganized || src.GridWidth <= 1 || src.GridHeight <= 1)
                return (1f, 1f);

            float strideX = Math.Max(1f, src.Positions[1].X - src.Positions[0].X);
            float strideY = Math.Max(1f, src.Positions[src.GridWidth].Y - src.Positions[0].Y);
            float srcW = src.GridWidth * strideX;
            float srcH = src.GridHeight * strideY;
            return (maskW / srcW, maskH / srcH);
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "InputPoints", "OutputPoints", "KeptRatio", "MaskCoveragePercent"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new PointCloudMaskCropTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                InvertMask = this.InvertMask,
                MinMaskValue = this.MinMaskValue,
                DilatePixels = this.DilatePixels,
                SkipInvalidZ = this.SkipInvalidZ
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
