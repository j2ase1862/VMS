using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Run Selected(ExecuteTool) 의 3D 업스트림 실행이 Run All(ExecuteAll) 과 같은 규칙을 따르는지 검증:
    /// - 업스트림으로 도는 HeightSlicer 도 mm 깊이맵(CV_32FC1)을 받는다 (화면용 8bit Height Map 아님)
    /// - Result 연결의 앞 도구가 실패하면 대상 도구를 건너뛴다
    /// 현장 재현(2026-09-28): 스텝 [Slicer → Mask Crop → Cluster] 에서 Cluster 를 선택 실행하면 Slicer 가 8bit 값을
    /// mm 범위로 잘라 빈 마스크 → Crop 실패 → Cluster 가 원본 314만 점을 UI 스레드에서 군집화하다 멈췄다.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class VisionServiceRunSelected3DTests
    {
        /// <summary>8×8 격자 점군 — 왼쪽 4열은 Z=100(부품), 오른쪽 4열은 Z=200(바닥).</summary>
        private static PointCloudData MakeTwoLevelCloud()
        {
            const int w = 8, h = 8;
            var xyz = new float[w * h * 3];
            int i = 0;
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    xyz[i++] = col;
                    xyz[i++] = row;
                    xyz[i++] = col < 4 ? 100f : 200f;
                }
            return PointCloudData.FromArrays(xyz, null, "two-level", w, h);
        }

        private static (HeightSlicerTool slicer, PointCloudMaskCropTool crop, PointCloudClusterTool cluster) SetUpPipeline(
            VisionService service, float sliceMin, float sliceMax)
        {
            service.ClearTools();
            service.CurrentPointCloud = MakeTwoLevelCloud();
            // .vpc 열기와 같은 순서: 깊이맵(mm) + 화면용 8bit Height Map
            var (heightMap8U, depth, _) = service.GenerateHeightMap(service.CurrentPointCloud, 0f, 0f, 300f);
            depth.Dispose();
            service.SetImage(heightMap8U);
            heightMap8U.Dispose();

            var slicer = new HeightSlicerTool { Name = "Slicer", MinZ = sliceMin, MaxZ = sliceMax };
            var crop = new PointCloudMaskCropTool { Name = "Crop" };
            var cluster = new PointCloudClusterTool
            {
                Name = "Cluster", Tolerance = 1.5f, MinPoints = 5, DrawOverlay = false,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal,
            };
            service.AddTool(slicer);
            service.AddTool(crop);
            service.AddTool(cluster);
            service.AddConnection(slicer, crop, ConnectionType.Image);
            service.AddConnection(crop, cluster, ConnectionType.Result);
            return (slicer, crop, cluster);
        }

        [Fact]
        public void ExecuteTool_UpstreamHeightSlicer_UsesDepthMap_SoCropNarrowsCloud()
        {
            var service = VisionService.Instance;
            var (_, crop, cluster) = SetUpPipeline(service, 90f, 110f);
            try
            {
                var result = service.ExecuteTool(cluster);

                Assert.True(crop.LastResult!.Success, crop.LastResult.Message);
                Assert.Equal(32, crop.LastResult.Data["OutputPoints"]);
                Assert.True(result.Success, result.Message);
                Assert.Equal(32, result.Data["LargestPoints"]);   // 원본 64점이 아니라 잘린 32점
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }

        [Fact]
        public void ExecuteTool_ResultSourceFailed_SkipsTargetLikeExecuteAll()
        {
            var service = VisionService.Instance;
            var (_, crop, cluster) = SetUpPipeline(service, 500f, 600f);   // 높이대에 점 없음 → Crop 실패
            try
            {
                var result = service.ExecuteTool(cluster);

                Assert.False(crop.LastResult!.Success);
                Assert.False(result.Success);
                Assert.Contains("건너뜀", result.Message);
                Assert.False(result.Data.ContainsKey("ClusterCount"));   // 군집화가 돌지 않았다
                Assert.Same(result, cluster.LastResult);

                var all = service.ExecuteAll();
                Assert.Equal(result.Message, all[2].Message);   // Run All 과 같은 판정
            }
            finally
            {
                service.ClearTools();
                service.CurrentPointCloud = null;
            }
        }
    }
}
