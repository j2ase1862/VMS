using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace VMS.VisionSetup.VisionTools.Measurement
{
    /// <summary>
    /// 피팅 방법
    /// </summary>
    public enum PlaneFitMethod
    {
        LeastSquares,
        RANSAC
    }

    /// <summary>
    /// 3D 평면 피팅 도구 (Phase 5)
    /// ROI 영역 내 3D 포인트에서 평면 방정식 추출 (RANSAC / 최소자승법)
    /// HeightMapMetadata의 PixelTo3D를 활용하여 2D ROI → 3D 포인트 복원
    /// </summary>
    public class PlaneFitTool : VisionToolBase
    {
        private PlaneFitMethod _fitMethod = PlaneFitMethod.RANSAC;
        public PlaneFitMethod FitMethod
        {
            get => _fitMethod;
            set => SetProperty(ref _fitMethod, value);
        }

        /// <summary>RANSAC 반복 횟수</summary>
        private int _ransacIterations = 1000;
        public int RansacIterations
        {
            get => _ransacIterations;
            set => SetProperty(ref _ransacIterations, Math.Max(10, value));
        }

        /// <summary>RANSAC 인라이어 임계값 (mm)</summary>
        private double _ransacThreshold = 0.5;
        public double RansacThreshold
        {
            get => _ransacThreshold;
            set => SetProperty(ref _ransacThreshold, Math.Max(0.01, value));
        }

        /// <summary>포인트 서브샘플링 간격 (1=전체, 2=1/4, 4=1/16)</summary>
        private int _sampleStride = 2;
        public int SampleStride
        {
            get => _sampleStride;
            set => SetProperty(ref _sampleStride, Math.Max(1, value));
        }

        public PlaneFitTool()
        {
            Name = "Plane Fit";
            ToolType = "PlaneFitTool";
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                // HeightMapMetadata에서 3D 포인트 복원
                var metadata = VisionService.Instance.CurrentHeightMapMetadata;
                if (metadata == null || metadata.PixelTo3D.Length == 0)
                {
                    result.Success = false;
                    result.Message = "3D 데이터 없음: HeightMapMetadata가 필요합니다";
                    sw.Stop();
                    ExecutionTime = sw.Elapsed.TotalMilliseconds;
                    LastResult = result;
                    return result;
                }

                // ROI 영역에서 3D 포인트 수집
                var roi = UseROI ? GetAdjustedROI(inputImage) : new Rect(0, 0, inputImage.Width, inputImage.Height);
                var points3D = Collect3DPoints(metadata, roi);

                if (points3D.Count < 3)
                {
                    result.Success = false;
                    result.Message = $"평면 피팅 실패: 유효한 3D 포인트 부족 ({points3D.Count}개)";
                    sw.Stop();
                    ExecutionTime = sw.Elapsed.TotalMilliseconds;
                    LastResult = result;
                    return result;
                }

                // 평면 피팅 실행
                double a, b, c, d;
                int inlierCount;

                if (FitMethod == PlaneFitMethod.RANSAC)
                {
                    (a, b, c, d, inlierCount) = FitPlaneRANSAC(points3D);
                }
                else
                {
                    (a, b, c, d) = FitPlaneLeastSquares(points3D);
                    inlierCount = points3D.Count;
                }

                // 법선 벡터 정규화
                double norm = Math.Sqrt(a * a + b * b + c * c);
                if (norm > 1e-10) { a /= norm; b /= norm; c /= norm; d /= norm; }

                // 평면도 (Flatness) 계산: 모든 점의 평면까지 거리의 최대-최소 차이
                double maxDist = double.MinValue;
                double minDist = double.MaxValue;
                double sumDist = 0;

                foreach (var pt in points3D)
                {
                    double dist = a * pt.X + b * pt.Y + c * pt.Z + d;
                    maxDist = Math.Max(maxDist, dist);
                    minDist = Math.Min(minDist, dist);
                    sumDist += Math.Abs(dist);
                }

                double flatness = maxDist - minDist;
                double avgError = sumDist / points3D.Count;

                // 결과 저장
                result.Success = true;
                result.Data["PlaneA"] = a;
                result.Data["PlaneB"] = b;
                result.Data["PlaneC"] = c;
                result.Data["PlaneD"] = d;
                result.Data["NormalX"] = a;
                result.Data["NormalY"] = b;
                result.Data["NormalZ"] = c;
                result.Data["Flatness"] = flatness;
                result.Data["AvgError"] = avgError;
                result.Data["InlierCount"] = inlierCount;
                result.Data["TotalPoints"] = points3D.Count;
                result.Message = $"평면: {a:F4}x + {b:F4}y + {c:F4}z + {d:F2} = 0, " +
                                 $"평면도: {flatness:F3}mm, 인라이어: {inlierCount}/{points3D.Count}";

                // 오버레이 생성
                result.OverlayImage = CreateOverlay(inputImage, metadata, roi, a, b, c, d);

                // 그래픽 오버레이
                result.Graphics.Add(new GraphicOverlay
                {
                    Type = GraphicType.Rectangle,
                    Position = new Point2d(roi.X, roi.Y),
                    Width = roi.Width,
                    Height = roi.Height,
                    Color = new Scalar(0, 200, 255),
                    Thickness = 2
                });
                result.Graphics.Add(new GraphicOverlay
                {
                    Type = GraphicType.Text,
                    Position = new Point2d(roi.X, roi.Y - 10),
                    Text = $"Flatness: {flatness:F3}mm",
                    Color = new Scalar(0, 200, 255)
                });
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"평면 피팅 실패: {ex.Message}";
            }

            sw.Stop();
            ExecutionTime = sw.Elapsed.TotalMilliseconds;
            LastResult = result;
            return result;
        }

        /// <summary>
        /// HeightMapMetadata에서 ROI 영역의 유효한 3D 포인트 수집
        /// </summary>
        private List<Vector3> Collect3DPoints(HeightMapMetadata metadata, Rect roi)
        {
            var points = new List<Vector3>();

            int startX = Math.Max(0, roi.X);
            int startY = Math.Max(0, roi.Y);
            int endX = Math.Min(metadata.Width, roi.X + roi.Width);
            int endY = Math.Min(metadata.Height, roi.Y + roi.Height);

            for (int y = startY; y < endY; y += SampleStride)
            {
                for (int x = startX; x < endX; x += SampleStride)
                {
                    var pt = metadata.GetPoint3D(x, y);
                    if (pt.HasValue && !float.IsNaN(pt.Value.X) && !float.IsNaN(pt.Value.Z))
                    {
                        points.Add(pt.Value);
                    }
                }
            }

            return points;
        }

        /// <summary>
        /// RANSAC 평면 피팅
        /// </summary>
        private (double a, double b, double c, double d, int inlierCount) FitPlaneRANSAC(List<Vector3> points)
        {
            var random = new Random(42);
            int n = points.Count;
            double bestA = 0, bestB = 0, bestC = 1, bestD = 0;
            int bestInliers = 0;

            for (int iter = 0; iter < RansacIterations; iter++)
            {
                // 3점 랜덤 선택
                int i1 = random.Next(n);
                int i2 = random.Next(n);
                int i3 = random.Next(n);
                if (i1 == i2 || i2 == i3 || i1 == i3) continue;

                var p1 = points[i1];
                var p2 = points[i2];
                var p3 = points[i3];

                // 법선 벡터 = (p2-p1) × (p3-p1)
                var v1 = p2 - p1;
                var v2 = p3 - p1;
                var normal = Vector3.Cross(v1, v2);

                float len = normal.Length();
                if (len < 1e-10f) continue;

                normal /= len;
                float d = -Vector3.Dot(normal, p1);

                // 인라이어 수 카운트
                int inliers = 0;
                float threshold = (float)RansacThreshold;

                for (int i = 0; i < n; i++)
                {
                    float dist = Math.Abs(Vector3.Dot(normal, points[i]) + d);
                    if (dist <= threshold)
                        inliers++;
                }

                if (inliers > bestInliers)
                {
                    bestInliers = inliers;
                    bestA = normal.X;
                    bestB = normal.Y;
                    bestC = normal.Z;
                    bestD = d;
                }
            }

            // 인라이어로 최소자승법 리파인
            if (bestInliers > 3)
            {
                var inlierPoints = new List<Vector3>();
                float threshold = (float)RansacThreshold;
                var bestNormal = new Vector3((float)bestA, (float)bestB, (float)bestC);

                foreach (var pt in points)
                {
                    if (Math.Abs(Vector3.Dot(bestNormal, pt) + (float)bestD) <= threshold)
                        inlierPoints.Add(pt);
                }

                if (inlierPoints.Count >= 3)
                {
                    var (ra, rb, rc, rd) = FitPlaneLeastSquares(inlierPoints);
                    return (ra, rb, rc, rd, bestInliers);
                }
            }

            return (bestA, bestB, bestC, bestD, bestInliers);
        }

        /// <summary>
        /// 최소자승법 평면 피팅: z = ax + by + c → 법선 (a, b, -1) 정규화
        /// </summary>
        private static (double a, double b, double c, double d) FitPlaneLeastSquares(List<Vector3> points)
        {
            int n = points.Count;

            // 중심점 계산
            double cx = 0, cy = 0, cz = 0;
            foreach (var p in points) { cx += p.X; cy += p.Y; cz += p.Z; }
            cx /= n; cy /= n; cz /= n;

            // 공분산 행렬 (3x3)
            double xx = 0, xy = 0, xz = 0;
            double yy = 0, yz = 0, zz = 0;

            foreach (var p in points)
            {
                double dx = p.X - cx;
                double dy = p.Y - cy;
                double dz = p.Z - cz;
                xx += dx * dx; xy += dx * dy; xz += dx * dz;
                yy += dy * dy; yz += dy * dz;
                zz += dz * dz;
            }

            // 최소 고유값에 대응하는 고유벡터 = 법선 벡터
            // 3x3 대칭 행렬의 고유값 분해 (Power iteration for smallest eigenvalue)
            // det(A - λI) = 0 → 특성 다항식의 근

            // 간이 방법: SVD 대신 직접 계산
            double a11 = xx, a12 = xy, a13 = xz;
            double a22 = yy, a23 = yz;
            double a33 = zz;

            // 최소 고유값의 고유벡터를 inverse power iteration으로 구함
            double nx = 0, ny = 0, nz = 1; // 초기값

            for (int iter = 0; iter < 50; iter++)
            {
                // A * v
                double vx = a11 * nx + a12 * ny + a13 * nz;
                double vy = a12 * nx + a22 * ny + a23 * nz;
                double vz = a13 * nx + a23 * ny + a33 * nz;

                // trace(A) - 최대 고유값 근사를 위한 shift
                double shift = a11 + a22 + a33;

                // (shift*I - A) * v → 최소 고유값의 고유벡터로 수렴
                double sx = shift * nx - vx;
                double sy = shift * ny - vy;
                double sz = shift * nz - vz;

                double len = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                if (len < 1e-15) break;

                nx = sx / len;
                ny = sy / len;
                nz = sz / len;
            }

            double d = -(nx * cx + ny * cy + nz * cz);
            return (nx, ny, nz, d);
        }

        /// <summary>
        /// 오버레이: 평면까지 거리를 컬러맵으로 시각화
        /// </summary>
        private static Mat? CreateOverlay(Mat inputImage, HeightMapMetadata metadata, Rect roi,
            double a, double b, double c, double d)
        {
            try
            {
                var overlay = new Mat(inputImage.Size(), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));

                int startX = Math.Max(0, roi.X);
                int startY = Math.Max(0, roi.Y);
                int endX = Math.Min(metadata.Width, roi.X + roi.Width);
                int endY = Math.Min(metadata.Height, roi.Y + roi.Height);

                for (int y = startY; y < endY; y++)
                {
                    for (int x = startX; x < endX; x++)
                    {
                        var pt = metadata.GetPoint3D(x, y);
                        if (!pt.HasValue || float.IsNaN(pt.Value.Z)) continue;

                        double dist = a * pt.Value.X + b * pt.Value.Y + c * pt.Value.Z + d;
                        double absDist = Math.Abs(dist);

                        // 거리에 따라 색상 (0mm=초록, 0.5mm=노랑, 1mm+=빨강)
                        byte r, g, bv;
                        if (absDist < 0.5)
                        {
                            double t = absDist / 0.5;
                            r = (byte)(255 * t);
                            g = 255;
                            bv = 0;
                        }
                        else
                        {
                            double t = Math.Min(1.0, (absDist - 0.5) / 0.5);
                            r = 255;
                            g = (byte)(255 * (1 - t));
                            bv = 0;
                        }

                        overlay.Set(y, x, new Vec4b(bv, g, r, 128));
                    }
                }

                return overlay;
            }
            catch
            {
                return null;
            }
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "PlaneA", "PlaneB", "PlaneC", "PlaneD",
                "NormalX", "NormalY", "NormalZ",
                "Flatness", "AvgError", "InlierCount", "TotalPoints"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new PlaneFitTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                FitMethod = this.FitMethod,
                RansacIterations = this.RansacIterations,
                RansacThreshold = this.RansacThreshold,
                SampleStride = this.SampleStride,
                IsEnabled = this.IsEnabled,
                UseROI = this.UseROI,
                ROI = this.ROI
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
