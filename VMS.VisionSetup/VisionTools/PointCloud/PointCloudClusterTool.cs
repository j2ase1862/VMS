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

        public enum DimensionScaleMode
        {
            /// <summary>XyScale(mm/px) 수동 입력값 사용.</summary>
            Manual,
            /// <summary>카메라 depth intrinsics + 클러스터 실측 Z로 mm/px 자동 계산 (Z/fx).</summary>
            AutoFromCamera
        }

        private DimensionScaleMode _scaleMode = DimensionScaleMode.Manual;
        /// <summary>
        /// 치수 환산 방식. AutoFromCamera는 grab 점군에 실린 depth intrinsics로
        /// 클러스터별 mm/px = Z/fx 를 자동 계산 — 작동 거리가 바뀌어도 정확.
        /// intrinsics가 없으면(.vpc 로드 등) XyScale로 폴백.
        /// </summary>
        public DimensionScaleMode ScaleMode
        {
            get => _scaleMode;
            set => SetProperty(ref _scaleMode, value);
        }

        private bool _drawOverlay = true;
        /// <summary>2D 이미지 뷰어에 클러스터 결과(점·외접 사각형·라벨) 오버레이 표시.</summary>
        public bool DrawOverlay
        {
            get => _drawOverlay;
            set => SetProperty(ref _drawOverlay, value);
        }

        private float _xyScale = 1.0f;
        /// <summary>
        /// X/Y 치수 환산 배율 (Manual 모드). Mech-Mind organized 점군은 X/Y가 뎁스맵 픽셀 인덱스이므로
        /// (Z만 mm) 실측 mm 치수가 필요하면 mm/pixel 값을 입력. 1.0 = 원 단위 그대로.
        /// SizeX/SizeY/Length/Width에만 적용 (CenterX/Y는 기존 호환을 위해 원 단위 유지).
        /// </summary>
        public float XyScale
        {
            get => _xyScale;
            set => SetProperty(ref _xyScale, Math.Clamp(value, 0.0001f, 1000f));
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
                bool autoScaleUnavailable = false;

                Mat? overlay = null;
                float imgScaleX = 1f, imgScaleY = 1f;
                if (DrawOverlay)
                {
                    overlay = GetColorOverlayBase(inputImage);
                    (imgScaleX, imgScaleY) = ComputeImageScale(src, overlay.Width, overlay.Height);
                }

                for (int i = 0; i < reported; i++)
                {
                    var c = clusters[i];
                    var center = ComputeCentroid(c);
                    result.Data[$"Cluster{i}_Points"] = c.PointCount;
                    result.Data[$"Cluster{i}_CenterX"] = center.X;
                    result.Data[$"Cluster{i}_CenterY"] = center.Y;
                    result.Data[$"Cluster{i}_CenterZ"] = center.Z;

                    var (scaleX, scaleY, isAuto) = ResolveXyScale(src, center.Z);
                    if (ScaleMode == DimensionScaleMode.AutoFromCamera && !isAuto)
                        autoScaleUnavailable = true;

                    var m = ComputeDimensions(c, scaleX, scaleY);
                    result.Data[$"Cluster{i}_SizeX"] = m.SizeX;
                    result.Data[$"Cluster{i}_SizeY"] = m.SizeY;
                    result.Data[$"Cluster{i}_SizeZ"] = m.SizeZ;
                    result.Data[$"Cluster{i}_Length"] = m.Length;
                    result.Data[$"Cluster{i}_Width"] = m.Width;
                    result.Data[$"Cluster{i}_Angle"] = m.Angle;
                    result.Data[$"Cluster{i}_MmPerPx"] = (scaleX + scaleY) * 0.5f;
                    totalClustered += c.PointCount;

                    if (overlay != null)
                    {
                        bool dimsInMm = isAuto || XyScale != 1.0f;
                        DrawClusterOnOverlay(overlay, c, i, imgScaleX, imgScaleY,
                            m.Length, m.Width, dimsInMm);
                    }
                }
                result.Data["TotalClusteredPoints"] = totalClustered;

                if (overlay != null)
                    result.OverlayImage = overlay;
                result.OutputImage = inputImage.Clone();
                result.Success = true;
                result.Message = $"Found {clusters.Count} cluster(s), largest={clusters[0].PointCount} pts, mode={OutputMode}"
                    + (autoScaleUnavailable
                        ? " ⚠ Auto scale unavailable (no camera intrinsics) — using manual XyScale"
                        : "");
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

        /// <summary>
        /// 클러스터에 적용할 X/Y mm/px 배율.
        /// AutoFromCamera: 핀홀 모델 — 깊이 Z에서 1px의 실제 폭 = Z/fx (가로), Z/fy (세로).
        /// 클러스터 중심 Z를 대표 깊이로 사용 (평면 위 부품 가정).
        /// </summary>
        private (float scaleX, float scaleY, bool isAuto) ResolveXyScale(PointCloudData src, float clusterZ)
        {
            if (ScaleMode == DimensionScaleMode.AutoFromCamera)
            {
                var intr = src.Intrinsics;
                if (intr != null && intr.IsValid && clusterZ > 0)
                    return ((float)(clusterZ / intr.Fx), (float)(clusterZ / intr.Fy), true);
            }
            return (XyScale, XyScale, false);
        }

        // 클러스터별 오버레이 색상 (BGR) — 어두운 배경/점군 이미지에서 잘 보이는 고채도 계열
        private static readonly Scalar[] ClusterColors =
        {
            new(0, 255, 255),   // Yellow
            new(0, 255, 0),     // Green
            new(255, 128, 0),   // Blue-ish
            new(255, 0, 255),   // Magenta
            new(0, 128, 255),   // Orange
            new(255, 255, 0),   // Cyan
            new(128, 128, 255), // Light red
            new(0, 0, 255)      // Red
        };

        /// <summary>
        /// 점군 X/Y 좌표(뎁스맵 픽셀) → 표시 이미지 픽셀 배율.
        /// organized면 인접 점 간격(stride)으로 뎁스맵 해상도를 복원해 비율을 맞추고
        /// (PointCloudMaskCropTool.ComputeMaskScale과 동일 규약), unorganized면 동일 픽셀 공간 가정(1:1).
        /// </summary>
        private static (float scaleX, float scaleY) ComputeImageScale(PointCloudData src, int imgW, int imgH)
        {
            if (!src.IsOrganized || src.GridWidth <= 1 || src.GridHeight <= 1)
                return (1f, 1f);

            float strideX = Math.Max(1f, src.Positions[1].X - src.Positions[0].X);
            float strideY = Math.Max(1f, src.Positions[src.GridWidth].Y - src.Positions[0].Y);
            float srcW = src.GridWidth * strideX;
            float srcH = src.GridHeight * strideY;
            return (imgW / srcW, imgH / srcH);
        }

        /// <summary>
        /// 클러스터 하나를 2D 오버레이에 표시: 점(데시메이션) + 회전 외접 사각형 + 중심 십자 + 라벨.
        /// 점군이 픽셀 좌표계가 아니라(변환/mm 점군 등) 투영 점이 전부 이미지 밖이면 그리지 않음.
        /// </summary>
        private static void DrawClusterOnOverlay(Mat overlay, PointCloudData c, int index,
            float scaleX, float scaleY, float length, float width, bool dimsInMm)
        {
            int n = c.PointCount;
            if (n == 0) return;

            // 점 표시는 클러스터당 최대 ~20k개로 데시메이션 (표시 비용 억제, OBB 정밀도 충분)
            int step = Math.Max(1, n / 20_000);
            var projected = new List<OpenCvSharp.Point2f>(Math.Min(n, 20_000) + 1);
            int insideCount = 0;
            for (int i = 0; i < n; i += step)
            {
                var p = c.Positions[i];
                float x = p.X * scaleX, y = p.Y * scaleY;
                projected.Add(new OpenCvSharp.Point2f(x, y));
                if (x >= 0 && x < overlay.Width && y >= 0 && y < overlay.Height)
                    insideCount++;
            }
            if (insideCount == 0) return;

            var color = ClusterColors[index % ClusterColors.Length];

            foreach (var pt in projected)
            {
                int px = (int)pt.X, py = (int)pt.Y;
                if (px >= 0 && px < overlay.Width && py >= 0 && py < overlay.Height)
                    Cv2.Circle(overlay, px, py, 1, color, -1);
            }

            // 회전 외접 사각형 (이미지 픽셀 공간의 OBB)
            var obb = Cv2.MinAreaRect(projected);
            var corners = new OpenCvSharp.Point[4];
            var cornerPts = obb.Points();
            for (int i = 0; i < 4; i++)
                corners[i] = new OpenCvSharp.Point((int)cornerPts[i].X, (int)cornerPts[i].Y);
            Cv2.Polylines(overlay, new[] { corners }, true, color, 2);

            var centerPt = new OpenCvSharp.Point((int)obb.Center.X, (int)obb.Center.Y);
            Cv2.DrawMarker(overlay, centerPt, new Scalar(0, 0, 255), MarkerTypes.Cross, 12, 2);

            string unit = dimsInMm ? "mm" : "px";
            string label = $"#{index} {n}pt {length:F1}x{width:F1}{unit}";
            // Min/Max 조합 — 이미지가 라벨보다 작아도 (min > max) 예외 없이 동작
            var labelPos = new OpenCvSharp.Point(
                Math.Max(0, Math.Min(centerPt.X + 8, overlay.Width - 200)),
                Math.Max(14, Math.Min(centerPt.Y - 8, overlay.Height - 4)));
            // 검은 외곽선 + 흰 글자 — 배경 무관 가독성
            Cv2.PutText(overlay, label, labelPos, HersheyFonts.HersheySimplex, 0.5, Scalar.Black, 3);
            Cv2.PutText(overlay, label, labelPos, HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1);
        }

        /// <summary>
        /// 클러스터 치수: 축 정렬 크기(SizeX/Y/Z) + XY 평면 최소 외접 사각형(OBB) 길이/폭/각도.
        /// OBB는 회전 놓인 부품의 실제 길이·폭 측정용. 배율은 X/Y에만 적용 (Z는 원래 mm),
        /// OBB Length/Width는 X/Y 배율 평균 사용 (fx≈fy인 일반 카메라에서 오차 미미).
        /// </summary>
        private static (float SizeX, float SizeY, float SizeZ, float Length, float Width, float Angle)
            ComputeDimensions(PointCloudData c, float xyScaleX, float xyScaleY)
        {
            int n = c.PointCount;
            if (n == 0) return (0, 0, 0, 0, 0, 0);

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            var pts2f = new Point2f[n];
            for (int i = 0; i < n; i++)
            {
                var p = c.Positions[i];
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
                pts2f[i] = new Point2f(p.X, p.Y);
            }

            float obbScale = (xyScaleX + xyScaleY) * 0.5f;
            var obb = Cv2.MinAreaRect(pts2f);
            float len = Math.Max(obb.Size.Width, obb.Size.Height) * obbScale;
            float wid = Math.Min(obb.Size.Width, obb.Size.Height) * obbScale;
            // MinAreaRect 각도는 Size.Width 축 기준 — 긴 변 기준 각도로 정규화
            float ang = obb.Size.Width >= obb.Size.Height ? obb.Angle : obb.Angle + 90f;
            if (ang > 90f) ang -= 180f;
            if (ang < -90f) ang += 180f;

            return ((maxX - minX) * xyScaleX, (maxY - minY) * xyScaleY, maxZ - minZ, len, wid, ang);
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
                keys.Add($"Cluster{i}_SizeX");
                keys.Add($"Cluster{i}_SizeY");
                keys.Add($"Cluster{i}_SizeZ");
                keys.Add($"Cluster{i}_Length");
                keys.Add($"Cluster{i}_Width");
                keys.Add($"Cluster{i}_Angle");
                keys.Add($"Cluster{i}_MmPerPx");
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
                OutputMode = this.OutputMode,
                ScaleMode = this.ScaleMode,
                XyScale = this.XyScale,
                DrawOverlay = this.DrawOverlay
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
