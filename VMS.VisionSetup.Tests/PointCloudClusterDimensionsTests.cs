using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// PointCloudClusterTool 치수 메트릭 검증:
    /// - 축 정렬 크기 SizeX/Y/Z (XyScale은 X/Y에만 적용, Z는 mm 그대로)
    /// - XY 평면 OBB Length/Width/Angle (회전 놓인 부품)
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class PointCloudClusterDimensionsTests
    {
        /// <summary>간격 1의 격자 블록 점군 (X 0..sx-1, Y 0..sy-1), Z는 zBase..zBase+sz-1 반복.</summary>
        private static PointCloudData MakeBlock(int sx, int sy, int sz = 1, float zBase = 5f)
        {
            var xyz = new float[sx * sy * sz * 3];
            int i = 0;
            for (int z = 0; z < sz; z++)
                for (int y = 0; y < sy; y++)
                    for (int x = 0; x < sx; x++)
                    {
                        xyz[i++] = x;
                        xyz[i++] = y;
                        xyz[i++] = zBase + z;
                    }
            return PointCloudData.FromArrays(xyz);
        }

        private static VisionResult RunCluster(PointCloudClusterTool tool)
        {
            using var dummy = new Mat(4, 4, MatType.CV_8UC1, Scalar.Black);
            return tool.Execute(dummy);
        }

        [Fact]
        public void Execute_ReportsAxisAlignedSizes()
        {
            VisionService.Instance.CurrentPointCloud = MakeBlock(10, 5, 3);

            var tool = new PointCloudClusterTool
            {
                Tolerance = 2f,
                MinPoints = 10,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal
            };
            var result = RunCluster(tool);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data["ClusterCount"]);
            Assert.Equal(9f, (float)result.Data["Cluster0_SizeX"], 1);
            Assert.Equal(4f, (float)result.Data["Cluster0_SizeY"], 1);
            Assert.Equal(2f, (float)result.Data["Cluster0_SizeZ"], 1);
        }

        [Fact]
        public void Execute_XyScale_AppliesToXyOnly()
        {
            VisionService.Instance.CurrentPointCloud = MakeBlock(10, 5, 3);

            var tool = new PointCloudClusterTool
            {
                Tolerance = 2f,
                MinPoints = 10,
                XyScale = 0.5f,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal
            };
            var result = RunCluster(tool);

            Assert.Equal(4.5f, (float)result.Data["Cluster0_SizeX"], 1);
            Assert.Equal(2f, (float)result.Data["Cluster0_SizeY"], 1);
            // Z는 원래 mm — 배율 미적용
            Assert.Equal(2f, (float)result.Data["Cluster0_SizeZ"], 1);
            // OBB 길이/폭에도 적용
            Assert.Equal(4.5f, (float)result.Data["Cluster0_Length"], 1);
            Assert.Equal(2f, (float)result.Data["Cluster0_Width"], 1);
        }

        [Fact]
        public void Execute_ObbLengthWidth_AxisAlignedBlock()
        {
            VisionService.Instance.CurrentPointCloud = MakeBlock(10, 5);

            var tool = new PointCloudClusterTool
            {
                Tolerance = 2f,
                MinPoints = 10,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal
            };
            var result = RunCluster(tool);

            // 긴 변 = X 방향 9, 짧은 변 = Y 방향 4
            Assert.Equal(9f, (float)result.Data["Cluster0_Length"], 0);
            Assert.Equal(4f, (float)result.Data["Cluster0_Width"], 0);
            // 각도는 [-90, 90] 정규화 범위 내 (축 정렬 사각형은 OpenCV 규약상 0/±90 경계값 가능)
            float angle = (float)result.Data["Cluster0_Angle"];
            Assert.InRange(angle, -90f, 90f);
        }

        [Fact]
        public void GetAvailableResultKeys_IncludesDimensionKeys()
        {
            var keys = new PointCloudClusterTool().GetAvailableResultKeys();

            Assert.Contains("Cluster0_SizeX", keys);
            Assert.Contains("Cluster0_SizeZ", keys);
            Assert.Contains("Cluster0_Length", keys);
            Assert.Contains("Cluster0_Width", keys);
            Assert.Contains("Cluster0_Angle", keys);
        }
    }
}
