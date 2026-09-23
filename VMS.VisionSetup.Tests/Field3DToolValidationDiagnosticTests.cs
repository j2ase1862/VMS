using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 현장 Mech-Mind 촬영본(.vpc 10장, 2026-09-23)으로 3D 도구 7종을 앱과 같은 VisionService.ExecuteAll
    /// 경로로 헤드리스 실행해 수치를 남기는 진단 테스트. 환경 의존(D:\3D Image) — CI 는 *Diagnostic* 필터로 제외.
    ///
    /// 장면: 휜 패널(카메라 약 1.89m) 위 금속 프레임 1개(윗면이 패널보다 약 89mm 가까움), 매 장 위치·각도만 다름.
    /// 좌표: 촬영 버전이 v1.42.0 이전이라 내부 파라미터 없음 → X/Y = 화소, Z = mm.
    /// 독립 기준값(파이썬, VMS 미사용): 프레임 윗면 높이 p50 86~92mm, 프레임 윗면 Z 1788~1834mm.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class Field3DToolValidationDiagnosticTests
    {
        private const string DataDir = @"D:\3D Image";
        private const float PartZMin = 1780f;   // 프레임 윗면 밴드 (패널은 대부분 ≥1855mm)
        private const float PartZMax = 1845f;
        // 금속 프레임은 점 누락으로 레일이 끊겨 기본 Tolerance(5)로는 2~4조각 — 환경변수로 비교 실행
        private static readonly int RefIndex = int.TryParse(Environment.GetEnvironmentVariable("VMS_FIELD3D_REF"), out var ri) ? ri : 1;
        private static readonly bool Coarse = Environment.GetEnvironmentVariable("VMS_FIELD3D_COARSE") != "0";
        private static readonly float ClusterTol = float.TryParse(Environment.GetEnvironmentVariable("VMS_FIELD3D_TOL"), out var t) ? t : 5f;

        private readonly ITestOutputHelper _out;
        public Field3DToolValidationDiagnosticTests(ITestOutputHelper output) => _out = output;

        private static string[] Files() => Directory.Exists(DataDir)
            ? Directory.GetFiles(DataDir, "*.vpc")
                .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('-').Last()))
                .ToArray()
            : Array.Empty<string>();

        [Fact]
        public void Diagnostic_Field3D_PerScanTools()
        {
            var files = Files();
            if (files.Length == 0) { _out.WriteLine("데이터 없음 — 스킵"); return; }

            foreach (var file in files)
            {
                using var cloud = PointCloudData.LoadFromFile(file);
                var name = Path.GetFileNameWithoutExtension(file);
                _out.WriteLine($"════ {name}: {cloud.PointCount:N0}pt grid {cloud.GridWidth}x{cloud.GridHeight} " +
                               $"organized={cloud.IsOrganized} intrinsics={(cloud.Intrinsics != null ? "O" : "X")}");
                try
                {
                    RunFilter(cloud);
                    var part = RunSegmentation(cloud);
                    if (part != null) RunPlaneAndHeight(cloud, part.Value);
                }
                finally
                {
                    VisionService.Instance.ClearTools();
                    VisionService.Instance.CurrentPointCloud = null;
                }
            }
        }

        [Fact]
        public void Diagnostic_Field3D_RegistrationAndDeviation()
        {
            var files = Files();
            if (files.Length < 2) { _out.WriteLine("데이터 없음 — 스킵"); return; }

            // 기준 = 1번 스캔의 프레임 점군 (골든 샘플 방식)
            var refPath = Path.Combine(Path.GetTempPath(), "vms_field3d_ref_scan1.vpc");
            using (var cloud1 = PointCloudData.LoadFromFile(files[RefIndex - 1]))
            {
                var partCloud = ExtractPartCloud(cloud1);
                Assert.NotNull(partCloud);
                partCloud!.SaveToFile(refPath);
                _out.WriteLine($"[Tolerance={ClusterTol} ref=scan{RefIndex} coarse={Coarse}] 기준 점군 저장: {partCloud.PointCount:N0}pt → {refPath}");
            }

            foreach (var file in files)
            {
                using var cloud = PointCloudData.LoadFromFile(file);
                var service = VisionService.Instance;
                try
                {
                    PrepareHeightMap(cloud, PartZMin, PartZMax);
                    var (slicer, crop, cluster) = AddSegmentationChain(PointCloudClusterTool.ClusterOutputMode.LargestOnly);
                    var reg = new PointCloudRegistrationTool
                    {
                        Name = "Registration", ReferencePath = refPath, EnableCoarseAlignment = Coarse,
                        ApplyTransformToSource = true, MaxIterations = 50, ConfidenceDistanceMm = 3f,
                    };
                    var dev = new PointCloudDeviationTool
                    {
                        Name = "Deviation", ReferencePath = refPath, ToleranceMm = 3f, MaxDefectRatioPercent = 5f,
                        OutputMode = PointCloudDeviationTool.DeviationOutputMode.KeepOriginal,
                    };
                    service.AddTool(reg); service.AddTool(dev);
                    service.AddConnection(cluster, reg, ConnectionType.Result);
                    service.AddConnection(reg, dev, ConnectionType.Result);

                    var sw = Stopwatch.StartNew();
                    var results = service.ExecuteAll();
                    sw.Stop();
                    var r = ById(reg); var d = ById(dev);
                    _out.WriteLine($"{Path.GetFileNameWithoutExtension(file),-22} " +
                        $"REG {(r.Success ? "OK" : "NG")} conv={Get(r, "Converged")} it={Get(r, "Iterations")} " +
                        $"rotZ={D(r, "RotationZ"):F1}° rotX={D(r, "RotationX"):F1}° rotY={D(r, "RotationY"):F1}° " +
                        $"T=({D(r, "TranslationX"):F0},{D(r, "TranslationY"):F0},{D(r, "TranslationZ"):F1}) " +
                        $"meanErr={D(r, "MeanError"):F2} conf={D(r, "Confidence"):F1}% coarse={Get(r, "CoarseApplied")} | " +
                        $"DEV {(d.Success ? "OK" : "NG")} mean={D(d, "MeanDeviation"):F2} max={D(d, "MaxDeviation"):F1} " +
                        $"defect={D(d, "DefectRatio"):F2}% ({Get(d, "DefectPoints")}/{Get(d, "CheckedPoints")})  [{sw.ElapsedMilliseconds}ms]");
                    if (!r.Success) _out.WriteLine($"    REG msg: {r.Message}");
                    if (!d.Success) _out.WriteLine($"    DEV msg: {d.Message}");
                }
                finally
                {
                    service.ClearTools();
                    service.CurrentPointCloud = null;
                }
            }
        }

        // ── 단계별 ──

        private void RunFilter(PointCloudData cloud)
        {
            var service = VisionService.Instance;
            service.ClearTools();
            PrepareHeightMap(cloud, PartZMin, PartZMax);
            var filter = new PointCloudFilterTool { Name = "Filter", EnableVoxelGrid = true, VoxelSize = 4f, EnableSor = false };
            service.AddTool(filter);
            var sw = Stopwatch.StartNew();
            service.ExecuteAll();
            var r = ById(filter);
            _out.WriteLine($"  FILTER(voxel 4) {(r.Success ? "OK" : "NG")} {Get(r, "InputPoints")}→{Get(r, "OutputPoints")} " +
                           $"({D(r, "ReductionRatio"):F1}%) [{sw.ElapsedMilliseconds}ms] {Short(r.Message)}");
            service.ClearTools();
            service.CurrentPointCloud = cloud;
        }

        /// <summary>HeightSlicer → MaskCrop → Cluster(KeepOriginal) — 프레임 검출. 가장 큰 클러스터 중심·크기 반환.</summary>
        private (double cx, double cy, double cz, double sx, double sy)? RunSegmentation(PointCloudData cloud)
        {
            var service = VisionService.Instance;
            service.ClearTools();
            PrepareHeightMap(cloud, PartZMin, PartZMax);
            var (slicer, crop, cluster) = AddSegmentationChain(PointCloudClusterTool.ClusterOutputMode.KeepOriginal);
            var sw = Stopwatch.StartNew();
            service.ExecuteAll();
            sw.Stop();
            var s = ById(slicer); var c = ById(crop); var k = ById(cluster);
            _out.WriteLine($"  SLICER {(s.Success ? "OK" : "NG")} | CROP {(c.Success ? "OK" : "NG")} " +
                           $"{Get(c, "InputPoints")}→{Get(c, "OutputPoints")} cov={D(c, "MaskCoveragePercent"):F2}% | " +
                           $"CLUSTER {(k.Success ? "OK" : "NG")} n={Get(k, "ClusterCount")} largest={Get(k, "LargestPoints")} [{sw.ElapsedMilliseconds}ms]");
            if (!k.Success) { _out.WriteLine($"    msg: {k.Message}"); return null; }
            for (int i = 0; i < Math.Min(3, Convert.ToInt32(k.Data["ClusterCount"])); i++)
            {
                _out.WriteLine($"    C{i}: {Get(k, $"Cluster{i}_Points")}pt  size(px,px,mm)=({D(k, $"Cluster{i}_SizeX"):F0},{D(k, $"Cluster{i}_SizeY"):F0},{D(k, $"Cluster{i}_SizeZ"):F1})  " +
                               $"L×W={D(k, $"Cluster{i}_Length"):F0}×{D(k, $"Cluster{i}_Width"):F0}  ang={D(k, $"Cluster{i}_Angle"):F1}°  " +
                               $"center=({D(k, $"Cluster{i}_CenterX"):F0},{D(k, $"Cluster{i}_CenterY"):F0},{D(k, $"Cluster{i}_CenterZ"):F1})");
            }
            var result = (D(k, "Cluster0_CenterX"), D(k, "Cluster0_CenterY"), D(k, "Cluster0_CenterZ"),
                          D(k, "Cluster0_SizeX"), D(k, "Cluster0_SizeY"));
            service.ClearTools();
            service.CurrentPointCloud = cloud;
            return result;
        }

        /// <summary>PlaneFit(패널, 프레임 주변 ROI) + PlaneFit(프레임 윗면) + Geometry3D(프레임 중심 ↔ 패널 평면).</summary>
        private void RunPlaneAndHeight(PointCloudData cloud, (double cx, double cy, double cz, double sx, double sy) part)
        {
            var service = VisionService.Instance;
            service.ClearTools();
            // 높이맵 밴드는 패널+프레임 전체 — PlaneFit 은 PixelTo3D 를 쓰므로 밴드와 무관하게 ROI 안 점을 모은다
            PrepareHeightMap(cloud, 1700f, 2000f);

            int half = (int)Math.Max(part.sx, part.sy) / 2 + 150;
            var panelRoi = new Rect((int)part.cx - half, (int)part.cy - half, half * 2, half * 2);
            var partRoi = new Rect((int)part.cx - 20, (int)part.cy - 20, 40, 40);

            var panel = new PlaneFitTool { Name = "Panel Plane", FitMethod = PlaneFitMethod.RANSAC, RansacThreshold = 3.0, RansacIterations = 300, SampleStride = 2, UseROI = true, ROI = panelRoi };
            var panelLs = new PlaneFitTool { Name = "Panel Plane LS", FitMethod = PlaneFitMethod.LeastSquares, SampleStride = 2, UseROI = true, ROI = panelRoi };
            var top = new PlaneFitTool { Name = "Frame Top", FitMethod = PlaneFitMethod.RANSAC, RansacThreshold = 3.0, RansacIterations = 300, SampleStride = 1, UseROI = true, ROI = partRoi };
            var cluster = new PointCloudClusterTool { Name = "Cluster", MinPoints = 2000, OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal };
            var geo = new Geometry3DTool { Name = "Height", Operation = Geometry3DOperation.PointToPlaneDistance, UseManualPoints = false };

            // 클러스터는 프레임 밴드로 잘라야 하므로 별도 실행 없이 여기선 PlaneFit 만 먼저
            service.AddTool(panel); service.AddTool(panelLs); service.AddTool(top);
            service.ExecuteAll();
            foreach (var t in new[] { panel, panelLs, top })
            {
                var r = ById(t);
                _out.WriteLine($"  PLANE[{t.Name}] {(r.Success ? "OK" : "NG")} n=({D(r, "NormalX"):F4},{D(r, "NormalY"):F4},{D(r, "NormalZ"):F4}) " +
                               $"d={D(r, "PlaneD"):F1} flat={D(r, "Flatness"):F2} avgErr={D(r, "AvgError"):F2} " +
                               $"inliers={Get(r, "InlierCount")}/{Get(r, "TotalPoints")}  {(r.Success ? "" : Short(r.Message))}");
            }
            // 프레임 중심 Z 에서 패널 평면까지 — 평면식을 직접 풀어 도구와 대조
            var pr = ById(panel);
            if (pr.Success)
            {
                double a = D(pr, "PlaneA"), b = D(pr, "PlaneB"), c = D(pr, "PlaneC"), d = D(pr, "PlaneD");
                double zPlane = -(a * part.cx + b * part.cy + d) / c;
                _out.WriteLine($"  → 패널평면 Z@프레임중심 = {zPlane:F1}mm, 프레임 중심 Z = {part.cz:F1}mm, ΔZ = {zPlane - part.cz:F1}mm");
            }

            // Geometry3D: 패널 평면(PlaneFit) ↔ 프레임 중심(Cluster) — 클러스터는 프레임 밴드 높이맵에서
            service.ClearTools();
            service.CurrentPointCloud = cloud;
            PrepareHeightMap(cloud, PartZMin, PartZMax);
            var (slicer, crop, cl) = AddSegmentationChain(PointCloudClusterTool.ClusterOutputMode.KeepOriginal);
            var panel2 = new PlaneFitTool { Name = "Panel Plane", FitMethod = PlaneFitMethod.RANSAC, RansacThreshold = 3.0, RansacIterations = 300, SampleStride = 2, UseROI = true, ROI = panelRoi };
            service.AddTool(panel2); service.AddTool(geo);
            service.AddConnection(panel2, geo, ConnectionType.Result);
            service.AddConnection(cl, geo, ConnectionType.Result);
            service.ExecuteAll();
            var p2 = ById(panel2); var g = ById(geo);
            _out.WriteLine($"  PLANE(프레임 밴드 높이맵) {(p2.Success ? "OK" : "NG")} flat={D(p2, "Flatness"):F2} inliers={Get(p2, "InlierCount")}/{Get(p2, "TotalPoints")}");
            _out.WriteLine($"  GEO3D point→plane {(g.Success ? "OK" : "NG")} dist={D(g, "Distance3D"):F2} signed={D(g, "SignedDistance"):F2}  {(g.Success ? "" : Short(g.Message))}");
        }

        // ── 공용 ──

        private PointCloudData? ExtractPartCloud(PointCloudData cloud)
        {
            var service = VisionService.Instance;
            service.ClearTools();
            PrepareHeightMap(cloud, PartZMin, PartZMax);
            AddSegmentationChain(PointCloudClusterTool.ClusterOutputMode.LargestOnly);
            var results = service.ExecuteAll();
            var partCloud = service.CurrentPointCloud;
            service.ClearTools();
            return results.All(r => r.Success) ? partCloud : null;
        }

        private static void PrepareHeightMap(PointCloudData cloud, float zMin, float zMax)
        {
            var service = VisionService.Instance;
            var (hm8, _, _) = service.GenerateHeightMap(cloud, 0f, zMin, zMax);
            service.SetImage(hm8);
            hm8.Dispose();
            service.CurrentPointCloud = cloud;
        }

        private static (HeightSlicerTool, PointCloudMaskCropTool, PointCloudClusterTool) AddSegmentationChain(
            PointCloudClusterTool.ClusterOutputMode mode)
        {
            var service = VisionService.Instance;
            var slicer = new HeightSlicerTool { Name = "Height Slicer", MinZ = PartZMin, MaxZ = PartZMax };
            var crop = new PointCloudMaskCropTool { Name = "Mask Crop" };
            var cluster = new PointCloudClusterTool { Name = "Cluster", MinPoints = 2000, Tolerance = ClusterTol, OutputMode = mode };
            service.AddTool(slicer); service.AddTool(crop); service.AddTool(cluster);
            service.AddConnection(slicer, crop, ConnectionType.Image);
            service.AddConnection(crop, cluster, ConnectionType.Result);
            return (slicer, crop, cluster);
        }

        private static VisionResult ById(VisionToolBase tool)
            => VisionService.Instance.LastExecutionResultsById.TryGetValue(tool.Id, out var r)
                ? r : new VisionResult { Success = false, Message = "실행 결과 없음" };

        private static string Get(VisionResult r, string key)
            => r.Data != null && r.Data.TryGetValue(key, out var v) ? Convert.ToString(v) ?? "" : "-";

        private static double D(VisionResult r, string key)
            => r.Data != null && r.Data.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v) : double.NaN;

        private static string Short(string? s) => s == null ? "" : (s.Length > 140 ? s[..140] + "…" : s);
    }
}
