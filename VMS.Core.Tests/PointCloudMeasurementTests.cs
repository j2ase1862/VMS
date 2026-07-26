using System.Collections.Generic;
using System.Numerics;
using VMS.Camera.Models;
using VMS.Core.Controls;
using Xunit;

namespace VMS.Core.Tests
{
    public class PointCloudMeasurementTests
    {
        private static DepthIntrinsics MakeIntrinsics(double fx = 2000, double fy = 2000, double cx = 640, double cy = 480)
            => new() { Fx = fx, Fy = fy, Cx = cx, Cy = cy };

        // ── ToMetric / Distance / Angle ──

        [Fact]
        public void ToMetric_WithIntrinsics_UnprojectsPixelsToMm()
        {
            var intr = MakeIntrinsics();
            // 주점에서 +100px, 깊이 1000mm → X = 100 × 1000 / 2000 = 50mm
            var metric = PointCloudMeasurement.ToMetric(new Vector3(740, 480, 1000), intr);

            Assert.Equal(50f, metric.X, 3);
            Assert.Equal(0f, metric.Y, 3);
            Assert.Equal(1000f, metric.Z, 3);
        }

        [Fact]
        public void ToMetric_WithoutIntrinsics_ReturnsInput()
        {
            var p = new Vector3(1, 2, 3);
            Assert.Equal(p, PointCloudMeasurement.ToMetric(p, null));
        }

        [Fact]
        public void Distance_SameDepth_ScalesByZOverFx()
        {
            var intr = MakeIntrinsics();
            // 가로 100px @ Z=1000mm, fx=2000 → 50mm
            float dist = PointCloudMeasurement.Distance(
                new Vector3(640, 480, 1000), new Vector3(740, 480, 1000), intr);

            Assert.Equal(50f, dist, 3);
        }

        [Fact]
        public void Distance_AsymmetricFy_UsesFyForVertical()
        {
            var intr = MakeIntrinsics(fx: 1000, fy: 500, cx: 0, cy: 0);
            // 세로 100px @ Z=1000mm, fy=500 → 200mm
            float dist = PointCloudMeasurement.Distance(
                new Vector3(0, 0, 1000), new Vector3(0, 100, 1000), intr);

            Assert.Equal(200f, dist, 3);
        }

        [Fact]
        public void Distance_NoIntrinsics_IsEuclidean()
        {
            float dist = PointCloudMeasurement.Distance(
                new Vector3(0, 0, 0), new Vector3(3, 4, 0), null);

            Assert.Equal(5f, dist, 3);
        }

        [Fact]
        public void AngleDeg_RightAngle_Returns90()
        {
            float deg = PointCloudMeasurement.AngleDeg(
                new Vector3(1, 0, 0), Vector3.Zero, new Vector3(0, 1, 0), null);

            Assert.Equal(90f, deg, 2);
        }

        [Fact]
        public void AngleDeg_CollinearOpposite_Returns180()
        {
            float deg = PointCloudMeasurement.AngleDeg(
                new Vector3(-5, 0, 0), Vector3.Zero, new Vector3(7, 0, 0), null);

            Assert.Equal(180f, deg, 2);
        }

        [Fact]
        public void AngleDeg_DegenerateVertex_ReturnsNaN()
        {
            float deg = PointCloudMeasurement.AngleDeg(
                Vector3.Zero, Vector3.Zero, new Vector3(1, 0, 0), null);

            Assert.True(float.IsNaN(deg));
        }

        // ── IsMetric ──

        [Fact]
        public void IsMetric_OrganizedWithoutIntrinsics_False()
        {
            var cloud = new PointCloudData
            {
                Positions = new Vector3[4],
                GridWidth = 2,
                GridHeight = 2
            };
            Assert.False(PointCloudMeasurement.IsMetric(cloud));
        }

        [Fact]
        public void IsMetric_OrganizedWithIntrinsics_True()
        {
            var cloud = new PointCloudData
            {
                Positions = new Vector3[4],
                GridWidth = 2,
                GridHeight = 2,
                Intrinsics = MakeIntrinsics()
            };
            Assert.True(PointCloudMeasurement.IsMetric(cloud));
        }

        [Fact]
        public void IsMetric_UnorganizedCloud_True()
        {
            var cloud = new PointCloudData { Positions = new Vector3[5] };
            Assert.True(PointCloudMeasurement.IsMetric(cloud));
        }

        // ── Projection / Picking ──
        // 카메라: (0,0,10) 에서 -Z 방향, up +Y, 수평 FOV 45°, 뷰포트 800×400

        private static Matrix4x4 TestViewProj() =>
            PointCloudMeasurement.BuildViewProjection(
                new Vector3(0, 0, 10), new Vector3(0, 0, -1), new Vector3(0, 1, 0),
                horizontalFovDeg: 45f, aspect: 2f, nearPlane: 0.1f, farPlane: 1000f);

        [Fact]
        public void Project_CenterOfView_MapsToScreenCenter()
        {
            var vp = TestViewProj();
            bool ok = PointCloudMeasurement.TryProjectToScreen(
                Vector3.Zero, vp, 800, 400, out double sx, out double sy);

            Assert.True(ok);
            Assert.Equal(400.0, sx, 1);
            Assert.Equal(200.0, sy, 1);
        }

        [Fact]
        public void Project_PointBehindCamera_ReturnsFalse()
        {
            var vp = TestViewProj();
            bool ok = PointCloudMeasurement.TryProjectToScreen(
                new Vector3(0, 0, 20), vp, 800, 400, out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void Project_RightOfCenter_MapsRight()
        {
            var vp = TestViewProj();
            PointCloudMeasurement.TryProjectToScreen(
                new Vector3(1, 0, 0), vp, 800, 400, out double sx, out double sy);

            Assert.True(sx > 400.0);
            Assert.Equal(200.0, sy, 1);
        }

        [Fact]
        public void FindNearest_PicksPointUnderCursor()
        {
            var vp = TestViewProj();
            var points = new List<Vector3> { new(0, 0, 0), new(2, 0, 0) };

            int idx = PointCloudMeasurement.FindNearestPointOnScreen(
                points, vp, new Vector3(0, 0, 10), 800, 400,
                mouseX: 400, mouseY: 200, radiusPx: 8);

            Assert.Equal(0, idx);
        }

        [Fact]
        public void FindNearest_NoPointInRadius_ReturnsMinusOne()
        {
            var vp = TestViewProj();
            var points = new List<Vector3> { new(0, 0, 0) };

            int idx = PointCloudMeasurement.FindNearestPointOnScreen(
                points, vp, new Vector3(0, 0, 10), 800, 400,
                mouseX: 10, mouseY: 10, radiusPx: 8);

            Assert.Equal(-1, idx);
        }

        [Fact]
        public void FindNearest_OverlappedOnScreen_PrefersPointNearerToCamera()
        {
            var vp = TestViewProj();
            // 두 점 모두 화면 중앙에 투영 — 카메라(z=10)에 가까운 (0,0,5) 가 선택돼야 함
            var points = new List<Vector3> { new(0, 0, 0), new(0, 0, 5) };

            int idx = PointCloudMeasurement.FindNearestPointOnScreen(
                points, vp, new Vector3(0, 0, 10), 800, 400,
                mouseX: 400, mouseY: 200, radiusPx: 8);

            Assert.Equal(1, idx);
        }
    }
}
