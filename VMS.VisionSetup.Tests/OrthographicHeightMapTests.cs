using System;
using System.Numerics;
using VMS.Camera.Converters;
using VMS.Camera.Models;
using VMS.VisionSetup.Services;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 비격자(unorganized) 점군 정사투영 Height Map 검증 (3D 개선 문서 잔여 항목):
    /// - Registration/Cluster 후처럼 격자 구조가 깨진 점군도 depth map 생성 가능
    /// - 셀당 윗면(Z 최대) 채택, 빈 셀 0, 픽셀↔3D 역참조 생성
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class OrthographicHeightMapTests
    {
        /// <summary>비격자 점군 — 50x50mm 평면(z=10) + 중앙 10x10mm 돌출(z=15). GridWidth=0.</summary>
        private static PointCloudData MakeUnorganizedScene()
        {
            var list = new System.Collections.Generic.List<float>();
            var rand = new Random(42);
            for (int i = 0; i < 8000; i++)
            {
                float x = (float)(rand.NextDouble() * 50.0);
                float y = (float)(rand.NextDouble() * 50.0);
                bool bump = x is > 20f and < 30f && y is > 20f and < 30f;
                float z = bump ? 15f : 10f;
                list.Add(x); list.Add(y); list.Add(z);
            }
            var cloud = PointCloudData.FromArrays(list.ToArray());
            Assert.False(cloud.IsOrganized);
            return cloud;
        }

        [Fact]
        public void Ortho_projection_builds_map_with_top_surface_and_lookup()
        {
            var cloud = MakeUnorganizedScene();

            var (depthMap, pixelTo3D, w, h) =
                PointCloudConverter.OrthographicToDepthMap32F(cloud, zRef: 0f);

            using (depthMap)
            {
                Assert.InRange(w, 64, 2048);
                Assert.InRange(h, 64, 2048);
                Assert.Equal(w * h, pixelTo3D.Length);

                // 중앙 돌출 영역 픽셀 = 15, 주변 평면 = 10 (셀당 윗면 채택)
                int uBump = (int)(25f / 50f * w);
                int vBump = (int)(25f / 50f * h);
                float bumpVal = depthMap.At<float>(vBump, uBump);
                Assert.InRange(bumpVal, 14.5f, 15.5f);

                int uFlat = (int)(5f / 50f * w);
                int vFlat = (int)(5f / 50f * h);
                float flatVal = depthMap.At<float>(vFlat, uFlat);
                Assert.InRange(flatVal, 9.5f, 10.5f);

                // 역참조 — 돌출 픽셀의 3D 점이 실제 돌출 영역 좌표
                var p3d = pixelTo3D[vBump * w + uBump];
                Assert.NotNull(p3d);
                Assert.InRange(p3d!.Value.Z, 14.5f, 15.5f);
            }
        }

        [Fact]
        public void Ortho_zRef_offsets_values()
        {
            var cloud = MakeUnorganizedScene();
            var (depthMap, _, w, h) =
                PointCloudConverter.OrthographicToDepthMap32F(cloud, zRef: 10f);
            using (depthMap)
            {
                int uFlat = (int)(5f / 50f * w);
                int vFlat = (int)(5f / 50f * h);
                Assert.InRange(depthMap.At<float>(vFlat, uFlat), -0.5f, 0.5f);
            }
        }

        [Fact]
        public void VisionService_GenerateHeightMap_accepts_unorganized_cloud()
        {
            var cloud = MakeUnorganizedScene();

            var (heightMap8U, depthMap32F, metadata) =
                VisionService.Instance.GenerateHeightMap(cloud, zRef: 0f, zMin: 8f, zMax: 16f);

            Assert.False(heightMap8U.Empty());
            Assert.Equal(metadata.Width * metadata.Height, metadata.PixelTo3D.Length);
            Assert.True(metadata.Width >= 64 && metadata.Height >= 64);

            heightMap8U.Dispose();
            depthMap32F.Dispose();
        }

        [Fact]
        public void Ortho_rejects_empty_or_degenerate_input()
        {
            var empty = PointCloudData.FromArrays(Array.Empty<float>());
            Assert.ThrowsAny<InvalidOperationException>(
                () => PointCloudConverter.OrthographicToDepthMap32F(empty, 0f));
        }
    }
}
