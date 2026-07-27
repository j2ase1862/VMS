using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 현장 .vpc 실데이터로 v1.5.x 검증 체크리스트 §1b 를 헤드리스로 수행하는 진단 테스트.
    /// 체인: (Height Map 생성) → HeightSlicer → PointCloud Mask Crop → PointCloud Cluster
    /// — 앱의 Run 버튼과 동일한 VisionService.ExecuteAll 경로 사용.
    ///
    /// 검증 항목:
    ///   1. 체인이 성공하고 클러스터가 잡히는지 (ClusterCount ≥ 1)
    ///   2. Mask Crop coverage % 가 슬라이스 밴드의 점 비율과 상식적으로 일치하는지
    ///   3. 재Grab 없이 Run 두 번 연속 → 동일 결과 (점군 원본 복원)
    ///
    /// 슬라이스 밴드는 절대 Z(mm, zRef=0) 로 지정 — 사용자 지정 밴드와
    /// 트레이 최빈값 기반 완화 밴드를 비교 실행한다.
    /// 환경 의존(D:\VMS 3D DATA) — CI 는 *Diagnostic* 필터로 제외.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class VpcMaskCropChainDiagnosticTests
    {
        private const string DataDir = @"D:\VMS 3D DATA";
        private readonly ITestOutputHelper _out;

        public VpcMaskCropChainDiagnosticTests(ITestOutputHelper output) => _out = output;

        // 사용자 지정 절대 Z 밴드 (2026-07-24): 부품 = 950~1075mm — 실물 3개가 정확히 분리됨.
        // 상한을 트레이 근처(-2.5mm)로 완화하면 트레이 인접점이 부품·주변 구조를 하나로 연결해
        // 거대 클러스터(MaxPoints 초과로 제외)로 병합 → 오히려 부품을 놓친다. 비교용으로 유지.
        private const float UserZMin = 950f;
        private const float UserZMax = 1075f;

        [Fact]
        public void Diagnostic_VpcMaskCropChain_RunsOnFieldData()
        {
            if (!Directory.Exists(DataDir))
            {
                _out.WriteLine($"데이터 폴더 없음 — 스킵: {DataDir}");
                return;
            }

            var files = Directory.GetFiles(DataDir, "*.vpc");
            Assert.NotEmpty(files);

            foreach (var file in files)
                RunFile(file);
        }

        private void RunFile(string path)
        {
            var cloud = PointCloudData.LoadFromFile(path);
            try
            {
                _out.WriteLine($"════ {Path.GetFileName(path)} ════");
                _out.WriteLine($"Points={cloud.PointCount:N0}  Grid={cloud.GridWidth}x{cloud.GridHeight}  " +
                               $"Organized={cloud.IsOrganized}  Intrinsics={(cloud.Intrinsics != null ? "있음" : "없음(.vpc)")}");

                // ── Z 분포: 트레이 평면(최빈값) 파악 ──
                var zs = new List<float>(cloud.PointCount);
                int invalid = 0;
                foreach (var p in cloud.Positions)
                {
                    if (p.Z == 0f) { invalid++; continue; }
                    zs.Add(p.Z);
                }
                Assert.True(zs.Count > 1000, "유효 Z 점이 너무 적음");

                var hist = new Dictionary<int, int>();
                foreach (var z in zs)
                {
                    int bin = (int)MathF.Floor(z);
                    hist[bin] = hist.TryGetValue(bin, out var c) ? c + 1 : 1;
                }
                float trayZ = hist.OrderByDescending(kv => kv.Value).First().Key + 0.5f;
                zs.Sort();
                _out.WriteLine($"Z: valid={zs.Count:N0} invalid(Z=0)={invalid:N0}  " +
                               $"min={zs[0]:F1} med={zs[zs.Count / 2]:F1} max={zs[^1]:F1}  tray(mode)={trayZ:F1}mm");

                // ── 밴드 A: 사용자 지정 [950, 1075] — 재실행 복원까지 전체 검증 ──
                var (countA, _) = RunBand(cloud, zs, UserZMin, UserZMax,
                    $"A:사용자 [{UserZMin:F0}, {UserZMax:F0}]", checkRerun: true);

                // ── 밴드 B: 상한 완화 [950, 트레이-2.5mm] — 낮은 부품 포함 여부 비교 ──
                float relaxedMax = trayZ - 2.5f;
                var (countB, _) = RunBand(cloud, zs, UserZMin, relaxedMax,
                    $"B:완화 [{UserZMin:F0}, {relaxedMax:F1}]", checkRerun: false);

                _out.WriteLine($"[비교] ClusterCount — 밴드A(≤1075)={countA} vs 밴드B(≤트레이-2.5)={countB}");
                _out.WriteLine("");
            }
            finally
            {
                var service = VisionService.Instance;
                service.ClearTools();
                service.CurrentPointCloud = null;
                cloud.Dispose();
            }
        }

        /// <summary>지정 절대 Z 밴드로 체인 1회(옵션: 2회) 실행하고 (ClusterCount, OutputPoints) 반환.</summary>
        private (int clusterCount, int outputPoints) RunBand(
            PointCloudData cloud, List<float> sortedZ, float zMin, float zMax, string label, bool checkRerun)
        {
            var service = VisionService.Instance;
            service.ClearTools(); // 연결도 함께 초기화

            // zRef=0 → depth map = 절대 Z. 측정실패(Z=0)는 밴드 밖이라 자연 제외.
            var (heightMap8U, _, _) = service.GenerateHeightMap(cloud, 0f, zMin, zMax);
            service.SetImage(heightMap8U);
            heightMap8U.Dispose();
            service.CurrentPointCloud = cloud;

            var slicer = new HeightSlicerTool { Name = "Height Slicer", MinZ = zMin, MaxZ = zMax };
            var crop = new PointCloudMaskCropTool { Name = "Mask Crop" };
            var cluster = new PointCloudClusterTool
            {
                Name = "Cluster",
                MinPoints = 300,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal,
            };

            service.AddTool(slicer);
            service.AddTool(crop);
            service.AddTool(cluster);
            service.AddConnection(slicer, crop, ConnectionType.Image);   // 슬라이스 결과 = 크롭 마스크
            service.AddConnection(crop, cluster, ConnectionType.Result); // 순서/실패 전파

            var run1 = service.ExecuteAll();
            foreach (var r in run1)
                _out.WriteLine($"[{label}] {(r.Success ? "OK " : "FAIL")} {r.Message}");

            Assert.Equal(3, run1.Count);
            Assert.True(run1[0].Success, $"HeightSlicer 실패: {run1[0].Message}");
            Assert.True(run1[1].Success, $"MaskCrop 실패: {run1[1].Message}");
            Assert.True(run1[2].Success, $"Cluster 실패: {run1[2].Message}");

            int in1 = Convert.ToInt32(run1[1].Data["InputPoints"]);
            int out1 = Convert.ToInt32(run1[1].Data["OutputPoints"]);
            double coverage = Convert.ToDouble(run1[1].Data["MaskCoveragePercent"]);
            int clusters1 = Convert.ToInt32(run1[2].Data["ClusterCount"]);

            // coverage 상식 검증 — 밴드 내 점 비율(전체 점 대비)과 자릿수 수준 일치.
            int bandCount = CountInRange(sortedZ, zMin, zMax);
            double bandRatioPct = 100.0 * bandCount / cloud.PointCount;
            _out.WriteLine($"[{label}] coverage={coverage:F1}% vs 밴드점비율={bandRatioPct:F1}%  " +
                           $"crop {in1:N0}→{out1:N0}  ClusterCount={clusters1}");
            Assert.True(coverage > 0 && coverage < 100, "coverage 가 0% 또는 100% — 마스크 비정상");
            Assert.True(Math.Abs(coverage - bandRatioPct) < Math.Max(5.0, bandRatioPct),
                $"coverage({coverage:F1}%)가 밴드 점 비율({bandRatioPct:F1}%)과 크게 어긋남");
            Assert.True(clusters1 >= 1, "클러스터 0개");

            for (int i = 0; i < clusters1 && i < 10; i++)
            {
                if (!run1[2].Data.TryGetValue($"Cluster{i}_Points", out var cp)) break;
                run1[2].Data.TryGetValue($"Cluster{i}_SizeX", out var sx);
                run1[2].Data.TryGetValue($"Cluster{i}_SizeY", out var sy);
                run1[2].Data.TryGetValue($"Cluster{i}_SizeZ", out var sz);
                run1[2].Data.TryGetValue($"Cluster{i}_CenterZ", out var cz);
                _out.WriteLine($"  Cluster{i}: {cp} pts, Size(px,px,mm)=({sx:F0}, {sy:F0}, " +
                               $"{Convert.ToDouble(sz):F1}), CenterZ={Convert.ToDouble(cz):F1}mm");
            }

            if (checkRerun)
            {
                // §1b-3: 재Grab 없이 2차 실행 — 원본 복원 확인
                var run2 = service.ExecuteAll();
                int in2 = Convert.ToInt32(run2[1].Data["InputPoints"]);
                int out2 = Convert.ToInt32(run2[1].Data["OutputPoints"]);
                int clusters2 = Convert.ToInt32(run2[2].Data["ClusterCount"]);

                Assert.True(run2[1].Success && run2[2].Success, "2차 실행 실패");
                Assert.Equal(in1, in2);
                Assert.Equal(out1, out2);
                Assert.Equal(clusters1, clusters2);
                _out.WriteLine($"[{label}] 2차 실행 동일 (In={in2:N0}, Out={out2:N0}, " +
                               $"Clusters={clusters2}) — 점군 원본 복원 OK");
            }

            service.ClearTools();
            return (clusters1, out1);
        }

        private static int CountInRange(List<float> sortedZ, float lo, float hi)
        {
            int a = LowerBound(sortedZ, lo);
            int b = LowerBound(sortedZ, hi);
            return Math.Max(0, b - a);
        }

        private static int LowerBound(List<float> sorted, float value)
        {
            int i = sorted.BinarySearch(value);
            if (i < 0) return ~i;
            while (i > 0 && sorted[i - 1] >= value) i--;
            return i;
        }
    }
}
