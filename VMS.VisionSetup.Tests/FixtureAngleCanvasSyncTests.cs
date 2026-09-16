using System;
using System.Collections.Generic;
using OpenCvSharp;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.PLC.Models.Sequence;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// Fixture 가 전달한 각도가 <b>캔버스 도형에도</b> 반영되는지.
    ///
    /// <para>실행이 보는 영역(ROI/ROIAngle)과 화면에 그려진 사각형은 별개 객체다. 예전 동기화는
    /// 중심·크기만 맞추고 각도는 일부러 유지해서("Angle 유지"), Fixture 가 각도를 전달하면
    /// <b>사각형은 제자리로 옮겨 가면서 기울기만 예전 각도로 남았다</b> — 화면이 그럴듯하게
    /// 따라가니 사용자는 그 사각형을 믿는데 실제 검사 영역은 다른 각도다.</para>
    ///
    /// <para>사용자가 직접 ROI 를 편집한 경우에는 예전 그대로 각도를 유지해야 한다 —
    /// 그리려고 잡아 둔 기울기를 텍스트 편집이 지우면 안 된다.</para>
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class FixtureAngleCanvasSyncTests
    {
        private const double Cx = 250, Cy = 200, W = 40, H = 20;

        private static (MainViewModel vm, BlobTool tool, RectangleAffineROI shape) Setup(double shapeAngle)
        {
            var shape = new RectangleAffineROI(Cx, Cy, W, H, shapeAngle);
            var tool = new BlobTool
            {
                UseROI = true,
                ROI = new Rect((int)(Cx - W / 2), (int)(Cy - H / 2), (int)W, (int)H),
                ROIAngle = shapeAngle,
                ROICenterX = Cx,
                ROICenterY = Cy,
                AssociatedROIShape = shape
            };

            var vm = CreateViewModel();
            vm.SelectedTool = new ToolItem { Name = tool.Name, ToolType = tool.ToolType, VisionTool = tool };
            return (vm, tool, shape);
        }

        /// <summary>
        /// Fixture 가 각도를 채우면 캔버스 사각형도 같이 기운다.
        /// </summary>
        [Fact]
        public void FixtureAngle_RotatesCanvasShape()
        {
            var (vm, tool, shape) = Setup(shapeAngle: 0);
            try
            {
                var source = new Dictionary<string, object>
                {
                    ["CenterX"] = 200.0, ["CenterY"] = 200.0, ["Angle"] = 0.0
                };

                tool.IsFixtureTransformActive = true;
                ToolSourceInjector.ApplyFixtureTransform(tool, source);   // 기준 스냅샷
                source["Angle"] = 30.0;
                ToolSourceInjector.ApplyFixtureTransform(tool, source);
                tool.IsFixtureTransformActive = false;

                Assert.Equal(30, tool.ROIAngle, 3);
                Assert.Equal(30, shape.Angle, 3);      // ← 예전에는 0 그대로였다
                Assert.Equal(tool.ROICenterX, shape.CenterX, 3);
                Assert.Equal(tool.ROICenterY, shape.CenterY, 3);
            }
            finally
            {
                vm.SelectedTool = null;
            }
        }

        /// <summary>
        /// 사용자가 그려 둔 기울기는 텍스트 편집으로 ROI 를 옮겨도 유지된다 (기존 동작).
        /// </summary>
        [Fact]
        public void UserRoiEdit_KeepsCanvasShapeAngle()
        {
            var (vm, tool, shape) = Setup(shapeAngle: 25);
            try
            {
                Assert.False(tool.HasFixtureAngle);

                tool.ROIX += 40;

                Assert.Equal(25, shape.Angle, 3);                 // 각도는 그대로
                Assert.Equal(Cx + 40, shape.CenterX, 3);          // 위치만 이동
                Assert.Equal(Cx + 40, tool.ROICenterX, 3);
            }
            finally
            {
                vm.SelectedTool = null;
            }
        }

        /// <summary>
        /// Fixture 가 각도를 준 뒤에도 회전 중심은 정밀값이 유지돼야 한다 —
        /// 정수 사각형에서 되계산한 값으로 덮으면 실행마다 반픽셀씩 밀린다.
        /// </summary>
        [Fact]
        public void FixtureAngle_KeepsPreciseRotationCenter()
        {
            var (vm, tool, _) = Setup(shapeAngle: 0);
            try
            {
                var source = new Dictionary<string, object>
                {
                    ["CenterX"] = 200.0, ["CenterY"] = 200.0, ["Angle"] = 0.0
                };

                tool.IsFixtureTransformActive = true;
                ToolSourceInjector.ApplyFixtureTransform(tool, source);
                source["Angle"] = 30.0;
                ToolSourceInjector.ApplyFixtureTransform(tool, source);
                tool.IsFixtureTransformActive = false;

                // 기준(200,200) 기준 상대위치 (50,0) 을 30° 회전 → (+43.3013, +25.0)
                Assert.Equal(200 + 50 * Math.Cos(Math.PI / 6), tool.ROICenterX, 3);
                Assert.Equal(200 + 50 * Math.Sin(Math.PI / 6), tool.ROICenterY, 3);
            }
            finally
            {
                vm.SelectedTool = null;
            }
        }

        private static MainViewModel CreateViewModel()
        {
            return new MainViewModel(
                VisionService.Instance,
                new FakeRecipeService(),
                new FakeCameraService(),
                new FakeDialogService(),
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
            public string GetAppDataFolderPath() => System.IO.Path.GetTempPath();
        }

        private sealed class FakeDialogService : IDialogService
        {
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public string? ShowOpenFileDialog(string title, string filter) => null;
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
