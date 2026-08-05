using VMS.Core.Controls;
using Xunit;

namespace VMS.Core.Tests
{
    /// <summary>
    /// PointCloudViewer.SelectLodStride 검증 — Mech-Mind 풀해상도(3.1M)가 기본
    /// 전체 표시되도록 상향된 임계값과 수동 밀도(0=자동/1/2/4) 규칙의 회귀 방지.
    /// (Mech-Eye Viewer 대비 점군이 성기게 보이던 2026-08-05 현장 피드백)
    /// </summary>
    public class PointCloudViewerLodStrideTests
    {
        [Theory]
        [InlineData(100_000, 1)]        // 소규모 — 전체
        [InlineData(3_145_728, 1)]      // Mech-Mind 2048×1536 풀해상도 — 전체 표시가 기본
        [InlineData(4_000_001, 2)]      // 4M 초과 — 1/2
        [InlineData(8_000_001, 4)]      // 8M 초과 — 1/4
        public void Auto_stride_follows_thresholds(int totalCount, int expected)
        {
            Assert.Equal(expected, PointCloudViewer.SelectLodStride(totalCount, overrideStride: 0));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void Manual_override_wins_regardless_of_count(int overrideStride)
        {
            Assert.Equal(overrideStride, PointCloudViewer.SelectLodStride(10_000_000, overrideStride));
            Assert.Equal(overrideStride, PointCloudViewer.SelectLodStride(1_000, overrideStride));
        }
    }
}
