using System;
using System.Linq;
using System.Numerics;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// PointKdTree — ICP 최근접 탐색을 전수 비교에서 KD-트리로 바꿨다. 결과가 전수 비교와 <b>한 점도</b> 다르면 안 된다
    /// (정합 결과가 바뀌면 기존 레시피의 판정이 달라진다).
    /// </summary>
    public class PointKdTreeTests
    {
        /// <summary>예전 구현과 같은 규칙 — 가장 가까운 점, 같으면 번호가 작은 점, NaN 은 건너뜀.</summary>
        private static int BruteForce(Vector3[] pts, int count, Vector3 q)
        {
            if (float.IsNaN(q.X)) return -1;
            float best = float.MaxValue; int idx = -1;
            for (int j = 0; j < count; j++)
            {
                var p = pts[j];
                if (float.IsNaN(p.X)) continue;
                float d = (q - p).LengthSquared();
                if (d < best) { best = d; idx = j; }
            }
            return idx;
        }

        private static void AssertSameAsBruteForce(Vector3[] reference, Vector3[] queries)
        {
            var tree = new PointKdTree(reference, reference.Length);
            for (int i = 0; i < queries.Length; i++)
            {
                int expected = BruteForce(reference, reference.Length, queries[i]);
                int actual = tree.Nearest(queries[i]);
                Assert.True(expected == actual, $"query {i} {queries[i]}: brute {expected} vs tree {actual}");
            }
        }

        [Fact]
        public void Matches_brute_force_on_random_clouds()
        {
            var rnd = new Random(11);
            Vector3 R(float s) => new((float)rnd.NextDouble() * s, (float)rnd.NextDouble() * s, (float)rnd.NextDouble() * s * 0.1f);
            var reference = Enumerable.Range(0, 3000).Select(_ => R(300)).ToArray();
            var queries = Enumerable.Range(0, 1500).Select(_ => R(320) - new Vector3(10)).ToArray();
            AssertSameAsBruteForce(reference, queries);
        }

        /// <summary>격자는 등거리 점이 많다 — 동점일 때 번호가 작은 점을 고르는지(전수 비교와 같은 대응점).</summary>
        [Fact]
        public void Matches_brute_force_on_grid_with_ties()
        {
            var reference = (from x in Enumerable.Range(0, 20) from y in Enumerable.Range(0, 15) from z in Enumerable.Range(0, 3)
                             select new Vector3(x * 4, y * 4, z * 4)).ToArray();
            // 격자 칸 가운데 · 모서리 가운데 — 여러 점과 정확히 같은 거리
            var queries = (from x in Enumerable.Range(0, 19) from y in Enumerable.Range(0, 14)
                           select new Vector3(x * 4 + 2, y * 4 + 2, 2)).ToArray();
            AssertSameAsBruteForce(reference, queries);
        }

        [Fact]
        public void Skips_nan_points_and_queries()
        {
            var reference = new[] { new Vector3(float.NaN, 0, 0), new Vector3(5, 0, 0), new Vector3(1, 0, 0), new Vector3(float.NaN) };
            var tree = new PointKdTree(reference, reference.Length);

            Assert.Equal(2, tree.Count);
            Assert.Equal(2, tree.Nearest(Vector3.Zero));
            Assert.Equal(-1, tree.Nearest(new Vector3(float.NaN, 0, 0)));
            Assert.Equal(-1, new PointKdTree(Array.Empty<Vector3>(), 0).Nearest(Vector3.Zero));
        }

        /// <summary>count 보다 긴 배열(풀링된 버퍼)이어도 count 까지만 본다.</summary>
        [Fact]
        public void Uses_only_the_first_count_points()
        {
            var reference = new[] { new Vector3(10, 0, 0), new Vector3(0, 0, 0) };
            Assert.Equal(0, new PointKdTree(reference, 1).Nearest(Vector3.Zero));
        }

        /// <summary>ICP 결과가 전수 비교 시절과 같은지 — 같은 대응점이면 같은 변환이 나온다.</summary>
        [Fact]
        public void Icp_on_tree_gives_the_expected_transform()
        {
            var rnd = new Random(5);
            var pts = Enumerable.Range(0, 2000).Select(_ => new Vector3((float)rnd.NextDouble() * 200, (float)rnd.NextDouble() * 120, (float)rnd.NextDouble() * 30)).ToArray();
            var reference = PointCloudData.FromArrays(pts.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray());
            var pose = Matrix4x4.CreateRotationZ(4f * MathF.PI / 180f) * Matrix4x4.CreateTranslation(3f, -2f, 1f);
            var source = PointCloudData.FromArrays(pts.Select(p => Vector3.Transform(p, pose)).SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray());

            var m = TransformUtils.ICP(reference, source, 100, 0.001f, out var stats);

            Assert.True(stats.MeanError < 0.05f, $"mean error {stats.MeanError}");
            Assert.True(Math.Abs(TransformUtils.ToEulerAnglesDegrees(m).Z + 4f) < 0.1f);
        }
    }
}
