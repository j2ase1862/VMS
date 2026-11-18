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

        // ── VS-07: Show ROI 는 보기만 ─────────────────────────────────────────────

        /// <summary>도구 값 스냅샷 — Show ROI 전후 비교용.</summary>
        private static (Rect roi, double angle, double cx, double cy, bool hasBase, Rect baseRoi,
            double baseAngle, double refX, double refY, double refAngle, bool hasAngle) Snapshot(BlobTool t) =>
            (t.ROI, t.ROIAngle, t.ROICenterX, t.ROICenterY, t.HasFixtureBaseROI, t.FixtureBaseROI,
             t.FixtureBaseROIAngle, t.FixtureRefX, t.FixtureRefY, t.FixtureRefAngle, t.HasFixtureAngle);

        /// <summary>
        /// 화면의 [Show ROI] 와 같은 순서 — 설정 VM 이 도형을 만들고, 캔버스(ImageCanvas.ShowToolROIs)가
        /// 그 도형을 선택한다(SelectedROI). 테스트에는 캔버스가 없으므로 선택만 흉내 낸다.
        /// </summary>
        private static void ShowRoi(MainViewModel vm, BlobTool tool)
        {
            vm.SelectedToolSettings!.ShowROICommand.Execute(null);
            Assert.NotNull(tool.AssociatedROIShape);
            vm.SelectedROI = tool.AssociatedROIShape;
        }

        /// <summary>
        /// 회전 ROI 를 Fixture(각도 전달)로 따라간 뒤 [Show ROI] — 각도 · ROI · Fixture 기준이 그대로여야 한다.
        /// 레시피 재로드 직후처럼 캔버스 도형이 없는 상태에서 시작한다(ShowROI 가 ROIAngle 로 도형을 만든다).
        /// </summary>
        [Fact]
        public void ShowRoi_RotatedFixtureTool_DoesNotChangeAngleOrFixtureBase()
        {
            var (vm, tool, _) = Setup(shapeAngle: 34.12);
            try
            {
                tool.AssociatedROIShape = null;   // 재로드 직후 — 도형 없음, ROIAngle 이 기준
                var source = new Dictionary<string, object>
                {
                    ["CenterX"] = 200.0, ["CenterY"] = 200.0, ["Angle"] = 0.0
                };
                tool.IsFixtureTransformActive = true;
                ToolSourceInjector.ApplyFixtureTransform(tool, source);   // 기준 스냅샷
                source["CenterX"] = 213.7;
                source["Angle"] = -6.5;
                ToolSourceInjector.ApplyFixtureTransform(tool, source);
                tool.IsFixtureTransformActive = false;

                var before = Snapshot(tool);
                Assert.True(before.hasBase);
                Assert.Equal(34.12 - 6.5, before.angle, 3);

                ShowRoi(vm, tool);

                Assert.Equal(before, Snapshot(tool));
                var shape = Assert.IsType<RectangleAffineROI>(tool.AssociatedROIShape);
                Assert.Equal(before.angle, shape.Angle, 3);     // 화면도 같은 각도
            }
            finally
            {
                vm.SelectedTool = null;
            }
        }

        /// <summary>
        /// 각도 없이 위치만 주는 기준(Blob 등)으로 회전 ROI 가 옮겨진 뒤 — 회전 중심이 옛 위치로 남아
        /// Show ROI 가 그 중심으로 ROI 를 되돌려 쓰면 ROI 가 바뀌어 Fixture 기준이 버려졌다.
        /// </summary>
        [Fact]
        public void ShowRoi_TranslationOnlyFixture_DoesNotResetFixtureBase()
        {
            var (vm, tool, _) = Setup(shapeAngle: 20);
            try
            {
                tool.AssociatedROIShape = null;
                // Run All 은 고르지 않은 도구에도 돈다 — 그 도구는 화면 동기화(회전 중심 따라가기)를 받지 않는다
                var selected = vm.SelectedTool;
                vm.SelectedTool = null;
                var source = new Dictionary<string, object> { ["CenterX"] = 200.0, ["CenterY"] = 200.0 };
                tool.IsFixtureTransformActive = true;
                ToolSourceInjector.ApplyFixtureTransform(tool, source);
                source["CenterX"] = 240.0;   // 40px 이동, 각도 없음
                ToolSourceInjector.ApplyFixtureTransform(tool, source);
                tool.IsFixtureTransformActive = false;
                vm.SelectedTool = selected;   // 실행 뒤 도구를 고르고 [Show ROI]

                var before = Snapshot(tool);
                Assert.True(before.hasBase);

                ShowRoi(vm, tool);

                Assert.Equal(before, Snapshot(tool));
                Assert.True(tool.HasFixtureBaseROI);
            }
            finally
            {
                vm.SelectedTool = null;
            }
        }

        /// <summary>새로 그린 도형을 고르면 예전처럼 도구에 적용된다 (보기 전용은 도구 자신의 도형만).</summary>
        [Fact]
        public void SelectingNewShape_StillAppliesToTool()
        {
            var (vm, tool, _) = Setup(shapeAngle: 0);
            try
            {
                var drawn = new RectangleAffineROI(300, 220, 60, 30, 15);
                vm.SelectedROI = drawn;

                Assert.Equal(15, tool.ROIAngle, 3);
                Assert.Same(drawn, tool.AssociatedROIShape);
                Assert.Equal(300, tool.ROICenterX, 3);
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
