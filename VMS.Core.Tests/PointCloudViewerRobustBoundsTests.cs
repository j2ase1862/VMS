using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using System.Numerics;
using VMS.Core.Controls;
using Xunit;

namespace VMS.Core.Tests
{
    /// <summary>
    /// PointCloudViewer.ComputeRobustBounds 검증 — 실측 점군의 소수 깊이 이상점이
    /// 그리드/바운딩 박스를 부풀려 본체에서 떨어져 보이던 문제(2026-07-21 현장 확인)의
    /// 회귀 방지.
    /// </summary>
    public class PointCloudViewerRobustBoundsTests
    {
        private static Vector3Collection MakeSheet(int count = 10_000)
        {
            var positions = new Vector3Collection(count);
            for (int i = 0; i < count; i++)
                positions.Add(new Vector3(i % 100, -(i % 50), i / 100f));
            return positions;
        }

        [Fact]
        public void Depth_outliers_do_not_inflate_bounds()
        {
            var positions = MakeSheet();
            // 본체(Y -49~0)에서 한참 떨어진 깊이 이상점 소수 — 0.5% 미만
            for (int i = 0; i < 10; i++)
                positions.Add(new Vector3(50, -5000f - i, 50));

            var (min, max) = PointCloudViewer.ComputeRobustBounds(positions);

            Assert.True(min.Y > -100f, $"outlier가 바운즈를 부풀림: min.Y={min.Y}");
            Assert.True(max.Y <= 0.5f);
        }

        [Fact]
        public void Bounds_cover_main_body()
        {
            var (min, max) = PointCloudViewer.ComputeRobustBounds(MakeSheet());

            // 본체 범위(0~99 / -49~0)를 실질적으로 커버해야 함 (백분위 절사 소량 허용)
            Assert.True(min.X <= 2f && max.X >= 97f);
            Assert.True(min.Y <= -46f && max.Y >= -2f);
        }

        [Fact]
        public void Flat_axis_is_padded_to_avoid_degenerate_box()
        {
            var positions = new Vector3Collection();
            for (int i = 0; i < 1000; i++)
                positions.Add(new Vector3(i, 0f, i % 30));   // Y 완전 평면

            var (min, max) = PointCloudViewer.ComputeRobustBounds(positions);

            Assert.True(max.Y - min.Y > 0f, "평면 축이 퇴화됨 (박스 접힘)");
        }
    }
}
