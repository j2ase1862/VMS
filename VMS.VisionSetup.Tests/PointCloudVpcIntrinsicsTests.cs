using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// .vpc 후행 블록 — 저장본이 <b>카메라 내부 파라미터(fx/fy/cx/cy)</b>를 함께 싣는지.
    ///
    /// <para><b>왜 필요한가.</b> grab 점군의 X/Y 는 뎁스맵 화소이고 Z 만 mm 다. 화소를 mm 로
    /// 바꾸는 fx/fy 가 파일에 없으면, 저장한 점군을 다시 불러왔을 때 치수 자동 환산
    /// (PointCloudCluster 의 AutoFromCamera)이 꺼져 <b>XY 치수를 mm 로 잴 수 없다</b> —
    /// 배율을 촬영 당시에 따로 받아 적어야 했고, 오프라인 검증·시험 자료로 쓰기 어려웠다.</para>
    ///
    /// <para><b>호환성이 핵심이다.</b> 블록은 색상 뒤에 붙인다. 구버전 로더는 색상까지만 읽고
    /// 스트림 끝을 확인하지 않으므로 새 파일도 그대로 읽고, 새 로더는 블록이 없는 옛 파일도 읽는다.</para>
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class PointCloudVpcIntrinsicsTests : IDisposable
    {
        private readonly string _tempDir;

        public PointCloudVpcIntrinsicsTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"vpc_intr_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private string Path_(string name) => System.IO.Path.Combine(_tempDir, name);

        /// <summary>
        /// 카메라 grab 점군과 같은 모양 — X/Y 는 <b>뎁스맵 화소</b>, Z 는 mm.
        /// (MechMindCameraAcquisition 이 <c>new Vector3(srcCol, srcRow, z)</c> 로 채우는 규약)
        /// </summary>
        private static PointCloudData MakeGrabLikeCloud(int w, int h, float zMm)
        {
            var xyz = new float[w * h * 3];
            int i = 0;
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    xyz[i++] = col;
                    xyz[i++] = row;
                    xyz[i++] = zMm;
                }

            var cloud = PointCloudData.FromArrays(xyz);
            cloud.GridWidth = w;
            cloud.GridHeight = h;
            return cloud;
        }

        [Fact]
        public void 내부_파라미터가_저장되고_다시_읽힌다()
        {
            var cloud = MakeGrabLikeCloud(8, 6, 500f);
            cloud.Intrinsics = new DepthIntrinsics { Fx = 1234.5, Fy = 1230.25, Cx = 640.5, Cy = 480.25 };

            var path = Path_("with_intrinsics.vpc");
            cloud.SaveToFile(path);
            var loaded = PointCloudData.LoadFromFile(path);

            Assert.NotNull(loaded.Intrinsics);
            Assert.Equal(1234.5, loaded.Intrinsics!.Fx, 6);
            Assert.Equal(1230.25, loaded.Intrinsics.Fy, 6);
            Assert.Equal(640.5, loaded.Intrinsics.Cx, 6);
            Assert.Equal(480.25, loaded.Intrinsics.Cy, 6);

            // 점군 본문은 그대로여야 한다
            Assert.Equal(cloud.PointCount, loaded.PointCount);
            Assert.Equal(cloud.GridWidth, loaded.GridWidth);
            Assert.Equal(cloud.Positions[10], loaded.Positions[10]);
        }

        [Fact]
        public void 내부_파라미터가_없으면_옛_형식과_바이트가_같다()
        {
            // 후행 블록을 조건부로 붙이므로, 없을 때는 파일이 종전과 한 바이트도 달라지지 않아야 한다
            var cloud = MakeGrabLikeCloud(8, 6, 500f);
            Assert.Null(cloud.Intrinsics);

            var path = Path_("no_intrinsics.vpc");
            cloud.SaveToFile(path);

            int count = cloud.PointCount;
            long headerAndName = 4 + 4 + 4 + 4 + (1 + (cloud.Name ?? "PointCloud").Length);
            long expected = headerAndName + count * 12L + count * 3L;
            Assert.Equal(expected, new FileInfo(path).Length);

            Assert.Null(PointCloudData.LoadFromFile(path).Intrinsics);
        }

        [Fact]
        public void 옛_파일도_그대로_열린다()
        {
            // 후행 블록이 없는 파일 = 옛 버전이 저장한 파일
            var cloud = MakeGrabLikeCloud(8, 6, 500f);
            var path = Path_("legacy.vpc");
            cloud.SaveToFile(path);

            var loaded = PointCloudData.LoadFromFile(path);

            Assert.Equal(cloud.PointCount, loaded.PointCount);
            Assert.True(loaded.IsOrganized);
            Assert.Null(loaded.Intrinsics);
        }

        [Fact]
        public void 꼬리가_잘린_파일도_점군은_살린다()
        {
            // 복사 중 끊긴 파일 등 — 내부 파라미터만 포기하고 점군은 읽어야 한다.
            // 여기서 예외를 던지면 멀쩡한 점군이 "못 여는 파일" 이 된다.
            var cloud = MakeGrabLikeCloud(8, 6, 500f);
            cloud.Intrinsics = new DepthIntrinsics { Fx = 1000, Fy = 1000, Cx = 100, Cy = 100 };

            var path = Path_("truncated.vpc");
            cloud.SaveToFile(path);

            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes.Take(bytes.Length - 12).ToArray());   // 후행 블록 일부만 남김

            var loaded = PointCloudData.LoadFromFile(path);

            Assert.Equal(cloud.PointCount, loaded.PointCount);
            Assert.Null(loaded.Intrinsics);
        }

        /// <summary>
        /// 이 변경의 목적 그 자체 — <b>저장했다 불러온 점군으로 XY 치수가 mm 로 나오는지.</b>
        /// 종전에는 내부 파라미터가 없어 AutoFromCamera 가 수동 배율(기본 1.0 = 화소)로 폴백했다.
        /// </summary>
        [Fact]
        public void 저장본으로도_AutoFromCamera_치수가_mm_로_나온다()
        {
            const float z = 500f;          // 작동거리 500mm
            const double fx = 1000.0;      // → 1화소 = 500/1000 = 0.5mm
            const int w = 41, h = 21;      // 가로 40칸, 세로 20칸

            var cloud = MakeGrabLikeCloud(w, h, z);
            cloud.Intrinsics = new DepthIntrinsics { Fx = fx, Fy = fx, Cx = w / 2.0, Cy = h / 2.0 };

            var path = Path_("roundtrip.vpc");
            cloud.SaveToFile(path);
            var loaded = PointCloudData.LoadFromFile(path);

            var svc = VisionService.Instance;
            var savedCloud = svc.CurrentPointCloud;
            try
            {
                svc.CurrentPointCloud = loaded;

                var tool = new PointCloudClusterTool
                {
                    ScaleMode = PointCloudClusterTool.DimensionScaleMode.AutoFromCamera,
                    Tolerance = 3f,
                    MinPoints = 10,
                    MaxPoints = 100000
                };

                using var img = new Mat(h, w, MatType.CV_8UC1, Scalar.All(0));
                var result = tool.Execute(img);

                Assert.True(result.Success, result.Message);
                Assert.DoesNotContain("Auto scale unavailable", result.Message);

                // 1화소 = 0.5mm → 가로 40칸 = 20mm, 세로 20칸 = 10mm
                Assert.Equal(0.5, Convert.ToDouble(result.Data["Cluster0_MmPerPx"]), 3);
                Assert.Equal(20.0, Convert.ToDouble(result.Data["Cluster0_SizeX"]), 1);
                Assert.Equal(10.0, Convert.ToDouble(result.Data["Cluster0_SizeY"]), 1);
            }
            finally
            {
                svc.CurrentPointCloud = savedCloud;
            }
        }
    }
}
