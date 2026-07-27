using System;
using System.Collections.Generic;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Geometry3DTool의 Result 연결 소스 주입 검증:
    /// - PointCloudClusterTool → 클러스터 중심점(mm) 추출, 툴 1개 연결로 A/B 두 클러스터 간 거리
    /// - PlaneFitTool → 평면 추출, 클러스터 점과 조합한 점-평면 거리
    /// - ExecuteAll 경로(VisionService 주입) 및 ExtractGeometry3D 단위 동작
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class Geometry3DClusterSourceTests
    {
        /// <summary>4x4 블록 2개 (중심 (1.5, 1.5, 5)와 (1.5+offsetX, 1.5, 5)) — unorganized.</summary>
        private static PointCloudData MakeTwoBlocks(float offsetX = 20f)
        {
            var xyz = new List<float>();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) { xyz.Add(x); xyz.Add(y); xyz.Add(5f); }
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) { xyz.Add(x + offsetX); xyz.Add(y); xyz.Add(5f); }
            return PointCloudData.FromArrays(xyz.ToArray());
        }

        [Fact]
        public void ExecuteAll_ClusterToGeometry3D_MeasuresDistanceBetweenTwoClusters()
        {
            var service = VisionService.Instance;
            service.ClearTools();
            using var image = new Mat(8, 8, MatType.CV_8UC3, new Scalar(255, 255, 255));
            service.SetImage(image);
            service.CurrentPointCloud = MakeTwoBlocks();

            var cluster = new PointCloudClusterTool
            {
                Name = "Cluster",
                Tolerance = 2f,
                MinPoints = 5,
                DrawOverlay = false,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal
            };
            // 클러스터 툴 1개 연결 — A=0, B=1(기본)의 두 클러스터 중심 간 거리
            var geo = new Geometry3DTool
            {
                Name = "Geo3D",
                Operation = Geometry3DOperation.PointToPointDistance,
                UseManualPoints = false
            };
            service.AddTool(cluster);
            service.AddTool(geo);
            service.AddConnection(cluster, geo, ConnectionType.Result);

            try
            {
                var results = service.ExecuteAll();
                Assert.True(results[0].Success, results[0].Message);
                Assert.True(results[1].Success, results[1].Message);
                Assert.Equal(20.0, Convert.ToDouble(results[1].Data["Distance3D"]), 1);
                Assert.Equal(2, geo.SourceGeometries.Count); // Finalize가 두 번째 점 추가
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }

        [Fact]
        public void Execute_PointToPlane_FromClusterAndPlaneSources()
        {
            var geo = new Geometry3DTool
            {
                Operation = Geometry3DOperation.PointToPlaneDistance,
                UseManualPoints = false
            };

            // 평면을 먼저 연결해도 (소스 순서 무관) 점 소스를 찾아야 함
            var planeResult = new VisionResult { Success = true };
            planeResult.Data["PlaneA"] = 0.0;
            planeResult.Data["PlaneB"] = 0.0;
            planeResult.Data["PlaneC"] = 1.0;
            planeResult.Data["PlaneD"] = 0.0; // z = 0 평면

            var clusterResult = new VisionResult { Success = true };
            clusterResult.Data["Cluster0_CenterXMm"] = 1.5f;
            clusterResult.Data["Cluster0_CenterYMm"] = 1.5f;
            clusterResult.Data["Cluster0_CenterZ"] = 5f;

            geo.ClearSourceGeometries();
            geo.CollectSourceGeometry("p1", "Plane", "PlaneFitTool", planeResult);
            geo.CollectSourceGeometry("c1", "Cluster", "PointCloudClusterTool", clusterResult);
            geo.FinalizeSourceGeometries();

            using var dummy = new Mat(4, 4, MatType.CV_8UC1, Scalar.Black);
            var result = geo.Execute(dummy);

            Assert.True(result.Success, result.Message);
            Assert.Equal(5.0, Convert.ToDouble(result.Data["Distance3D"]), 3);
        }

        [Fact]
        public void ExtractGeometry3D_Cluster_UsesRequestedIndex()
        {
            var r = new VisionResult { Success = true };
            r.Data["Cluster0_CenterXMm"] = 1f;
            r.Data["Cluster0_CenterYMm"] = 2f;
            r.Data["Cluster0_CenterZ"] = 3f;
            r.Data["Cluster1_CenterXMm"] = 10f;
            r.Data["Cluster1_CenterYMm"] = 20f;
            r.Data["Cluster1_CenterZ"] = 30f;

            var g0 = Geometry3DTool.ExtractGeometry3D("id", "Cluster", "PointCloudClusterTool", r, 0);
            var g1 = Geometry3DTool.ExtractGeometry3D("id", "Cluster", "PointCloudClusterTool", r, 1);
            var g2 = Geometry3DTool.ExtractGeometry3D("id", "Cluster", "PointCloudClusterTool", r, 2);

            Assert.NotNull(g0);
            Assert.True(g0!.HasPoint);
            Assert.Equal(1f, g0.Point.X, 3);
            Assert.NotNull(g1);
            Assert.Equal(30f, g1!.Point.Z, 3);
            Assert.Null(g2); // 없는 클러스터 번호
        }

        [Fact]
        public void ExtractGeometry3D_FailedSource_ReturnsNull()
        {
            var r = new VisionResult { Success = false };
            r.Data["Cluster0_CenterXMm"] = 1f;
            r.Data["Cluster0_CenterYMm"] = 2f;
            r.Data["Cluster0_CenterZ"] = 3f;

            Assert.Null(Geometry3DTool.ExtractGeometry3D("id", "Cluster", "PointCloudClusterTool", r));
        }
    }
}
