using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.Camera.Converters;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.PLC.Models.Sequence;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.VisionTools.PointCloud;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 비격자 점군(저장 .vpc 처럼 XY 가 mm)의 3D 결함 2건 — docs/gs/guides/3d_vpc_tool_validation.md 3-1 · 3-2.
    /// <list type="bullet">
    ///   <item>3-1: [Height Map 생성] 이 비격자 점군에서 아무 일도 하지 않았다 (뷰모델이 조용히 반환).</item>
    ///   <item>3-2: MaskCrop 이 비격자 점군 XY(mm)를 마스크 화소와 1:1 로 보아 엉뚱한 귀퉁이만 남겼다.</item>
    /// </list>
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class UnorganizedCloudHeightMapAndMaskCropTests
    {
        /// <summary>
        /// 리포트의 합성 스캔과 같은 꼴 — X −150~140mm, Y −20~230mm, 음수 원점, 비격자.
        /// 마스크 화소와 1:1 로 보면 0~W, 0~H mm 귀퉁이만 덮는 좌표다.
        /// </summary>
        private static PointCloudData MakeMetricScan(int count = 12000, int seed = 7)
        {
            var rand = new Random(seed);
            var xyz = new float[count * 3];
            for (int i = 0; i < count; i++)
            {
                float x = -150f + (float)rand.NextDouble() * 290f;
                float y = -20f + (float)rand.NextDouble() * 250f;
                xyz[i * 3] = x;
                xyz[i * 3 + 1] = y;
                xyz[i * 3 + 2] = 40f + (x > 0 ? 5f : 0f);
            }
            var cloud = PointCloudData.FromArrays(xyz, null, "scan");
            Assert.False(cloud.IsOrganized);
            return cloud;
        }

        private static Mat MakeLeftMask(int w, int h, int leftCols)
        {
            var mask = new Mat(h, w, MatType.CV_8UC1, Scalar.Black);
            using var left = new Mat(mask, new Rect(0, 0, leftCols, h));
            left.SetTo(255);
            return mask;
        }

        [Fact]
        public void Ortho_grid_puts_every_point_in_a_filled_cell()
        {
            var cloud = MakeMetricScan();
            var (map, pixelTo3D, w, h) = PointCloudConverter.OrthographicToDepthMap32F(cloud, 0f, out var grid);
            using (map)
            {
                Assert.Equal(w, grid.Width);
                Assert.Equal(h, grid.Height);
                Assert.True(grid.CellSize > 0f);

                // 비닝과 되짚기가 같은 규칙인지 — 어느 점이든 자기 칸은 비어 있지 않다
                foreach (var p in cloud.Positions)
                {
                    Assert.True(grid.TryGetCell(p.X, p.Y, out int u, out int v));
                    Assert.NotNull(pixelTo3D[v * w + u]);
                }

                // 격자 밖 좌표는 칸이 없다 (예전 비닝처럼 가장자리로 끌어오지 않는다)
                Assert.False(grid.TryGetCell(grid.OriginX - grid.CellSize, grid.OriginY, out _, out _));
                Assert.False(grid.TryGetCell(grid.OriginX + (w + 1) * grid.CellSize, grid.OriginY, out _, out _));
            }
        }

        /// <summary>3-2 — Height Map 왼쪽 절반 마스크로 자르면 점군도 X 기준 왼쪽 절반이 남아야 한다.</summary>
        [Fact]
        public void MaskCrop_on_metric_unorganized_cloud_follows_the_height_map_grid()
        {
            var service = VisionService.Instance;
            var cloud = MakeMetricScan();
            service.CurrentPointCloud = cloud;
            var (hm8, _, meta) = service.GenerateHeightMap(cloud, 0f, 40f, 45f);
            hm8.Dispose();
            var grid = Assert.IsType<OrthographicGrid>(meta.Orthographic);

            int half = grid.Width / 2;
            using var mask = MakeLeftMask(grid.Width, grid.Height, half);
            var result = new PointCloudMaskCropTool().Execute(mask);

            int expected = cloud.Positions.Count(p => grid.TryGetCell(p.X, p.Y, out int u, out _) && u < half);
            Assert.True(result.Success, result.Message);
            Assert.Contains("ortho grid", result.Message);
            Assert.Equal(expected, result.Data["OutputPoints"]);
            // 리포트에서는 18.6% 만 남았다 — 절반 가까이 남아야 한다
            Assert.InRange((double)result.Data["KeptRatio"], 0.4, 0.6);

            float cutX = grid.OriginX + half * grid.CellSize;
            Assert.All(service.CurrentPointCloud!.Positions, p => Assert.True(p.X < cutX + 1e-3f));
        }

        /// <summary>
        /// 마스크가 정사투영 맵과 크기가 다르면(그 맵에서 나온 마스크가 아니면) 예전처럼 1:1 —
        /// 격자 점군을 잘라 비격자가 된 점군(XY = 화소)에 마스크를 다시 거는 흐름을 깨지 않는다.
        /// </summary>
        [Fact]
        public void MaskCrop_keeps_pixel_space_mapping_when_mask_is_not_from_the_ortho_map()
        {
            var service = VisionService.Instance;
            var (hm8, _, meta) = service.GenerateHeightMap(MakeMetricScan(), 0f, 40f, 45f);
            hm8.Dispose();
            Assert.NotNull(meta.Orthographic);

            // XY = 화소인 비격자 점군 10×10 (격자 점군 크롭 결과와 같은 꼴)
            var xyz = new List<float>();
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                    xyz.AddRange(new[] { (float)x, (float)y, 10f });
            service.CurrentPointCloud = PointCloudData.FromArrays(xyz.ToArray(), null, "pixel-space");

            using var mask = MakeLeftMask(10, 10, 5);
            var result = new PointCloudMaskCropTool().Execute(mask);

            Assert.True(result.Success, result.Message);
            Assert.DoesNotContain("ortho grid", result.Message);
            Assert.Equal(50, result.Data["OutputPoints"]);
            Assert.All(service.CurrentPointCloud!.Positions, p => Assert.True(p.X < 5f));
        }

        /// <summary>3-1 — 버튼이 비격자 점군에서도 Height Map 을 만들어야 한다 (예전에는 조용히 반환).</summary>
        [Fact]
        public void GenerateHeightMap_command_builds_ortho_map_for_unorganized_cloud()
        {
            var vm = CreateViewModel(new FakeDialogService());
            vm.CurrentPointCloud = MakeMetricScan();
            Assert.True(vm.GenerateHeightMapCommand.CanExecute(null));

            vm.GenerateHeightMapCommand.Execute(null);

            var meta = Assert.IsType<HeightMapMetadata>(vm.CurrentHeightMapMetadata);
            Assert.NotNull(meta.Orthographic);
            Assert.NotNull(vm.CurrentImage); // 2D 영상이 없으므로 Height Map 이 작업 이미지가 된다
            Assert.StartsWith("Height Map 생성 완료", vm.StatusMessage);
        }

        /// <summary>3-1 — 저장 .vpc(비격자)를 열면 격자 점군처럼 Height Map 까지 바로 생겨야 한다.</summary>
        [Fact]
        public void Opening_unorganized_vpc_builds_height_map()
        {
            string path = Path.Combine(Path.GetTempPath(), $"unorganized_{Guid.NewGuid():N}.vpc");
            try
            {
                MakeMetricScan().SaveToFile(path);
                var vm = CreateViewModel(new FakeDialogService { OpenFilePath = path });

                vm.LoadPointCloudCommand.Execute(null);

                Assert.NotNull(vm.CurrentPointCloud);
                Assert.False(vm.CurrentPointCloud!.IsOrganized);
                Assert.NotNull(vm.CurrentHeightMapMetadata?.Orthographic);
                Assert.NotNull(VisionService.Instance.CurrentHeightMapMetadata?.Orthographic);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static MainViewModel CreateViewModel(FakeDialogService dialogs)
        {
            VisionService.Instance.CurrentHeightMapMetadata = null;
            return new MainViewModel(
                VisionService.Instance,
                new FakeRecipeService(),
                new FakeCameraService(),
                dialogs,
                () => { });
        }

        #region Fakes

        private sealed class FakeRecipeService : IRecipeService
        {
            public Recipe? CurrentRecipe { get; set; }
            public string RecipeFolderPath => string.Empty;
            public event EventHandler<Recipe?>? CurrentRecipeChanged { add { } remove { } }
            public Recipe? LoadRecipe(string filePath) => null;
            public Recipe? ReadRecipeFile(string filePath) => null;
            public bool SaveRecipe(Recipe recipe, string? filePath = null) => true;
            public bool SaveCurrentRecipe(string? filePath = null) => true;
            public Recipe CreateNewRecipe(string? name = null) => new();
            public bool DeleteRecipe(string filePath) => true;
            public List<RecipeInfo> GetRecipeList() => new();
            public bool ExportRecipe(Recipe recipe, string exportPath) => true;
            public Recipe? ImportRecipe(string importPath) => null;
            public Recipe? DuplicateRecipe(string filePath) => null;
            public bool RenameRecipe(string filePath, string newName) => false;
            public InspectionStep? AddStep(Recipe? recipe = null, string? cameraId = null) => null;
            public void AddStep(Recipe recipe, InspectionStep step) { }
            public bool RemoveStep(Recipe? recipe, string stepId) => false;
            public bool MoveStep(Recipe? recipe, string stepId, int newSequence) => false;
            public InspectionStep? DuplicateStep(Recipe recipe, string stepId) => null;
            public InspectionStep? FindStepByRobotNode(Recipe? recipe, int nodeIndex, string? cameraId = null) => null;
            public bool AddToolToStep(InspectionStep step, ToolConfig tool) => false;
            public bool AddToolToStep(Recipe recipe, string stepId, ToolConfig tool) => false;
            public bool AddToolToStep(InspectionStep step, VisionToolBase tool) => false;
            public bool RemoveToolFromStep(InspectionStep step, string toolId) => false;
            public bool RemoveToolFromStep(Recipe recipe, string stepId, string toolId) => false;
            public List<VisionToolBase> GetToolsFromRecipe(Recipe? recipe = null) => new();
            public List<VisionToolBase> GetToolsFromStep(InspectionStep step) => new();
            public void SetCurrentRecipe(Recipe? recipe) => CurrentRecipe = recipe;
            public void CloseCurrentRecipe() => CurrentRecipe = null;
        }

        private sealed class FakeCameraService : ICameraService
        {
            public List<CameraInfo> LoadCameraRegistry() => new();
            public bool SaveCameraRegistry(List<CameraInfo>? cameras = null) => true;
            public bool AddCamera(CameraInfo camera) => true;
            public bool UpdateCamera(CameraInfo camera) => true;
            public bool RemoveCamera(string id) => true;
            public CameraInfo? GetCamera(string id) => null;
            public List<CameraInfo> GetAllCameras() => new();
            public List<CameraInfo> GetEnabledCameras() => new();
            public CameraInfo CreateNewCamera() => new();
            public string GetAppDataFolderPath() => Path.GetTempPath();
        }

        private sealed class FakeDialogService : IDialogService
        {
            /// <summary>[3D 데이터 열기] 가 고를 파일.</summary>
            public string? OpenFilePath { get; init; }

            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public string? ShowOpenFileDialog(string title, string filter) => OpenFilePath;
            public string[]? ShowOpenFilesDialog(string title, string filter) => null;
            public string? ShowFolderBrowserDialog(string description) => null;
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null) => null;
            public string? ShowRenameDialog(string currentName) => null;
            public void ShowCameraManagerDialog() { }
            public Recipe? ShowRecipeManagerDialog() => null;
            public void ShowSequenceEditorDialog(IEnumerable<SequenceDeviceEntry>? extraDevices = null) { }
            public void ShowCalibrationManagerDialog() { }
            public RecipeTemplate? ShowTemplateGalleryDialog() => null;
            public Dictionary<string, string>? ShowCameraRemapDialog(
                IReadOnlyList<(string oldId, int stepCount)> unregistered,
                IReadOnlyList<CameraInfo> cameras) => null;
            public Mat? ShowTrainMaskEditorDialog(Mat templateImage, Mat? existingMask,
                double cannyLow = 50, double cannyHigh = 150) => null;
        }

        #endregion
    }
}
