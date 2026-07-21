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

            // Coarse 정렬 → ICP (툴의 실행 경로와 동일한 합성)
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

        [Fact]
        public void Coarse_returns_identity_for_degenerate_cloud()
        {
            var tiny = PointCloudData.FromArrays(new float[] { 0, 0, 0, 1, 1, 1 });
            var reference = MakeBox(Matrix4x4.Identity);

            Assert.Equal(Matrix4x4.Identity, TransformUtils.CoarseAlignPCA(reference, tiny));
        }
    }
}
