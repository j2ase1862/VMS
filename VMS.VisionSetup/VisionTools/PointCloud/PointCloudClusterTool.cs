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
    /// 3D 점군 유클리드 클러스터링 도구 (Cognex 대응 도구 없음, PCL EuclideanClusterExtraction과 유사).
    /// 거리 tolerance 이내 점들을 BFS로 연결해 클러스터 형성.
    /// 분리된 객체 검출 / 노이즈 제거 / 컴포넌트 카운트 등에 사용.
    /// 결과 처리 모드:
    ///   • LargestOnly: VisionService.CurrentPointCloud를 가장 큰 클러스터로 교체 (객체 분리)
    ///   • AllMerged: 활성 클러스터들을 모두 합산 (작은 노이즈만 제거)
    ///   • KeepOriginal: CurrentPointCloud 유지, 결과는 메트릭만
    /// </summary>
    public class PointCloudClusterTool : VisionToolBase
    {
        public enum ClusterOutputMode
        {
            /// <summary>가장 큰 클러스터를 CurrentPointCloud로 교체.</summary>
            LargestOnly,
            /// <summary>min/max 통과 모든 클러스터를 합산.</summary>
            AllMerged,
            /// <summary>CurrentPointCloud 유지, 메트릭만 반환.</summary>
            KeepOriginal
        }

        private float _tolerance = 5.0f;
        /// <summary>동일 클러스터 판정 거리 (mm). 두 점이 이 거리 이내면 같은 클러스터.</summary>
        public float Tolerance
        {
            get => _tolerance;
            set => SetProperty(ref _tolerance, Math.Clamp(value, 0.1f, 100f));
        }

        private int _minPoints = 100;
        public int MinPoints
        {
            get => _minPoints;
            set => SetProperty(ref _minPoints, Math.Max(1, value));
        }

        private int _maxPoints = 1_000_000;
        public int MaxPoints
        {
            get => _maxPoints;
            set => SetProperty(ref _maxPoints, Math.Max(_minPoints, value));
        }

        private int _maxReportedClusters = 10;
        /// <summary>결과 Data에 노출할 상위 클러스터 수 (실제 모두 처리, 표시만 제한).</summary>
        public int MaxReportedClusters
        {
            get => _maxReportedClusters;
            set => SetProperty(ref _maxReportedClusters, Math.Clamp(value, 1, 100));
        }

        private ClusterOutputMode _outputMode = ClusterOutputMode.LargestOnly;
        public ClusterOutputMode OutputMode
        {
            get => _outputMode;
            set => SetProperty(ref _outputMode, value);
        }

        public PointCloudClusterTool()
        {
            Name = "PointCloud Cluster";
            ToolType = "PointCloudClusterTool";
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
                    result.Message = "No point cloud available.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                var clusters = TransformUtils.EuclideanClustering(src, Tolerance, MinPoints, MaxPoints);

                if (clusters.Count == 0)
                {
                    result.Success = false;
                    result.Message = $"No cluster found within size [{MinPoints}, {MaxPoints}] @ tol={Tolerance:F2}mm.";
                    result.OutputImage = inputImage.Clone();
                    result.Data["ClusterCount"] = 0;
                    return result;
                }

                // 결과 모드별 처리
                switch (OutputMode)
                {
                    case ClusterOutputMode.LargestOnly:
                        VisionService.Instance.CurrentPointCloud = clusters[0];
                        break;
                    case ClusterOutputMode.AllMerged:
                        VisionService.Instance.CurrentPointCloud =
                            TransformUtils.ConcatenateClouds(clusters, "ClustersMerged");
                        break;
                    case ClusterOutputMode.KeepOriginal:
                        // 변경 없음
                        break;
                }

                // 메트릭
                result.Data["ClusterCount"] = clusters.Count;
                result.Data["LargestPoints"] = clusters[0].PointCount;
                long totalClustered = 0;
                int reported = Math.Min(MaxReportedClusters, clusters.Count);
                for (int i = 0; i < reported; i++)
                {
                    var c = clusters[i];
                    var center = ComputeCentroid(c);
                    result.Data[$"Cluster{i}_Points"] = c.PointCount;
                    result.Data[$"Cluster{i}_CenterX"] = center.X;
                    result.Data[$"Cluster{i}_CenterY"] = center.Y;
                    result.Data[$"Cluster{i}_CenterZ"] = center.Z;
                    totalClustered += c.PointCount;
                }
                result.Data["TotalClusteredPoints"] = totalClustered;

                result.OutputImage = inputImage.Clone();
                result.Success = true;
                result.Message = $"Found {clusters.Count} cluster(s), largest={clusters[0].PointCount} pts, mode={OutputMode}";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Clustering failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        private static Vector3 ComputeCentroid(PointCloudData c)
        {
            int n = c.PointCount;
            if (n == 0) return Vector3.Zero;
            double sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < n; i++)
            {
                var p = c.Positions[i];
                sx += p.X; sy += p.Y; sz += p.Z;
            }
            return new Vector3((float)(sx / n), (float)(sy / n), (float)(sz / n));
        }

        public override List<string> GetAvailableResultKeys()
        {
            var keys = new List<string>
            {
                "Success", "ClusterCount", "LargestPoints", "TotalClusteredPoints"
            };
            int slots = Math.Max(MaxReportedClusters, 4);
            for (int i = 0; i < slots; i++)
            {
                keys.Add($"Cluster{i}_Points");
                keys.Add($"Cluster{i}_CenterX");
                keys.Add($"Cluster{i}_CenterY");
                keys.Add($"Cluster{i}_CenterZ");
            }
            return keys;
        }

        public override VisionToolBase Clone()
        {
            var clone = new PointCloudClusterTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                Tolerance = this.Tolerance,
                MinPoints = this.MinPoints,
                MaxPoints = this.MaxPoints,
                MaxReportedClusters = this.MaxReportedClusters,
                OutputMode = this.OutputMode
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
