using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using VMS.Camera.Models;
using VMS.Camera.Utils;
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
    /// 도구 ROI(UseROI)를 켜면 ROI 밖 픽셀은 마스크에서 제외 — 원본 2D 이미지를 그대로
    /// 연결하고 ROI만 그려도 그 영역의 점군만 남길 수 있다 (마스크 도구 없이 상자 크롭).
    ///
    /// 좌표 매핑: organized 점군은 X/Y에 뎁스맵 픽셀 좌표가 저장되므로
    /// (MechMindCameraAcquisition — X=col·stride, Y=row·stride) 인접 점에서 stride를 복원해
    /// 마스크 해상도와 뎁스맵 해상도가 달라도 비율로 맞춘다.
    /// unorganized 점군은 마스크와 같은 픽셀 공간이라고 가정한다(크롭/필터 이후 재적용 등).
    /// </summary>
    public class PointCloudMaskCropTool : VisionToolBase
    {
        public enum CropCombineMode
        {
            /// <summary>현재 점군에서 잘라 교체 (기본). 단일 크롭 또는 직렬 체인(점점 좁히기)용.</summary>
            Replace,
            /// <summary>
            /// Run 시작 시점 점군에서 잘라 현재 점군에 합침 — Mask Crop 여러 개를 나란히
            /// (각기 다른 ROI/마스크로) 쓸 때 두 번째부터 선택. Replace만 쓰면 앞 크롭이
            /// 점군을 이미 좁혀서 뒤 크롭의 영역에 점이 남지 않는다.
            /// </summary>
            Union
        }

        private CropCombineMode _combineMode = CropCombineMode.Replace;
        public CropCombineMode CombineMode
        {
            get => _combineMode;
            set => SetProperty(ref _combineMode, value);
        }

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
                var service = VisionService.Instance;
                var current = service.CurrentPointCloud;
                var src = current;
                bool unionWithCurrent = false;
                if (CombineMode == CropCombineMode.Union)
                {
                    // 병렬 브랜치: 앞 크롭이 CurrentPointCloud를 이미 좁혔어도
                    // 항상 Run 시작 시점 점군에서 자르고, 결과를 기존 점군에 합친다.
                    var runInput = service.PipelineInputPointCloud;
                    if (runInput != null && runInput.PointCount > 0 && !ReferenceEquals(runInput, current))
                    {
                        src = runInput;
                        unionWithCurrent = current != null && current.PointCount > 0;
                    }
                    // runInput == current(이 실행의 첫 점군 도구)면 Replace와 동일하게 동작
                }

                if (src == null || src.PointCount == 0)
                {
                    result.Success = false;
                    result.Message = "No point cloud available. Acquire from a 3D camera first.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                // 마스크 준비: 그레이 변환 → (옵션) 팽창 → ROI 적용 → 연속 버퍼 추출
                using var mask = PrepareMask(inputImage);
                mask.GetArray(out byte[] maskData);
                int maskW = mask.Width, maskH = mask.Height;
                double coverage = 100.0 * Cv2.CountNonZero(mask) / (maskW * (double)maskH);

                // 유효 마스크(임계값·팽창·ROI·반전 반영된 '남긴 영역') — 표시/후속 Image 연결용
                using var keptMask = new Mat();
                Cv2.Threshold(mask, keptMask, MinMaskValue - 1, 255, ThresholdTypes.Binary);
                if (InvertMask)
                    Cv2.BitwiseNot(keptMask, keptMask);

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
                    // 원본 점군 유지 — 후속 도구가 빈 점군으로 오동작하지 않게.
                    // OutputImage에 유효 마스크를 실어 '왜 0점인지'(마스크가 검게 비었는지) 눈으로 확인 가능.
                    result.Success = false;
                    result.Message = $"No point inside mask (coverage {coverage:F1}%). Point cloud unchanged."
                        + (CombineMode == CropCombineMode.Replace && !ReferenceEquals(src, service.PipelineInputPointCloud)
                            ? " Hint: 앞 Mask Crop이 점군을 이미 좁혔다면 CombineMode=Union을 사용하세요."
                            : "");
                    result.OutputImage = keptMask.Clone();
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

                var cropped = new PointCloudData
                {
                    Name = $"{src.Name}_MaskCrop",
                    Positions = outPositions,
                    Colors = outColors,
                    PointCount = kept.Count,
                    Intrinsics = src.Intrinsics
                };
                service.CurrentPointCloud = unionWithCurrent
                    ? TransformUtils.ConcatenateClouds(
                        new List<PointCloudData> { current!, cropped }, $"{src.Name}_MaskCrop")
                    : cropped;
                result.Data["MergedPoints"] = service.CurrentPointCloud.PointCount;

                // 출력 = 유효 마스크 (남긴 영역이 흰색) — Result Image에서 크롭 범위를 바로 확인,
                // 후속 Image 연결도 실제 사용된 마스크를 받는다.
                result.OutputImage = keptMask.Clone();
                result.OverlayImage = RenderOverlay(inputImage, keptMask);
                result.Success = true;
                result.Message = $"Cropped {inputCount} → {kept.Count} points "
                    + $"(mask {maskW}x{maskH}, coverage {coverage:F1}%"
                    + $"{(UseROI ? ", ROI" : "")}"
                    + $"{(InvertMask ? ", inverted" : "")}"
                    + $"{(DilatePixels > 0 ? $", dilate {DilatePixels}px" : "")}"
                    + $"{(unionWithCurrent ? $", union +{current!.PointCount} = {service.CurrentPointCloud.PointCount}" : "")})";
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

            ApplyRoiToMask(gray, inputImage);

            if (!gray.IsContinuous())
            {
                var cont = gray.Clone();
                gray.Dispose();
                gray = cont;
            }
            return gray;
        }

        /// <summary>
        /// 남긴 영역을 2D 뷰어에 표시: 초록 반투명 + 외곽선.
        /// 오버레이 베이스는 원본 컬러 이미지(GetColorOverlayBase) — 마스크 위가 아니라
        /// 실제 촬영 이미지 위에서 크롭 범위를 확인할 수 있다.
        /// </summary>
        private Mat RenderOverlay(Mat inputImage, Mat keptMask)
        {
            var ov = GetColorOverlayBase(inputImage);
            if (ov.Size() != keptMask.Size())
                return ov; // 크기 불일치(비정상) — 틴트 없이 베이스만

            using (var tint = new Mat(ov.Size(), ov.Type(), new Scalar(0, 200, 0)))
            using (var blended = new Mat())
            {
                Cv2.AddWeighted(ov, 0.6, tint, 0.4, 0, blended);
                blended.CopyTo(ov, keptMask);
            }

            Cv2.FindContours(keptMask, out var contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            Cv2.DrawContours(ov, contours, -1, new Scalar(0, 255, 0), 2);
            return ov;
        }

        /// <summary>
        /// 도구 ROI가 켜져 있으면 ROI 밖 픽셀을 0으로 — 사용자가 이미지 뷰어에서 그린
        /// 영역(사각형/원/다각형)만 마스크로 인정한다. ROI가 이미지와 안 겹치면 무시(no-op).
        /// Dilate 이후에 적용하므로 팽창된 마스크도 ROI를 벗어나지 않는다.
        /// </summary>
        private void ApplyRoiToMask(Mat gray, Mat inputImage)
        {
            if (!UseROI) return;

            using var shapeMask = GetNonRectangularShapeMask(inputImage);
            if (shapeMask != null && shapeMask.Size() == gray.Size())
            {
                using var outsideShape = new Mat();
                Cv2.BitwiseNot(shapeMask, outsideShape);
                gray.SetTo(0, outsideShape);
                return;
            }

            var roi = GetAdjustedROI(inputImage);
            if (roi.Width <= 0 || roi.Height <= 0) return;

            using var outside = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.White);
            using (var region = new Mat(outside, roi))
                region.SetTo(0);
            gray.SetTo(0, outside);
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
                "Success", "InputPoints", "OutputPoints", "KeptRatio", "MaskCoveragePercent", "MergedPoints"
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
                SkipInvalidZ = this.SkipInvalidZ,
                CombineMode = this.CombineMode
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
