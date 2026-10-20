using System;
using System.Numerics;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// TransformUtils.CoarseAlignPCA + ComputeInlierRatio 검증 (3D Coarse Matching 간이안):
    /// - 큰 회전(90°/180°)에서 ICP 단독은 실패하지만 Coarse+ICP 는 수렴
    /// - 이미 정렬된 점군에서는 Coarse 가 사실상 항등 (부작용 없음)
    /// - Confidence(inlier 비율)가 정합 성공/실패를 구분
    /// </summary>
    public class TransformUtilsCoarseAlignTests
    {
        /// <summary>
        /// 비대칭 박스 격자 (6x4x2, 간격 상이) + 한쪽 모서리 마커 클러스터.
        /// 규칙 격자는 중심 대칭이라 180° 회전 후보와 점수가 동률이 되므로
        /// (실제 대칭 물체의 알려진 한계), 마커로 대칭을 깨서 자세가 유일해지게 한다.
        /// </summary>
        private static PointCloudData MakeBox(Matrix4x4 transform)
        {
            int nx = 6, ny = 4, nz = 2;
            float sx = 10f, sy = 7f, sz = 4f;
            int gridCount = nx * ny * nz;
            const int markerCount = 27;
            var xyz = new float[(gridCount + markerCount) * 3];
            int idx = 0;
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        var p = Vector3.Transform(new Vector3(x * sx, y * sy, z * sz), transform);
                        xyz[idx++] = p.X;
                        xyz[idx++] = p.Y;
                        xyz[idx++] = p.Z;
                    }

            // 마커: (0,0,0) 모서리 바깥쪽 3x3x3 소격자 — 중심 대칭 파괴
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    for (int z = 0; z < 3; z++)
                    {
                        var p = Vector3.Transform(
                            new Vector3(-8f + x * 1.5f, -8f + y * 1.5f, -6f + z * 1.5f), transform);
                        xyz[idx++] = p.X;
                        xyz[idx++] = p.Y;
                        xyz[idx++] = p.Z;
                    }
            return PointCloudData.FromArrays(xyz);
        }

        private static Matrix4x4 RotZ(float degrees) =>
            Matrix4x4.CreateRotationZ(degrees * MathF.PI / 180f);

        [Theory]
        [InlineData(90f)]
        [InlineData(180f)]
        public void Coarse_plus_icp_converges_on_large_rotation(float degrees)
        {
            var reference = MakeBox(Matrix4x4.Identity);
            var pose = RotZ(degrees) * Matrix4x4.CreateTranslation(15f, -10f, 5f);
            var source = MakeBox(pose);

            // PCA 하나를 고른 뒤 ICP (옛 툴 경로 — 지금 툴은 RegisterWithCoarse 를 쓴다)
            var coarse = TransformUtils.CoarseAlignPCA(reference, source);
            var coarseAligned = TransformUtils.TransformPointCloud(source, coarse);
            var icp = TransformUtils.ICP(reference, coarseAligned, 100, 0.05f, out var stats);
            var total = coarse * icp;

            Assert.True(stats.Converged, $"coarse+ICP not converged at {degrees}° (meanError={stats.MeanError})");
            Assert.True(stats.MeanError < 0.05f);

            var confidence = TransformUtils.ComputeInlierRatio(reference, source, total, 1.0f);
            Assert.True(confidence > 0.95f, $"confidence {confidence} too low at {degrees}°");

            coarseAligned.Dispose();
        }

        [Fact]
        public void Icp_alone_fails_on_180_rotation_proving_coarse_is_needed()
        {
            var reference = MakeBox(Matrix4x4.Identity);
            var source = MakeBox(RotZ(180f) * Matrix4x4.CreateTranslation(15f, -10f, 5f));

            var icpOnly = TransformUtils.ICP(reference, source, 100, 0.05f, out _);
            var confidence = TransformUtils.ComputeInlierRatio(reference, source, icpOnly, 1.0f);

            // 180° 는 ICP 단독으론 로컬 미니멈에 갇힘 — coarse 도입의 근거
            Assert.True(confidence < 0.9f,
                $"expected ICP-only to fail at 180° but confidence was {confidence}");
        }

        [Fact]
        public void Coarse_on_already_aligned_cloud_is_near_identity()
        {
            var reference = MakeBox(Matrix4x4.Identity);
            var source = MakeBox(Matrix4x4.CreateTranslation(0.2f, -0.1f, 0.1f));

            var coarse = TransformUtils.CoarseAlignPCA(reference, source);
            var euler = TransformUtils.ToEulerAnglesDegrees(coarse);

            // 회전 성분이 거의 없어야 함 (작은 이동만 흡수)
            Assert.True(Math.Abs(euler.X) < 2f && Math.Abs(euler.Y) < 2f && Math.Abs(euler.Z) < 2f,
                $"coarse rotation unexpectedly large: {euler}");
        }

        [Fact]
        public void InlierRatio_is_high_for_identity_on_identical_clouds_and_low_for_offset()
        {
            var reference = MakeBox(Matrix4x4.Identity);
            var identical = MakeBox(Matrix4x4.Identity);
            var offset = MakeBox(Matrix4x4.CreateTranslation(5f, 0f, 0f));

            Assert.True(TransformUtils.ComputeInlierRatio(reference, identical, Matrix4x4.Identity, 0.5f) > 0.99f);
            Assert.True(TransformUtils.ComputeInlierRatio(reference, offset, Matrix4x4.Identity, 0.5f) < 0.5f);
        }

        // ───────────── RegisterWithCoarse (툴이 쓰는 경로) ─────────────

        /// <summary>
        /// 현장 프레임과 비슷한 꼴 — 사각 링(376×252mm, 폭 20mm, 4mm 간격) + 한쪽에 치우친 낮은 가로대.
        /// <paramref name="cutX"/> 보다 오른쪽은 찍히지 않은 것으로 뺀다(현장에서 프레임 일부가 가려진 촬영본).
        /// </summary>
        private static PointCloudData MakeFrame(Matrix4x4 transform, float cutX = 999f, float noise = 0f, int seed = 1)
        {
            var rnd = new Random(seed);
            float N() => (float)(rnd.NextDouble() - 0.5) * noise;
            var xyz = new System.Collections.Generic.List<float>();
            for (float x = 0; x <= 376; x += 4)
                for (float y = 0; y <= 252; y += 4)
                {
                    bool ring = x < 20 || x > 356 || y < 20 || y > 232;
                    bool bar = x >= 100 && x < 120;
                    if (!(ring || bar) || x > cutX) continue;
                    var p = Vector3.Transform(new Vector3(x - 188 + N(), y - 126 + N(), (bar ? -25f : 0f) + N()), transform);
                    xyz.Add(p.X); xyz.Add(p.Y); xyz.Add(p.Z);
                }
            return PointCloudData.FromArrays(xyz.ToArray());
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(180f)]
        public void RegisterWithCoarse_converges_on_large_rotation(float degrees)
        {
            var reference = MakeBox(Matrix4x4.Identity);
            var source = MakeBox(RotZ(degrees) * Matrix4x4.CreateTranslation(15f, -10f, 5f));

            var total = TransformUtils.RegisterWithCoarse(reference, source, 100, 0.05f, 1.0f, out var stats, out _);

            Assert.True(stats.MeanError < 0.05f, $"mean error {stats.MeanError} at {degrees}°");
            Assert.True(TransformUtils.ComputeInlierRatio(reference, source, total, 1.0f) > 0.95f);
        }

        /// <summary>
        /// 핵심 — 일부가 가려진 프레임이 조금 돌아가 있으면 PCA 주축이 틀어져 옛 방식은 수십 도 어긋났다.
        /// 새 방식은 "그대로" 를 포함한 후보를 끝까지 맞춰 보고 고르므로 실제 각도를 찾는다.
        /// </summary>
        [Theory]
        [InlineData(5f)]
        [InlineData(-20f)]
        [InlineData(35f)]
        public void RegisterWithCoarse_finds_the_pose_of_a_partly_hidden_frame(float degrees)
        {
            var reference = MakeFrame(Matrix4x4.Identity);
            var source = MakeFrame(RotZ(degrees) * Matrix4x4.CreateTranslation(6f, -4f, 0f), cutX: 340f, noise: 1f, seed: 7);

            var total = TransformUtils.RegisterWithCoarse(reference, source, 50, 0.05f, 3f, out _, out var candidates);
            float rotZ = TransformUtils.ToEulerAnglesDegrees(total).Z;

            Assert.True(Math.Abs(rotZ + degrees) < 3f, $"rotZ {rotZ}° — expected about {-degrees}°");
            Assert.Equal(5, candidates.Length);
            Assert.Single(candidates, c => c.Chosen);
        }

        /// <summary>위 시험이 무는지 — 같은 조건에서 옛 방식(PCA 하나를 고른 뒤 ICP)은 크게 어긋난다.</summary>
        [Fact]
        public void Old_pca_then_icp_misses_a_partly_hidden_frame()
        {
            var reference = MakeFrame(Matrix4x4.Identity);
            var source = MakeFrame(RotZ(5f) * Matrix4x4.CreateTranslation(6f, -4f, 0f), cutX: 340f, noise: 1f, seed: 7);

            var coarse = TransformUtils.CoarseAlignPCA(reference, source);
            using var moved = TransformUtils.TransformPointCloud(source, coarse);
            var total = coarse * TransformUtils.ICP(reference, moved, 50, 0.05f, out _);

            Assert.True(Math.Abs(TransformUtils.ToEulerAnglesDegrees(total).Z + 5f) > 20f);
        }

        /// <summary>대칭 프레임이 같은 신뢰도의 "180° 뒤집힌" 답을 가져도 덜 돌린 쪽을 고른다.</summary>
        [Theory]
        [InlineData(10f)]
        [InlineData(-25f)]
        public void RegisterWithCoarse_prefers_the_smaller_rotation_on_a_tie(float degrees)
        {
            var reference = MakeFrame(Matrix4x4.Identity);
            var source = MakeFrame(RotZ(degrees) * Matrix4x4.CreateTranslation(6f, -4f, 0f));

            var total = TransformUtils.RegisterWithCoarse(reference, source, 50, 0.05f, 3f, out _, out _);

            Assert.True(Math.Abs(TransformUtils.ToEulerAnglesDegrees(total).Z + degrees) < 1.5f);
        }

        /// <summary>"그대로" 가 늘 결선에 있으므로 Coarse 를 켜도 ICP 단독보다 신뢰도가 낮아지지 않는다.</summary>
        [Fact]
        public void RegisterWithCoarse_is_never_worse_than_icp_alone()
        {
            var reference = MakeFrame(Matrix4x4.Identity);
            var source = MakeFrame(RotZ(5f) * Matrix4x4.CreateTranslation(2f, 1f, 0f), noise: 1f, seed: 3);

            var icpOnly = TransformUtils.ICP(reference, source, 50, 0.05f, out _);
            var withCoarse = TransformUtils.RegisterWithCoarse(reference, source, 50, 0.05f, 3f, out _, out var candidates);

            float alone = TransformUtils.ComputeInlierRatio(reference, source, icpOnly, 3f);
            float coarse = TransformUtils.ComputeInlierRatio(reference, source, withCoarse, 3f);
            Assert.True(coarse >= alone - 0.03f, $"coarse {coarse:P0} < ICP alone {alone:P0}");
            Assert.NotNull(candidates[0].FinalConfidence); // "그대로" 는 끝까지 맞춰 본다
        }

        [Fact]
        public void Coarse_returns_identity_for_degenerate_cloud()
        {
            var tiny = PointCloudData.FromArrays(new float[] { 0, 0, 0, 1, 1, 1 });
            var reference = MakeBox(Matrix4x4.Identity);

            Assert.Equal(Matrix4x4.Identity, TransformUtils.CoarseAlignPCA(reference, tiny));
        }
    }
}
