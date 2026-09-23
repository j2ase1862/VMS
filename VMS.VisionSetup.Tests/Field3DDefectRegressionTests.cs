using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 현장 Mech-Mind 촬영본 3D 검증(2026-09-23)에서 드러난 결함 회귀 테스트 — 합성 데이터, 환경 무관.
    ///   ① ICP 가 평면에 가까운 부품에서 회전·평행이동을 풀지 못하고 발산 (회전 전치 + 특이값 미정렬)
    ///   ② 격자 점군의 측정 실패 화소(Z=0)가 Height Map 의 3D 점으로 들어가 PlaneFit·Geometry3D 오염
    ///   ③ PlaneFit 의 BGRA 오버레이가 다른 도구 오버레이와 합성될 때 예외로 Run 전체 중단
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class Field3DDefectRegressionTests
    {
        // ── ① ICP ──

        /// <summary>현장 부품과 비슷한 평평한 프레임(300×200px, Z≈1800mm) + 한쪽 모서리 돌기.</summary>
        private static PointCloudData Frame()
        {
            var xyz = new List<float>();
            var rnd = new Random(1);
            for (int i = 0; i < 12000; i++)
            {
                float x = (float)(rnd.NextDouble() * 300), y = (float)(rnd.NextDouble() * 200);
                bool rim = x < 20 || x > 280 || y < 20 || y > 180 || Math.Abs(x - 150) < 10;
                bool bump = x > 250 && y > 150;
                if (!rim && !bump) continue;
                xyz.Add(x + 900); xyz.Add(y + 600); xyz.Add(1800 + (bump ? -15 : 0) + (float)(rnd.NextDouble() * 0.5));
            }
            return PointCloudData.FromArrays(xyz.ToArray());
        }

        [Theory]
        [InlineData(0f, 10f, 5f)]     // 순수 평행이동 — 종전 구현은 이것도 발산했다
        [InlineData(3f, 0f, 0f)]
        [InlineData(5f, 10f, 5f)]
        public void Icp_RecoversInPlaneOffset_OnPlanarPart(float deg, float tx, float ty)
        {
            using var reference = Frame();
            var c = reference.Positions.Aggregate(Vector3.Zero, (a, p) => a + p) / reference.PointCount;
            var offset = Matrix4x4.CreateTranslation(-c) * Matrix4x4.CreateRotationZ(deg * MathF.PI / 180f)
                         * Matrix4x4.CreateTranslation(c + new Vector3(tx, ty, 0));
            using var source = PointCloudData.FromArrays(reference.Positions
                .SelectMany(p => { var q = Vector3.Transform(p, offset); return new[] { q.X, q.Y, q.Z }; }).ToArray());

            var t = TransformUtils.ICP(reference, source, 60, 0.01f, out _);

            // 같은 점끼리의 잔차 — 정합이 맞으면 0 에 가깝다
            double rms = Math.Sqrt(source.Positions.Zip(reference.Positions,
                (s, r) => (double)Vector3.DistanceSquared(Vector3.Transform(s, t), r)).Average());
            Assert.True(rms < 0.5, $"ICP 잔차 {rms:F2} — 알려진 오프셋(rot {deg}°, t {tx},{ty})을 풀지 못함");
            Assert.InRange(TransformUtils.ToEulerAnglesDegrees(t).Z, -deg - 0.3f, -deg + 0.3f);
        }

        // ── ② 측정 실패 화소 ──

        /// <summary>40×30 격자: Z=1800 평면, 왼쪽 절반은 측정 실패(0,0,0 이 아닌 (x,y,0)).</summary>
        private static PointCloudData GridWithHoles()
        {
            int w = 40, h = 30;
            var xyz = new float[w * h * 3];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 3;
                    xyz[i] = x; xyz[i + 1] = y;
                    xyz[i + 2] = x < w / 2 ? 0f : 1800f;   // 카메라 측정 실패 = Z 0
                }
            return PointCloudData.FromArrays(xyz, width: w, height: h);
        }

        [Fact]
        public void HeightMap_InvalidPixels_AreNotPoints()
        {
            using var cloud = GridWithHoles();
            var service = VisionService.Instance;
            var (hm, _, meta) = service.GenerateHeightMap(cloud, 0f, 1700f, 1900f);
            hm.Dispose();

            Assert.Null(meta.GetPoint3D(0, 0));      // 측정 실패
            Assert.NotNull(meta.GetPoint3D(30, 10)); // 정상
        }

        [Fact]
        public void PlaneFit_IgnoresInvalidPixels()
        {
            using var cloud = GridWithHoles();
            var service = VisionService.Instance;
            try
            {
                service.ClearTools();
                var (hm, _, _) = service.GenerateHeightMap(cloud, 0f, 1700f, 1900f);
                service.SetImage(hm);
                hm.Dispose();

                var plane = new PlaneFitTool { FitMethod = PlaneFitMethod.LeastSquares, SampleStride = 1 };
                service.AddTool(plane);
                service.ExecuteAll();
                var r = service.LastExecutionResultsById[plane.Id];

                Assert.True(r.Success, r.Message);
                Assert.Equal(20 * 30, Convert.ToInt32(r.Data["TotalPoints"]));      // 오른쪽 절반만
                Assert.InRange(Convert.ToDouble(r.Data["Flatness"]), 0.0, 0.01);    // 종전 ~1,800mm
            }
            finally
            {
                service.ClearTools();
            }
        }

        [Fact]
        public void Geometry3D_ManualPointOnInvalidPixel_FailsWithHint()
        {
            using var cloud = GridWithHoles();
            var service = VisionService.Instance;
            try
            {
                service.ClearTools();
                var (hm, _, _) = service.GenerateHeightMap(cloud, 0f, 1700f, 1900f);
                service.SetImage(hm);
                hm.Dispose();

                var plane = new PlaneFitTool { FitMethod = PlaneFitMethod.LeastSquares, SampleStride = 1 };
                var geo = new Geometry3DTool { Operation = Geometry3DOperation.PointToPlaneDistance }; // 수동 점 기본값 (0,0)
                service.AddTool(plane); service.AddTool(geo);
                service.AddConnection(plane, geo, ConnectionType.Result);
                service.ExecuteAll();
                var r = service.LastExecutionResultsById[geo.Id];

                // 종전: (0,0,0) 을 점으로 써 "OK, 거리 = 카메라 거리" 를 냈다
                Assert.False(r.Success);
                Assert.Contains("3D 점이 없습니다", r.Message);
            }
            finally
            {
                service.ClearTools();
            }
        }

        // ── ③ 오버레이 합성 ──

        [Fact]
        public void OverlayComposer_MergesBgraLayer_WithoutThrowing()
        {
            using var baseGray = new Mat(20, 20, MatType.CV_8UC1, Scalar.All(50));
            using var first = new Mat(20, 20, MatType.CV_8UC3, Scalar.All(50));
            first.Set(2, 2, new Vec3b(0, 0, 255));                     // 첫 도구의 그래픽
            using var layer = new Mat(20, 20, MatType.CV_8UC4, Scalar.All(0));
            layer.Set(10, 10, new Vec4b(0, 255, 0, 255));              // PlaneFit 식 반투명 레이어

            using var composite = OverlayComposer.CreateComposite(first, baseGray);
            OverlayComposer.Merge(layer, baseGray, composite);          // 종전: Absdiff 예외

            Assert.Equal(new Vec3b(0, 0, 255), composite.At<Vec3b>(2, 2));   // 기존 그래픽 유지
            Assert.Equal(new Vec3b(0, 255, 0), composite.At<Vec3b>(10, 10)); // 레이어 그래픽 반영
            Assert.Equal(new Vec3b(50, 50, 50), composite.At<Vec3b>(15, 15)); // 레이어 밖은 원본(검게 칠하지 않음)
        }

        [Fact]
        public void OverlayComposer_BgraAsFirstLayer_KeepsBaseImage()
        {
            using var baseGray = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(80));
            using var layer = new Mat(10, 10, MatType.CV_8UC4, Scalar.All(0));
            layer.Set(1, 1, new Vec4b(255, 0, 0, 255));

            using var composite = OverlayComposer.CreateComposite(layer, baseGray);

            Assert.Equal(3, composite.Channels());
            Assert.Equal(new Vec3b(80, 80, 80), composite.At<Vec3b>(5, 5));
            Assert.Equal(new Vec3b(255, 0, 0), composite.At<Vec3b>(1, 1));
        }
    }
}
