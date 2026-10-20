using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 현장 Mech-Mind 프레임 점군으로 Registration 의 Coarse(PCA) 초기 맞춤을 켰을 때와 껐을 때를 비교한다.
    /// 2026-09-28 재현에서 Coarse ON 이 평면 내 180° 뒤집힘(rotZ ±160~175°)을 낸 것을 확인 · 수정 검증용.
    /// 점군 준비는 레시피 1-2 와 같다: 프레임 높이대 → 가장 큰 클러스터 → 복셀 4mm.
    /// 환경 의존(D:\3D Image · D:\3D Image-1) — CI 는 *Diagnostic* 필터로 제외.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class RegistrationCoarseFieldDiagnosticTests
    {
        private static readonly string[] DataDirs = { @"D:\3D Image", @"D:\3D Image-1" };
        private const float PartZMin = 1780f, PartZMax = 1845f, ClusterTol = 45f, Voxel = 4f;

        private readonly ITestOutputHelper _out;
        public RegistrationCoarseFieldDiagnosticTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Diagnostic_Registration_CoarseOnVsOff_OnFieldFrames()
        {
            var swOff = new System.Diagnostics.Stopwatch(); var swNew = new System.Diagnostics.Stopwatch();
            foreach (var dir in DataDirs.Where(Directory.Exists))
            {
                var refPath = Path.Combine(dir, "Recipe", "frame_reference.vpc");
                if (!File.Exists(refPath)) { _out.WriteLine($"기준 점군 없음 — 스킵: {refPath}"); continue; }
                using var reference = PointCloudData.LoadFromFile(refPath);
                _out.WriteLine($"== {dir}  기준 {reference.PointCount}점");

                foreach (var file in Directory.GetFiles(dir, "*.vpc").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var src = Prepare(file);
                    if (src == null) { _out.WriteLine($"  {Path.GetFileName(file),-34} 프레임 없음"); continue; }
                    using (src)
                    {
                        swOff.Start(); var off = Run(reference, src, coarse: false); swOff.Stop();
                        var on = Run(reference, src, coarse: true);
                        swNew.Start(); var neu = RunNew(reference, src, out var cands); swNew.Stop();
                        _out.WriteLine($"  {Path.GetFileName(file),-34} n={src.PointCount,5} │ OFF {off.rotZ,7:F1}° {off.conf,4:P0} │ PCA {on.rotZ,7:F1}° {on.conf,4:P0} │ NEW {neu.rotZ,7:F1}° {neu.conf,4:P0}"
                                       + (Math.Abs(Wrap(neu.rotZ - off.rotZ)) > 20 ? "   ← NEW≠OFF" : "")
                                       + "  [" + string.Join(" ", cands.Select(c =>
                                           $"{(c.Chosen ? "*" : "")}{c.Name}:{c.QuickConfidence:P0}{(c.FinalConfidence is { } f ? $"→{f:P0}" : "")}/{c.RotationDeg:F0}°")) + "]");
                    }
                }
            }
            _out.WriteLine($"합계 — Coarse OFF {swOff.ElapsedMilliseconds} ms · 새 Coarse {swNew.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// 다운샘플하지 않은 원본 규모(프레임 높이대 전체 점)로 정합 시간을 잰다 — ICP 최근접 탐색 비용 확인용.
        /// </summary>
        [Fact]
        public void Diagnostic_Registration_Timing_OnRawFrameBand()
        {
            var dir = DataDirs.FirstOrDefault(Directory.Exists);
            var refPath = dir == null ? null : Path.Combine(dir, "Recipe", "frame_reference.vpc");
            if (refPath == null || !File.Exists(refPath)) { _out.WriteLine("데이터 없음 — 스킵"); return; }
            foreach (var name in new[] { "Mech-Mind 3D Scan-2.vpc", "Mech-Mind 3D Scan-6.vpc" })
            {
                var file = Path.Combine(dir!, name);
                if (!File.Exists(file)) continue;
                using var refRaw = PrepareRaw(Path.Combine(dir!, "Mech-Mind 3D Scan-2.vpc"));
                using var src = PrepareRaw(file);
                if (refRaw == null || src == null) continue;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var t = TransformUtils.ICP(refRaw, src, 50, 2f, out _);
                long icpMs = sw.ElapsedMilliseconds; sw.Restart();
                var t2 = TransformUtils.RegisterWithCoarse(refRaw, src, 50, 2f, 3f, out _, out _);
                long coarseMs = sw.ElapsedMilliseconds;
                _out.WriteLine($"{name}: 기준 {refRaw.PointCount}점 · 소스 {src.PointCount}점 │ ICP 단독 {icpMs} ms (rotZ {TransformUtils.ToEulerAnglesDegrees(t).Z:F1}°) │ Coarse+ICP {coarseMs} ms (rotZ {TransformUtils.ToEulerAnglesDegrees(t2).Z:F1}°)");
            }
        }

        private static PointCloudData? PrepareRaw(string file)
        {
            var voxelled = Prepare(file, voxel: false);
            return voxelled;
        }

        private static (float rotZ, float conf) Run(PointCloudData reference, PointCloudData src, bool coarse)
        {
            var c = Matrix4x4.Identity;
            PointCloudData input = src;
            PointCloudData? copy = null;
            if (coarse)
            {
                c = TransformUtils.CoarseAlignPCA(reference, src);
                copy = TransformUtils.TransformPointCloud(src, c);
                input = copy;
            }
            try
            {
                var icp = TransformUtils.ICP(reference, input, 50, 2f, out _);
                var t = c * icp;
                return (TransformUtils.ToEulerAnglesDegrees(t).Z, TransformUtils.ComputeInlierRatio(reference, src, t, 3f));
            }
            finally { copy?.Dispose(); }
        }

        private static (float rotZ, float conf) RunNew(PointCloudData reference, PointCloudData src, out TransformUtils.CoarseCandidate[] cands)
        {
            var t = TransformUtils.RegisterWithCoarse(reference, src, 50, 2f, 3f, out _, out cands);
            return (TransformUtils.ToEulerAnglesDegrees(t).Z, TransformUtils.ComputeInlierRatio(reference, src, t, 3f));
        }

        /// <summary>레시피 1-2 와 같은 준비 — 높이대 → 가장 큰 클러스터 → 복셀.</summary>
        private static PointCloudData? Prepare(string file, bool voxel = true)
        {
            using var raw = PointCloudData.LoadFromFile(file);
            var band = raw.Positions.Take(raw.PointCount)
                .Where(p => p.Z >= PartZMin && p.Z <= PartZMax && float.IsFinite(p.X)).ToArray();
            if (band.Length < 2000) return null;

            var service = VisionService.Instance;
            service.CurrentPointCloud = PointCloudData.FromArrays(band.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(), null, "band");
            using var dummy = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(0));
            var cluster = new PointCloudClusterTool
            {
                Tolerance = ClusterTol, MinPoints = 2000, OutputMode = PointCloudClusterTool.ClusterOutputMode.LargestOnly,
            }.Execute(dummy);
            if (!cluster.Success || service.CurrentPointCloud == null) return null;
            var largest = service.CurrentPointCloud;
            service.CurrentPointCloud = null;
            return voxel ? TransformUtils.VoxelGridFilter(largest, Voxel) : largest;
        }

        private static float Wrap(float deg) => ((deg + 540f) % 360f) - 180f;
    }
}
