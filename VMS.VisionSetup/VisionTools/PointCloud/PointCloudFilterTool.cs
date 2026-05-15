using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.VisionTools.PointCloud
{
    /// <summary>
    /// 3D 점군 필터링 도구 — VoxelGrid 다운샘플 + Statistical Outlier Removal.
    /// 입력: VisionService.CurrentPointCloud (3D 카메라 grab 결과 등)
    /// 출력: 필터링된 PointCloud → VisionService.CurrentPointCloud 갱신.
    /// 후속 도구(HeightSlicer, PlaneFit, Geometry3D)가 필터링된 데이터를 사용.
    /// 이미지 입력은 사용하지 않고 pass-through.
    /// </summary>
    public class PointCloudFilterTool : VisionToolBase
    {
        // ── VoxelGrid ──
        private bool _enableVoxelGrid = true;
        public bool EnableVoxelGrid
        {
            get => _enableVoxelGrid;
            set => SetProperty(ref _enableVoxelGrid, value);
        }

        private float _voxelSize = 1.0f;
        /// <summary>Voxel 한 변의 길이 (mm). 같은 voxel 안의 모든 점을 하나로 합침.</summary>
        public float VoxelSize
        {
            get => _voxelSize;
            set => SetProperty(ref _voxelSize, Math.Clamp(value, 0.01f, 100f));
        }

        // ── Statistical Outlier Removal ──
        private bool _enableSor;
        public bool EnableSor
        {
            get => _enableSor;
            set => SetProperty(ref _enableSor, value);
        }

        private int _sorK = 30;
        /// <summary>이웃 점 개수 (KNN). 각 점의 K개 이웃 평균 거리 통계로 outlier 판정.</summary>
        public int SorK
        {
            get => _sorK;
            set => SetProperty(ref _sorK, Math.Clamp(value, 5, 200));
        }

        private double _sorStddev = 2.0;
        /// <summary>표준편차 배수. 평균거리 ± 이 값 × σ 밖이면 outlier로 제거.</summary>
        public double SorStddev
        {
            get => _sorStddev;
            set => SetProperty(ref _sorStddev, Math.Clamp(value, 0.5, 10.0));
        }

        public PointCloudFilterTool()
        {
            Name = "PointCloud Filter";
            ToolType = "PointCloudFilterTool";
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

                int inputCount = src.PointCount;
                var current = src;
                bool anyApplied = false;

                if (EnableVoxelGrid && VoxelSize > 0)
                {
                    current = TransformUtils.VoxelGridFilter(current, VoxelSize);
                    anyApplied = true;
                }

                if (EnableSor && current.PointCount >= SorK + 1)
                {
                    current = TransformUtils.StatisticalOutlierRemoval(current, SorK, SorStddev);
                    anyApplied = true;
                }

                if (!anyApplied)
                {
                    result.Success = true;
                    result.Message = "No filter enabled — pass-through.";
                    result.OutputImage = inputImage.Clone();
                    result.Data["InputPoints"] = inputCount;
                    result.Data["OutputPoints"] = current.PointCount;
                    result.Data["ReductionRatio"] = 0.0;
                    return result;
                }

                int outputCount = current.PointCount;
                double reduction = inputCount > 0 ? 1.0 - (double)outputCount / inputCount : 0;

                // 필터링된 점군을 VisionService 슬롯에 적용 → 후속 3D 도구가 사용
                VisionService.Instance.CurrentPointCloud = current;

                result.Data["InputPoints"] = inputCount;
                result.Data["OutputPoints"] = outputCount;
                result.Data["ReductionRatio"] = reduction;
                result.Data["AppliedVoxel"] = EnableVoxelGrid;
                result.Data["AppliedSor"] = EnableSor;

                result.OutputImage = inputImage.Clone(); // 이미지는 pass-through
                result.Success = true;
                result.Message = $"Filtered {inputCount} → {outputCount} points ({reduction * 100:F1}% reduction)"
                    + $"{(EnableVoxelGrid ? $", Voxel={VoxelSize:F2}mm" : "")}"
                    + $"{(EnableSor ? $", SOR(K={SorK}, σ×{SorStddev:F1})" : "")}";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"PointCloud filter failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "InputPoints", "OutputPoints", "ReductionRatio",
                "AppliedVoxel", "AppliedSor"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new PointCloudFilterTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                EnableVoxelGrid = this.EnableVoxelGrid,
                VoxelSize = this.VoxelSize,
                EnableSor = this.EnableSor,
                SorK = this.SorK,
                SorStddev = this.SorStddev
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
