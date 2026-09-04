using System;
using System.Collections.Generic;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 실증 보고 (2026-09-04): FeatureMatch Train ROI 를 RectangleAffine(각도 있음)으로 그려 학습하면
    /// 정상인데, 프로그램 재실행 → 레시피 재로드 후 보면 ROI 가 축 정렬 Rect 로 되살아난다.
    /// 원인: 재로드 후 AssociatedROIShape 는 null 이고, [Show ROI] 가 도형을 재생성할 때 측정 3종만
    /// Affine 으로 취급해 FeatureMatch 는 각도를 무시한 RectangleROI 를 만들었다.
    /// 부수 결함: 회전 ROI 를 일반 사각형으로 다시 그리거나 Clear 해도 ROIAngle/ROICenter 가 남아
    /// 다음 학습 크롭이 옛 각도·중심으로 워프된다.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class FeatureMatchRotatedRoiReloadTests
    {
        private const double Cx = 150, Cy = 140, W = 80, H = 60, AngleDeg = 30;

        private static Mat Scene()
        {
            var img = new Mat(300, 300, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(img, new Rect(120, 110, 60, 60), Scalar.White, -1);
            Cv2.Circle(img, new Point(150, 140), 12, Scalar.Gray, -1);
            Cv2.Line(img, new Point(100, 100), new Point(200, 180), Scalar.White, 3);
            return img;
        }

        /// <summary>MainViewModel.ApplyROIToTool 의 RectangleAffineROI 분기와 동일한 동기화.</summary>
        private static FeatureMatchTool ToolWithRotatedRoi()
        {
            var roi = new RectangleAffineROI(Cx, Cy, W, H, AngleDeg);
            return new FeatureMatchTool
            {
                UseROI = true,
                ROI = new Rect((int)(Cx - W / 2), (int)(Cy - H / 2), (int)W, (int)H),
                ROIAngle = AngleDeg,
                ROICenterX = Cx,
                ROICenterY = Cy,
                AssociatedROIShape = roi
            };
        }

        private static void AssertSameImage(Mat a, Mat b)
        {
            Assert.Equal(a.Size(), b.Size());
            using var diff = new Mat();
            Cv2.Absdiff(a, b, diff);
            Assert.Equal(0, Cv2.CountNonZero(diff));
        }

        [Fact]
        public void Reload_ThenShowRoi_RestoresRectangleAffineWithAngle()
        {
            var original = ToolWithRotatedRoi();
            using var scene = Scene();
            using var trainedCrop = original.GetAlignedROIImage(scene);
            Assert.True(original.TrainPattern(trainedCrop));

            // 저장 → 재실행 → 재로드
            var restored = (FeatureMatchTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(original))!;
            Assert.Null(restored.AssociatedROIShape);
            Assert.Equal(AngleDeg, restored.ROIAngle, 6);

            using var vm = new FeatureMatchToolSettingsViewModel(restored);
            vm.ShowROICommand.Execute(null);

            var affine = Assert.IsType<RectangleAffineROI>(restored.AssociatedROIShape);
            Assert.Equal(AngleDeg, affine.Angle, 6);
            Assert.Equal(Cx, affine.CenterX, 6);
            Assert.Equal(Cy, affine.CenterY, 6);
            Assert.Equal(W, affine.Width, 6);
            Assert.Equal(H, affine.Height, 6);

            // 복원된 도형으로 재학습해도 원 학습과 같은(회전 정렬) 크롭이어야 한다
            using var reloadCrop = restored.GetAlignedROIImage(scene);
            AssertSameImage(trainedCrop, reloadCrop);
            AssertSameImage(trainedCrop, restored.SelectedModel!.TemplateImage!);
        }

        [Fact]
        public void Reload_ThenShowRoi_NonRotatedStaysRectangle()
        {
            var tool = new FeatureMatchTool { UseROI = true, ROI = new Rect(10, 20, 50, 40) };
            var restored = (FeatureMatchTool)ToolSerializer.DeserializeTool(ToolSerializer.SerializeTool(tool))!;

            using var vm = new FeatureMatchToolSettingsViewModel(restored);
            vm.ShowROICommand.Execute(null);

            var rect = Assert.IsType<RectangleROI>(restored.AssociatedROIShape);
            Assert.Equal(10, rect.X, 6);
            Assert.Equal(20, rect.Y, 6);
        }

        [Fact]
        public void ClearRoi_ResetsRotationState()
        {
            var tool = ToolWithRotatedRoi();
            using var vm = new FeatureMatchToolSettingsViewModel(tool);

            vm.ClearROICommand.Execute(null);

            Assert.False(tool.UseROI);
            Assert.Null(tool.AssociatedROIShape);
            Assert.Equal(0, tool.ROIAngle);
            Assert.Equal(0, tool.ROICenterX);
            Assert.Equal(0, tool.ROICenterY);
        }

        [Fact]
        public void RedrawPlainRectangle_ClearsStaleRotation()
        {
            var vm = CreateViewModel();
            var tool = ToolWithRotatedRoi();
            vm.SelectedTool = new ToolItem { Name = tool.Name, ToolType = tool.ToolType, VisionTool = tool };

            vm.OnROICreated(new RectangleROI(20, 30, 100, 50) { Name = "Rect_1" });

            Assert.IsType<RectangleROI>(tool.AssociatedROIShape);
            Assert.Equal(new Rect(20, 30, 100, 50), tool.ROI);
            Assert.Equal(0, tool.ROIAngle);
            Assert.Equal(0, tool.ROICenterX);
            Assert.Equal(0, tool.ROICenterY);

            // 새 사각형 학습 크롭 = 축 정렬 크롭 (옛 30° 워프가 섞이면 안 된다)
            using var scene = Scene();
            using var aligned = tool.GetAlignedROIImage(scene);
            using var plain = new Mat(scene, tool.ROI);
            AssertSameImage(plain, aligned);

            vm.SelectedTool = null;
        }

        [Fact]
        public void MoveRotatedRoiByTextField_WithoutShape_MovesRotationCenter()
        {
            var vm = CreateViewModel();
            var tool = ToolWithRotatedRoi();
            tool.AssociatedROIShape = null;   // 재로드 직후: 도형 미표시
            vm.SelectedTool = new ToolItem { Name = tool.Name, ToolType = tool.ToolType, VisionTool = tool };

            tool.ROIX += 50;
            tool.ROIY -= 10;

            Assert.Equal(Cx + 50, tool.ROICenterX, 6);
            Assert.Equal(Cy - 10, tool.ROICenterY, 6);
            Assert.Equal(AngleDeg, tool.ROIAngle, 6);

            vm.SelectedTool = null;
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
