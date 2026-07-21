using System;
using System.IO;
using System.Numerics;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// PointCloudDeviationTool 검증 (3D 편차 검사):
    /// - 그리드 해시 최근접 거리의 정확성 (반경 내 정확 / 반경 밖 클램프)
    /// - 동일 점군 → 불량 0, OK. 국소 돌출 → 해당 점만 불량 집계, NG
    /// - DefectsOnly 출력이 불량 점만 남기는지 (후속 Cluster 연계 전제)
    /// - 히트맵 색 매핑 (0=녹 / 상한=적)
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class PointCloudDeviationToolTests : IDisposable
    {
        private readonly string _tempDir;

        public PointCloudDeviationToolTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"deviation_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        /// <summary>평면 격자 20x20 (간격 1mm). bumpPoints 개는 Z+bumpMm 로 돌출.</summary>
        private static PointCloudData MakePlane(int bumpPoints = 0, float bumpMm = 0f)
        {
            const int n = 20;
            var xyz = new float[n * n * 3];
            int idx = 0, bumped = 0;
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                {
                    float z = bumped < bumpPoints ? bumpMm : 0f;
                    if (bumped < bumpPoints) bumped++;
                    xyz[idx++] = x;
                    xyz[idx++] = y;
                    xyz[idx++] = z;
                }
            return PointCloudData.FromArrays(xyz);
        }

        private string SaveReference(PointCloudData cloud)
        {
            var path = Path.Combine(_tempDir, "ref.vpc");
            cloud.SaveToFile(path);
            return path;
        }

        // ─── TransformUtils.ComputeNearestDistances ───

        [Fact]
        public void NearestDistances_exact_within_radius_and_clamped_outside()
        {
            var reference = MakePlane();
            var src = PointCloudData.FromArrays(new float[]
            {
                5f, 5f, 0.3f,    // 기준 위 0.3mm — 정확히 측정돼야 함
                5f, 5f, 50f      // 반경 밖 — 클램프
            });

            var d = TransformUtils.ComputeNearestDistances(reference, src, maxSearchRadius: 2f);

            Assert.Equal(0.3f, d[0], 2);
            Assert.Equal(2f, d[1], 2);
        }

        // ─── 툴 실행 ───

        [Fact]
        public void Identical_clouds_pass_with_zero_defects()
        {
            var reference = MakePlane();
            var tool = new PointCloudDeviationTool
            {
                ReferencePath = SaveReference(reference),
                ToleranceMm = 0.5f,
                OutputMode = PointCloudDeviationTool.DeviationOutputMode.ColorizeAll
            };
            VisionService.Instance.CurrentPointCloud = MakePlane();

            using var input = new Mat(2, 2, MatType.CV_8UC1);
            var result = tool.Execute(input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(0, (int)result.Data["DefectPoints"]);
            Assert.True((double)result.Data["MeanDeviation"] < 0.01);

            // ColorizeAll — 점 수 유지 + 전부 녹색 계열(정상)
            var colored = VisionService.Instance.CurrentPointCloud!;
            Assert.Equal(400, colored.PointCount);
            Assert.True(colored.Colors[0].G > colored.Colors[0].R);
        }

        [Fact]
        public void Bump_points_are_counted_as_defects_and_fail_judgment()
        {
            var reference = MakePlane();
            var tool = new PointCloudDeviationTool
            {
                ReferencePath = SaveReference(reference),
                ToleranceMm = 0.5f,
                HeatmapRangeMm = 1.0f,
                MaxDefectRatioPercent = 1.0f,   // 1% 까지 허용 — 돌출 30점(7.5%)은 NG
                OutputMode = PointCloudDeviationTool.DeviationOutputMode.KeepOriginal
            };
            VisionService.Instance.CurrentPointCloud = MakePlane(bumpPoints: 30, bumpMm: 2f);

            using var input = new Mat(2, 2, MatType.CV_8UC1);
            var result = tool.Execute(input);

            Assert.False(result.Success);
            Assert.Equal(30, (int)result.Data["DefectPoints"]);
            Assert.True((double)result.Data["MaxDeviation"] >= 1.9);
        }

        [Fact]
        public void DefectsOnly_keeps_only_out_of_tolerance_points()
        {
            var reference = MakePlane();
            var tool = new PointCloudDeviationTool
            {
                ReferencePath = SaveReference(reference),
                ToleranceMm = 0.5f,
                OutputMode = PointCloudDeviationTool.DeviationOutputMode.DefectsOnly
            };
            VisionService.Instance.CurrentPointCloud = MakePlane(bumpPoints: 25, bumpMm: 2f);

            using var input = new Mat(2, 2, MatType.CV_8UC1);
            tool.Execute(input);

            Assert.Equal(25, VisionService.Instance.CurrentPointCloud!.PointCount);
        }

        [Fact]
        public void Missing_reference_fails_gracefully()
        {
            var tool = new PointCloudDeviationTool { ReferencePath = "" };
            VisionService.Instance.CurrentPointCloud = MakePlane();

            using var input = new Mat(2, 2, MatType.CV_8UC1);
            var result = tool.Execute(input);

            Assert.False(result.Success);
            Assert.Contains("Reference", result.Message);
        }

        // ─── 히트맵 색 매핑 ───

        [Fact]
        public void DeviationToColor_maps_green_to_red()
        {
            var green = PointCloudDeviationTool.DeviationToColor(0f, 1f);
            var mid = PointCloudDeviationTool.DeviationToColor(0.5f, 1f);
            var red = PointCloudDeviationTool.DeviationToColor(1.5f, 1f);  // 상한 초과 → 클램프

            Assert.True(green.G > 150 && green.R < 50);
            Assert.True(mid.R > 150 && mid.G > 150);        // 황색 계열
            Assert.True(red.R > 150 && red.G < 50);
        }

        // ─── Clone / 직렬화 왕복 ───

        [Fact]
        public void Clone_copies_all_parameters()
        {
            var tool = new PointCloudDeviationTool
            {
                ReferencePath = @"C:\r.vpc",
                ToleranceMm = 0.3f,
                HeatmapRangeMm = 2.5f,
                MaxDefectRatioPercent = 2f,
                OutputMode = PointCloudDeviationTool.DeviationOutputMode.DefectsOnly
            };

            var clone = (PointCloudDeviationTool)tool.Clone();

            Assert.Equal(tool.ReferencePath, clone.ReferencePath);
            Assert.Equal(tool.ToleranceMm, clone.ToleranceMm);
            Assert.Equal(tool.HeatmapRangeMm, clone.HeatmapRangeMm);
            Assert.Equal(tool.MaxDefectRatioPercent, clone.MaxDefectRatioPercent);
            Assert.Equal(tool.OutputMode, clone.OutputMode);
        }
    }
}
