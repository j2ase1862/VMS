using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.VisionTools.PointCloud
{
    /// <summary>
    /// 점군 3D 직선 피팅 — 현재 점군(보통 Height Slicer → Mask Crop 으로 레일·모서리·봉만 남긴 것)에
    /// RANSAC 으로 직선을 찾고 인라이어 주성분(PCA)으로 다듬는다.
    /// 결과: 직선(점 + 단위 방향), 길이, 직진도(인라이어의 직선까지 거리 — 상위 1% 제외, 최대·RMS 병기).
    /// 3D Geometry 의 점-직선 거리(PointToLineDistance3D)에 직선을 공급한다 — 종전에는 직선을 내는 도구가
    /// 없어 그 연산을 쓸 수 없었다.
    /// 좌표는 점군 원좌표(3D 카메라 grab 점군은 X/Y 화소 · Z mm) — Plane Fit 과 같은 공간.
    /// 레시피 점군을 바꾸지 않는다(측정 전용).
    /// </summary>
    public class PointCloudLineFitTool : VisionToolBase
    {
        private float _distanceThreshold = 2f;
        /// <summary>RANSAC 인라이어 거리 (mm) — 직선에서 이 거리 안의 점을 "직선 위의 점" 으로 본다.</summary>
        public float DistanceThreshold
        {
            get => _distanceThreshold;
            set => SetProperty(ref _distanceThreshold, Math.Clamp(value, 0.01f, 100f));
        }

        private int _iterations = 300;
        /// <summary>RANSAC 후보 직선 시도 횟수.</summary>
        public int Iterations
        {
            get => _iterations;
            set => SetProperty(ref _iterations, Math.Clamp(value, 10, 10000));
        }

        private int _minInliers = 50;
        /// <summary>최소 인라이어 수 — 이보다 적으면 직선을 못 찾은 것으로 실패.</summary>
        public int MinInliers
        {
            get => _minInliers;
            set => SetProperty(ref _minInliers, Math.Max(2, value));
        }

        private bool _drawOverlay = true;
        /// <summary>찾은 직선을 2D 이미지 뷰어에 표시 (점군 X/Y 가 화소 좌표일 때).</summary>
        public bool DrawOverlay
        {
            get => _drawOverlay;
            set => SetProperty(ref _drawOverlay, value);
        }

        // ── 판정 — 직진도 ──

        private bool _enableJudgment;
        /// <summary>직진도 판정 사용 — Straightness(상위 1% 를 뺀 이탈) ≤ Max Straightness 면 합격.</summary>
        public bool EnableJudgment
        {
            get => _enableJudgment;
            set => SetProperty(ref _enableJudgment, value);
        }

        private double _maxStraightness = 1.0;
        /// <summary>직진도 상한 (mm) — 직선 위 점의 99% 가 직선에서 이 거리 안이어야 합격.</summary>
        public double MaxStraightness
        {
            get => _maxStraightness;
            set => SetProperty(ref _maxStraightness, Math.Max(0, value));
        }

        // RANSAC 점수 계산에 쓸 최대 점 수 — 수십만 점 레일도 일정 시간에 끝나게
        private const int MaxScoringPoints = 20_000;

        public PointCloudLineFitTool()
        {
            Name = "PointCloud Line Fit";
            ToolType = "PointCloudLineFitTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();
            try
            {
                var src = VisionService.Instance.CurrentPointCloud;
                var points = ValidPoints(src);
                if (points.Count < Math.Max(2, MinInliers))
                {
                    result.Success = false;
                    result.Message = src == null
                        ? "No point cloud available."
                        : $"직선 피팅 실패: 유효한 점 부족 ({points.Count}개 < 최소 {MinInliers}개)";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                var fit = Fit(points, DistanceThreshold, Iterations, MinInliers);
                if (fit == null)
                {
                    result.Success = false;
                    result.Message = $"직선 피팅 실패: 인라이어 {MinInliers}개 이상인 직선 없음 (임계 {DistanceThreshold:F2}mm)";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                var (p0, dir, inliers, length, maxDev, rmsDev, straightness, e1, e2) = fit.Value;
                result.Data["LinePointX"] = (double)p0.X;
                result.Data["LinePointY"] = (double)p0.Y;
                result.Data["LinePointZ"] = (double)p0.Z;
                result.Data["LineDirX"] = (double)dir.X;
                result.Data["LineDirY"] = (double)dir.Y;
                result.Data["LineDirZ"] = (double)dir.Z;
                result.Data["Length"] = length;
                // 직진도는 상위 1% 를 뺀 이탈(P99) — 3D 카메라의 튄 점 한두 개가 최대값을 정하지 않게
                result.Data["Straightness"] = straightness;
                result.Data["MaxDeviation"] = maxDev;
                result.Data["RmsDeviation"] = rmsDev;
                result.Data["InlierCount"] = inliers;
                result.Data["TotalPoints"] = points.Count;
                result.Data["InlierRatio"] = (double)inliers / points.Count;
                // XY 평면에서의 방향(도, −90~90) · XY 평면에 대한 기울기(도)
                result.Data["AngleXY"] = NormalizeAxisDeg(Math.Atan2(dir.Y, dir.X) * 180 / Math.PI);
                result.Data["Elevation"] = Math.Asin(Math.Clamp(dir.Z, -1f, 1f)) * 180 / Math.PI;
                result.Data["End1X"] = (double)e1.X; result.Data["End1Y"] = (double)e1.Y; result.Data["End1Z"] = (double)e1.Z;
                result.Data["End2X"] = (double)e2.X; result.Data["End2Y"] = (double)e2.Y; result.Data["End2Z"] = (double)e2.Z;

                result.Success = true;
                result.Message = $"직선: 길이 {length:F1}, 직진도 {straightness:F3}mm (최대 {maxDev:F3} · RMS {rmsDev:F3}), " +
                                 $"인라이어 {inliers}/{points.Count}";
                if (EnableJudgment)
                {
                    ToleranceJudgment.ApplyRange(result, straightness, 0, MaxStraightness, "mm", "직진도");
                    // 인라이어 거리가 상한 이하면 휜 부분이 "직선 밖 점"으로 빠져 직진도가 작게 나온다 — 조용히 OK 가 되지 않게
                    if (DistanceThreshold <= MaxStraightness)
                        result.Message += $" ⚠ Distance Threshold({DistanceThreshold:F1}) ≤ Max Straightness — 휜 부분이 직선 밖으로 빠져 " +
                                          $"직진도가 작게 나올 수 있음 (인라이어 {(double)inliers / points.Count:P0})";
                }

                result.OutputImage = inputImage.Clone();
                if (DrawOverlay)
                    result.OverlayImage = DrawLine(inputImage, e1, e2);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"직선 피팅 실패: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        private static List<Vector3> ValidPoints(PointCloudData? src)
        {
            var list = new List<Vector3>();
            if (src == null) return list;
            var positions = src.Positions;
            for (int i = 0; i < src.PointCount; i++)
            {
                var p = positions[i];
                // 측정 실패 화소(Z=0)는 점이 아니다 — 3D 카메라가 Z=0 으로 채운다
                if (p.Z != 0f && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z))
                    list.Add(p);
            }
            return list;
        }

        /// <summary>
        /// RANSAC(두 점 → 후보 직선, 인라이어 수 최대) → 인라이어 PCA 로 방향·중심 정련 → 끝점·직진도.
        /// 결정적 시드로 같은 입력이면 같은 결과.
        /// </summary>
        internal static (Vector3 Point, Vector3 Dir, int Inliers, double Length, double MaxDev, double RmsDev, double P99Dev, Vector3 End1, Vector3 End2)?
            Fit(IReadOnlyList<Vector3> points, float threshold, int iterations, int minInliers)
        {
            var rnd = new Random(12345);
            int n = points.Count;

            // 점수 계산용 표본 (점이 많으면 균등 간격 추출)
            IReadOnlyList<Vector3> scoring = points;
            if (n > MaxScoringPoints)
            {
                var sample = new List<Vector3>(MaxScoringPoints);
                double step = (double)n / MaxScoringPoints;
                for (int i = 0; i < MaxScoringPoints; i++) sample.Add(points[(int)(i * step)]);
                scoring = sample;
            }

            float thrSq = threshold * threshold;
            int bestCount = -1;
            Vector3 bestA = default, bestDir = default;
            for (int it = 0; it < iterations; it++)
            {
                var a = scoring[rnd.Next(scoring.Count)];
                var b = scoring[rnd.Next(scoring.Count)];
                var d = b - a;
                float len = d.Length();
                if (len < 1e-3f) continue;
                d /= len;
                int count = 0;
                foreach (var p in scoring)
                    if (DistSqToLine(p, a, d) <= thrSq) count++;
                if (count > bestCount) { bestCount = count; bestA = a; bestDir = d; }
            }
            if (bestCount < 0) return null;

            // 전체 점에서 인라이어 수집 → PCA 정련 (1회 재수집으로 정련된 직선 기준 인라이어 확정)
            var inliers = Collect(points, bestA, bestDir, thrSq);
            if (inliers.Count < minInliers) return null;
            var (center, dir) = Pca(inliers);
            inliers = Collect(points, center, dir, thrSq);
            if (inliers.Count < minInliers) return null;
            (center, dir) = Pca(inliers);

            // 방향 부호 고정 — +X 쪽(같으면 +Y)을 향하게 해 실행마다 뒤집히지 않도록
            if (dir.X < 0 || (Math.Abs(dir.X) < 1e-6f && dir.Y < 0)) dir = -dir;

            double tMin = double.MaxValue, tMax = double.MinValue, sumSq = 0;
            var devs = new double[inliers.Count];
            for (int i = 0; i < inliers.Count; i++)
            {
                var p = inliers[i];
                double t = Vector3.Dot(p - center, dir);
                tMin = Math.Min(tMin, t); tMax = Math.Max(tMax, t);
                double dsq = DistSqToLine(p, center, dir);
                devs[i] = Math.Sqrt(dsq);
                sumSq += dsq;
            }
            Array.Sort(devs);
            var e1 = center + dir * (float)tMin;
            var e2 = center + dir * (float)tMax;
            return (center, dir, inliers.Count, tMax - tMin, devs[^1], Math.Sqrt(sumSq / inliers.Count),
                    devs[Math.Max(0, (int)Math.Ceiling(devs.Length * 0.99) - 1)], e1, e2);
        }

        private static List<Vector3> Collect(IReadOnlyList<Vector3> points, Vector3 a, Vector3 d, float thrSq)
        {
            var list = new List<Vector3>();
            foreach (var p in points)
                if (DistSqToLine(p, a, d) <= thrSq) list.Add(p);
            return list;
        }

        private static float DistSqToLine(Vector3 p, Vector3 a, Vector3 unitDir)
        {
            var ap = p - a;
            float t = Vector3.Dot(ap, unitDir);
            // |ap|² − t² 는 직선 위 점에서 부동소수 오차로 음수가 될 수 있다 — √ 가 NaN 이 되지 않게
            return Math.Max(0f, ap.LengthSquared() - t * t);
        }

        /// <summary>점들의 중심과 주성분 방향(공분산 최대 고유벡터 — 거듭제곱법).</summary>
        private static (Vector3 Center, Vector3 Dir) Pca(List<Vector3> pts)
        {
            var c = Vector3.Zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;

            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts)
            {
                double dx = p.X - c.X, dy = p.Y - c.Y, dz = p.Z - c.Z;
                xx += dx * dx; xy += dx * dy; xz += dx * dz; yy += dy * dy; yz += dy * dz; zz += dz * dz;
            }

            // 초기 벡터: 대각 성분이 가장 큰 축 (0 벡터 수렴 방지)
            double vx = 1, vy = 0, vz = 0;
            if (yy >= xx && yy >= zz) { vx = 0; vy = 1; }
            else if (zz >= xx && zz >= yy) { vx = 0; vz = 1; }
            for (int i = 0; i < 100; i++)
            {
                double nx = xx * vx + xy * vy + xz * vz;
                double ny = xy * vx + yy * vy + yz * vz;
                double nz = xz * vx + yz * vy + zz * vz;
                double norm = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (norm < 1e-12) break;
                nx /= norm; ny /= norm; nz /= norm;
                bool converged = Math.Abs(nx - vx) + Math.Abs(ny - vy) + Math.Abs(nz - vz) < 1e-10;
                vx = nx; vy = ny; vz = nz;
                if (converged) break;
            }
            return (c, Vector3.Normalize(new Vector3((float)vx, (float)vy, (float)vz)));
        }

        private static double NormalizeAxisDeg(double deg)
        {
            while (deg > 90) deg -= 180;
            while (deg <= -90) deg += 180;
            return deg;
        }

        /// <summary>끝점 두 개를 2D 오버레이에 선으로 — 점군 X/Y 가 이미지 밖이면 그리지 않는다(mm 점군 등).</summary>
        private Mat? DrawLine(Mat inputImage, Vector3 e1, Vector3 e2)
        {
            bool Inside(Vector3 p) => p.X >= 0 && p.Y >= 0 && p.X < inputImage.Width && p.Y < inputImage.Height;
            if (!Inside(e1) && !Inside(e2)) return null;

            var overlay = GetColorOverlayBase(inputImage);
            var a = new Point((int)Math.Round(e1.X), (int)Math.Round(e1.Y));
            var b = new Point((int)Math.Round(e2.X), (int)Math.Round(e2.Y));
            int thick = Math.Max(2, inputImage.Width / 500);
            Cv2.Line(overlay, a, b, new Scalar(0, 255, 255), thick, LineTypes.AntiAlias);
            Cv2.Circle(overlay, a, thick * 3, new Scalar(0, 200, 255), -1, LineTypes.AntiAlias);
            Cv2.Circle(overlay, b, thick * 3, new Scalar(0, 200, 255), -1, LineTypes.AntiAlias);
            return overlay;
        }

        public override List<string> GetAvailableResultKeys() => new()
        {
            "Success", "LinePointX", "LinePointY", "LinePointZ", "LineDirX", "LineDirY", "LineDirZ",
            "Length", "Straightness", "MaxDeviation", "RmsDeviation", "InlierCount", "TotalPoints", "InlierRatio",
            "AngleXY", "Elevation", "End1X", "End1Y", "End1Z", "End2X", "End2Y", "End2Z",
            // 판정 (EnableJudgment 시)
            "JudgmentValue", "JudgmentLow", "JudgmentHigh", "JudgmentPass"
        };

        public override VisionToolBase Clone()
        {
            var clone = new PointCloudLineFitTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                DistanceThreshold = this.DistanceThreshold,
                Iterations = this.Iterations,
                MinInliers = this.MinInliers,
                DrawOverlay = this.DrawOverlay,
                EnableJudgment = this.EnableJudgment,
                MaxStraightness = this.MaxStraightness
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
