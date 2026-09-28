using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VMS.Camera.Models;
using VMS.Interfaces;
using VMS.Services;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;
using HeightSlicingSettings = VMS.VisionSetup.Models.HeightSlicingSettings;

namespace VMS.Tests.Services
{
    /// <summary>
    /// VMS 본체 검사 엔진(수동 검사·AUTO RUN)의 3D 레시피 실행 (v1.42.7).
    /// v1.42.6 까지는 카메라 2D 영상만 넘겨 3D 도구가 전부 NG 였다 — Height Slicer "CV_8UC3 미지원",
    /// Mask Crop "No point cloud", Plane Fit "HeightMapMetadata 필요".
    /// 합성 격자 점군: 바닥 Z=200, 부품 2개(6×6 화소) Z=100 → 부품 높이 100mm.
    /// </summary>
    [Collection(InspectionServiceStaticsCollection.Name)]
    public class InspectionService3DTests : IDisposable
    {
        private readonly InspectionService _service = new();

        public void Dispose()
        {
            _service.ClearCache();
            _service.SetRecipeContext(null);
            VisionService.Instance.Clear3DInput(null);
        }

        private static PointCloudData Scene()
        {
            const int w = 40, h = 30;
            var xyz = new float[w * h * 3];
            int i = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool part = y >= 10 && y < 16 && ((x >= 5 && x < 11) || (x >= 25 && x < 31));
                    xyz[i++] = x; xyz[i++] = y; xyz[i++] = part ? 100f : 200f;
                }
            return PointCloudData.FromArrays(xyz, null, "scene", w, h);
        }

        /// <summary>Height Slicer → Mask Crop → Cluster(개수 2 판정) + Plane Fit(바닥) → 3D Geometry(높이 100±1).</summary>
        private static InspectionStep Step()
        {
            var slicer = new HeightSlicerTool { Name = "Slicer", MinZ = 90, MaxZ = 110 };
            var crop = new PointCloudMaskCropTool { Name = "Crop" };
            var cluster = new PointCloudClusterTool
            {
                Name = "Cluster", Tolerance = 1.5f, MinPoints = 5, DrawOverlay = false,
                OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal,
                EnableJudgment = true, CountMode = PointCloudClusterTool.ClusterCountMode.Equal, ExpectedCount = 2,
            };
            var plane = new PlaneFitTool { Name = "Floor", SampleStride = 1, RansacThreshold = 0.5, UseROI = true, ROI = new Rect(0, 20, 40, 10) };
            var height = new Geometry3DTool
            {
                Name = "Height", Operation = Geometry3DOperation.PointToPlaneDistance, UseManualPoints = false,
                EnableJudgment = true, ExpectedValue = 100, ToleranceMinus = 1, TolerancePlus = 1,
            };

            var configs = new VMS.VisionSetup.Models.VisionToolBase[] { slicer, crop, cluster, plane, height }
                .Select((t, i) => { var c = ToolSerializer.SerializeTool(t); c.Sequence = i; return c; }).ToList();
            void Link(int src, int dst, string type) =>
                configs[dst].Connections.Add(new ToolConnectionConfig { SourceToolId = configs[src].Id, ConnectionType = type });
            Link(0, 1, "Image");
            Link(1, 2, "Result");
            Link(3, 4, "Result");
            Link(2, 4, "Result");

            return new InspectionStep { Id = "insp3d_" + Guid.NewGuid().ToString("N"), Name = "3D", Tools = configs };
        }

        private static ToolInspectionResult Tool(StepInspectionResult r, string name) => r.ToolResults.First(t => t.ToolName == name);

        private void UseRecipeSlicing() =>
            _service.SetRecipeContext(new Recipe
            {
                HeightSlicing = new HeightSlicingSettings { HeightLowerLimit = 0, HeightUpperLimit = 300, DepthRangeMin = 0, DepthRangeMax = 300 },
            });

        [Fact]
        public async Task ExecuteStep_WithPointCloud_Runs3DRecipe_LikeVisionSetup()
        {
            UseRecipeSlicing();
            using var cloud = Scene();
            using var image = new Mat(30, 40, MatType.CV_8UC3, Scalar.All(80));   // 카메라 2D 영상 자리

            var r = await _service.ExecuteStepAsync(Step(), image, cloud);

            Assert.True(r.Success, string.Join(" / ", r.ToolResults.Select(t => $"{t.ToolName}:{t.Message}")));
            Assert.Equal(2, Convert.ToInt32(Tool(r, "Cluster").Data["ClusterCount"]));
            Assert.Equal(100.0, Convert.ToDouble(Tool(r, "Height").Data["Distance3D"]), 1);
            Assert.True((bool)Tool(r, "Height").Data["JudgmentPass"]);

            // 스텝이 끝나면 3D 입력을 해제한다 — 다음 2D 스텝이 낡은 점군·높이맵을 보지 않게
            Assert.Null(VisionService.Instance.CurrentPointCloud);
            Assert.Null(VisionService.Instance.CurrentHeightMapMetadata);
            Assert.Null(VisionService.Instance.CurrentDepthMap32F);
            Assert.Equal(40 * 30, cloud.PointCount);   // 호출자 점군은 건드리지 않는다
        }

        [Fact]
        public async Task ExecuteStep_Repeated_StartsFromOriginalCloudEachTime()
        {
            UseRecipeSlicing();
            using var cloud = Scene();
            using var image = new Mat(30, 40, MatType.CV_8UC3, Scalar.All(80));

            var first = await _service.ExecuteStepAsync(Step(), image, cloud);
            var second = await _service.ExecuteStepAsync(Step(), image, cloud);

            // 앞 실행의 크롭 결과가 다음 실행 입력이 되면 Mask Crop 입력 점 수가 줄어든다
            Assert.Equal(Tool(first, "Crop").Data["InputPoints"], Tool(second, "Crop").Data["InputPoints"]);
            Assert.True(second.Success);
        }

        [Fact]
        public async Task ExecuteStep_WithoutPointCloud_3DToolsFail_NotSilentlyPass()
        {
            UseRecipeSlicing();
            using var image = new Mat(30, 40, MatType.CV_8UC3, Scalar.All(80));

            var r = await _service.ExecuteStepAsync(Step(), image);

            Assert.False(r.Success);
            Assert.False(Tool(r, "Crop").Success);
        }

        [Fact]
        public async Task ExecuteStep_NoRecipeSlicing_UsesCloudZRange()
        {
            // 레시피에 Height Slicing 이 없으면 점군의 유효 Z 범위로 깊이맵을 만든다
            using var cloud = Scene();
            using var image = new Mat(30, 40, MatType.CV_8UC3, Scalar.All(80));

            var r = await _service.ExecuteStepAsync(Step(), image, cloud);

            Assert.True(r.Success, string.Join(" / ", r.ToolResults.Select(t => $"{t.ToolName}:{t.Message}")));
        }
    }
}
