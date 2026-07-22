using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VMS.Camera.Converters;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.VisionTools.PointCloud
{
    /// <summary>
    /// 3D 편차 분석 도구 (CAD Compare 계열) — Reference 점군 대비 현재 점군의
    /// 점별 최근접 거리(편차)를 계산해 히트맵으로 시각화하고, 허용 오차를 벗어난
    /// 불량 점을 집계/추출한다. 정합(Registration) 후 사용 전제.
    /// 결과 처리 모드:
    ///   • ColorizeAll: 전체 점을 편차 히트맵(녹→황→적)으로 색칠해 교체
    ///   • DefectsOnly: tolerance 초과 점만 남김 (후속 Cluster 로 불량 영역 분석)
    ///   • KeepOriginal: 점군 유지, 메트릭만 반환
    /// </summary>
    public class PointCloudDeviationTool : VisionToolBase
    {
        public enum DeviationOutputMode
        {
            /// <summary>전체 점을 편차 히트맵으로 색칠해 CurrentPointCloud 교체.</summary>
            ColorizeAll,
            /// <summary>tolerance 초과 점만 남김 (히트맵 색 포함).</summary>
            DefectsOnly,
            /// <summary>CurrentPointCloud 유지, 메트릭만 반환.</summary>
            KeepOriginal
        }

        // ── Reference 점군 (.vpc 파일 경로) — Registration 과 동일 패턴 ──
        private string _referencePath = string.Empty;
        public string ReferencePath
        {
            get => _referencePath;
            set => SetProperty(ref _referencePath, value ?? string.Empty);
        }

        private float _toleranceMm = 0.5f;
        /// <summary>허용 편차 (mm). 이보다 먼 점은 불량(defect)으로 집계.</summary>
        public float ToleranceMm
        {
            get => _toleranceMm;
            set => SetProperty(ref _toleranceMm, Math.Clamp(value, 0.01f, 100f));
        }

        private float _heatmapRangeMm = 1.0f;
        /// <summary>히트맵 색 스케일 상한 (mm). 0=녹색 → 상한 이상=적색.</summary>
        public float HeatmapRangeMm
        {
            get => _heatmapRangeMm;
            set => SetProperty(ref _heatmapRangeMm, Math.Clamp(value, 0.05f, 100f));
        }

        private float _maxDefectRatioPercent = 0.5f;
        /// <summary>합격 판정 기준 — 불량 점 비율(%)이 이 값 이하이면 OK.</summary>
        public float MaxDefectRatioPercent
        {
            get => _maxDefectRatioPercent;
            set => SetProperty(ref _maxDefectRatioPercent, Math.Clamp(value, 0f, 100f));
        }

        private DeviationOutputMode _outputMode = DeviationOutputMode.ColorizeAll;
        public DeviationOutputMode OutputMode
        {
            get => _outputMode;
            set => SetProperty(ref _outputMode, value);
        }

        public bool IsReferenceLoaded => !string.IsNullOrEmpty(ReferencePath) && File.Exists(ReferencePath);

        public PointCloudDeviationTool()
        {
            Name = "PointCloud Deviation";
            ToolType = "PointCloudDeviationTool";
        }

        /// <summary>현재 VisionService.CurrentPointCloud 를 Reference 로 저장.</summary>
        public bool SaveCurrentAsReference(string filePath)
        {
            var src = VisionService.Instance.CurrentPointCloud;
            if (src == null || src.PointCount == 0) return false;
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                src.SaveToFile(filePath);
                ReferencePath = filePath;
                OnPropertyChanged(nameof(IsReferenceLoaded));
                return true;
            }
            catch
            {
                return false;
            }
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
                    result.Message = "No source point cloud available.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                if (!IsReferenceLoaded)
                {
                    result.Success = false;
                    result.Message = "Reference (.vpc) not loaded. Use 'Save Current as Reference' first.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                PointCloudData reference;
                try
                {
                    // .vpc(양품 스캔) 또는 .stl(CAD 표면 샘플링) — 확장자 자동 분기
                    reference = StlMeshLoader.LoadReferenceCloud(ReferencePath);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Failed to load reference: {ex.Message}";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                if (reference.PointCount == 0)
                {
                    result.Success = false;
                    result.Message = "Reference point cloud is empty.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                // 편차 계산 — 탐색 반경 밖 거리는 반경값으로 클램프 (tolerance 초과 확정이므로 충분)
                float searchRadius = Math.Max(ToleranceMm, HeatmapRangeMm) * 2f;
                var distances = TransformUtils.ComputeNearestDistances(reference, src, searchRadius);

                int count = src.PointCount;
                double sum = 0;
                float max = 0;
                int defects = 0;
                for (int i = 0; i < count; i++)
                {
                    float d = distances[i];
                    sum += d;
                    if (d > max) max = d;
                    if (d > ToleranceMm) defects++;
                }
                float meanDeviation = count > 0 ? (float)(sum / count) : 0f;
                float defectRatio = count > 0 ? (float)defects / count : 0f;

                // 출력 모드
                switch (OutputMode)
                {
                    case DeviationOutputMode.ColorizeAll:
                        VisionService.Instance.CurrentPointCloud = BuildHeatmapCloud(src, distances, null);
                        break;
                    case DeviationOutputMode.DefectsOnly:
                        VisionService.Instance.CurrentPointCloud =
                            BuildHeatmapCloud(src, distances, d => d > ToleranceMm);
                        break;
                    case DeviationOutputMode.KeepOriginal:
                        break;
                }

                bool pass = defectRatio * 100f <= MaxDefectRatioPercent;

                result.Data["MeanDeviation"] = (double)meanDeviation;
                result.Data["MaxDeviation"] = (double)max;
                result.Data["DefectPoints"] = defects;
                result.Data["DefectRatio"] = (double)defectRatio;
                result.Data["CheckedPoints"] = count;

                result.OutputImage = inputImage.Clone();
                result.Success = pass;
                result.Message = $"Deviation mean {meanDeviation:F3}mm, max {max:F3}mm"
                    + $"{(max >= searchRadius ? "+" : "")}, "
                    + $"defects {defects}/{count} ({defectRatio:P2}) → {(pass ? "OK" : "NG")}";

                reference.Dispose();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Deviation analysis failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        /// <summary>
        /// 편차 히트맵 점군 생성 — filter 가 null 이면 전체, 아니면 조건 통과 점만.
        /// 색: 0mm=녹 → HeatmapRange/2=황 → HeatmapRange 이상=적.
        /// </summary>
        private PointCloudData BuildHeatmapCloud(PointCloudData src, float[] distances,
            Func<float, bool>? filter)
        {
            int count = src.PointCount;
            var indices = new List<int>(filter == null ? count : Math.Min(count, 1024));
            for (int i = 0; i < count; i++)
            {
                if (filter == null || filter(distances[i]))
                    indices.Add(i);
            }

            var cloud = new PointCloudData
            {
                Name = src.Name + (filter == null ? "_deviation" : "_defects"),
                Positions = new System.Numerics.Vector3[indices.Count],
                Colors = new System.Windows.Media.Color[indices.Count],
                // 부분 추출 시 organized 구조는 깨짐
                GridWidth = filter == null ? src.GridWidth : 0,
                GridHeight = filter == null ? src.GridHeight : 0,
            };
            cloud.PointCount = indices.Count;

            for (int i = 0; i < indices.Count; i++)
            {
                int srcIdx = indices[i];
                cloud.Positions[i] = src.Positions[srcIdx];
                cloud.Colors[i] = DeviationToColor(distances[srcIdx], HeatmapRangeMm);
            }
            return cloud;
        }

        /// <summary>편차(mm) → 히트맵 색. 0=녹(0,180,0) → 절반=황(230,200,0) → 상한 이상=적(220,0,0).</summary>
        public static System.Windows.Media.Color DeviationToColor(float deviationMm, float rangeMm)
        {
            float t = Math.Clamp(deviationMm / Math.Max(rangeMm, 1e-3f), 0f, 1f);
            byte r, g, b = 0;
            if (t < 0.5f)
            {
                float u = t / 0.5f;      // 녹 → 황
                r = (byte)(0 + u * 230);
                g = (byte)(180 + u * 20);
            }
            else
            {
                float u = (t - 0.5f) / 0.5f;  // 황 → 적
                r = (byte)(230 - u * 10);
                g = (byte)(200 - u * 200);
            }
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "MeanDeviation", "MaxDeviation",
                "DefectPoints", "DefectRatio", "CheckedPoints"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new PointCloudDeviationTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ReferencePath = this.ReferencePath,
                ToleranceMm = this.ToleranceMm,
                HeatmapRangeMm = this.HeatmapRangeMm,
                MaxDefectRatioPercent = this.MaxDefectRatioPercent,
                OutputMode = this.OutputMode
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
