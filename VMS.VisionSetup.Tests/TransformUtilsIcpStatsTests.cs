using System;
using System.Numerics;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// TransformUtils.ICP 통계 오버로드 검증 (PointCloud Registration 결과값 보강):
    /// - 이동된 점군 정합 시 Converged/MeanError/Iterations 가 일관된 값
    /// - tolerance 미달성 시 Converged=false + 반복 소진
    /// - 기존 시그니처(통계 없는 버전)와 동일 행렬 반환
    /// - ToEulerAnglesDegrees 가 회전 행렬의 각도를 복원
    /// </summary>
    public class TransformUtilsIcpStatsTests
    {
        private static PointCloudData MakeCube(Vector3 offset, float spacing = 10f)
        {
            // 3x3x3 격자 — 비퇴화 점군 (27점)
            var xyz = new float[27 * 3];
            int idx = 0;
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    for (int z = 0; z < 3; z++)
                    {
                        xyz[idx++] = x * spacing + offset.X;
                        xyz[idx++] = y * spacing + offset.Y;
                        xyz[idx++] = z * spacing + offset.Z;
                    }
            return PointCloudData.FromArrays(xyz);
        }

        [Fact]
        public void Icp_converges_on_slightly_translated_cloud()
        {
            var reference = MakeCube(Vector3.Zero);
            var source = MakeCube(new Vector3(0.5f, -0.3f, 0.2f));

            var transform = TransformUtils.ICP(reference, source, 100, 0.05f, out var stats);

            Assert.True(stats.Converged);
            Assert.True(stats.MeanError < 0.05f);
            Assert.InRange(stats.Iterations, 1, 100);

            // 복원된 이동량 크기 ≈ 입력 이동량 크기
            var recovered = Math.Sqrt(
                transform.M41 * transform.M41 +
                transform.M42 * transform.M42 +
                transform.M43 * transform.M43);
            var expected = new Vector3(0.5f, -0.3f, 0.2f).Length();
            Assert.InRange(recovered, expected - 0.1, expected + 0.1);
        }

        [Fact]
        public void Icp_reports_not_converged_when_tolerance_unreachable()
        {
            var reference = MakeCube(Vector3.Zero);
            var source = MakeCube(new Vector3(0.5f, 0f, 0f));

            // tolerance 0 은 도달 불가 → 반복 소진 + Converged=false
            TransformUtils.ICP(reference, source, 3, 0f, out var stats);

            Assert.False(stats.Converged);
            Assert.Equal(3, stats.Iterations);
        }

        [Fact]
        public void Icp_legacy_overload_returns_same_transform()
        {
            var reference = MakeCube(Vector3.Zero);
            var sourceA = MakeCube(new Vector3(0.5f, -0.3f, 0.2f));
            var sourceB = MakeCube(new Vector3(0.5f, -0.3f, 0.2f));

            var legacy = TransformUtils.ICP(reference, sourceA, 50, 0.01f);
            var withStats = TransformUtils.ICP(reference, sourceB, 50, 0.01f, out _);

            Assert.Equal(legacy.M41, withStats.M41, 3);
            Assert.Equal(legacy.M42, withStats.M42, 3);
            Assert.Equal(legacy.M43, withStats.M43, 3);
        }

        [Theory]
        [InlineData(30f)]
        [InlineData(-45f)]
        public void ToEulerAnglesDegrees_recovers_z_rotation(float degrees)
        {
            var m = Matrix4x4.CreateRotationZ(degrees * MathF.PI / 180f);

            var euler = TransformUtils.ToEulerAnglesDegrees(m);

            Assert.InRange(euler.Z, degrees - 0.5f, degrees + 0.5f);
            Assert.InRange(euler.X, -0.5f, 0.5f);
            Assert.InRange(euler.Y, -0.5f, 0.5f);
        }

        [Fact]
        public void ToEulerAnglesDegrees_identity_is_zero()
        {
            var euler = TransformUtils.ToEulerAnglesDegrees(Matrix4x4.Identity);
            Assert.Equal(Vector3.Zero, euler);
        }
    }
}
