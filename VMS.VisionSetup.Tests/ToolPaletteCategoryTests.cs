using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.PLC.Models.Sequence;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 툴 팔레트 카테고리 구성 검증 (2026-09-17).
    /// 얼라인 툴은 예제 템플릿 갤러리의 "얼라인" 탭과 같은 이름의 "Alignment" 카테고리에 있어야
    /// 갤러리 항목과 팔레트 항목이 같은 툴임이 드러난다 — Pattern Matching 아래 숨기지 않는다.
    /// </summary>
    public class ToolPaletteCategoryTests
    {
        private static readonly string[] AlignToolTypes = { "MatchAlignTool", "MultiStepAlignTool" };

        [Fact]
        public void Palette_HasAlignmentCategory_WithBothAlignTools()
        {
            var vm = CreateViewModel();

            var alignment = Assert.Single(vm.ToolTree, c => c.CategoryName == "Alignment");
            Assert.Equal(AlignToolTypes, alignment.Tools.Select(t => t.ToolType).ToArray());

            var patternMatching = Assert.Single(vm.ToolTree, c => c.CategoryName == "Pattern Matching");
            Assert.DoesNotContain(patternMatching.Tools, t => AlignToolTypes.Contains(t.ToolType));

            // Alignment 는 Pattern Matching 바로 뒤 — 매칭 → 얼라인 순서가 사용 흐름
            var names = vm.ToolTree.Select(c => c.CategoryName).ToList();
            Assert.Equal(names.IndexOf("Pattern Matching") + 1, names.IndexOf("Alignment"));
        }

        [Fact]
        public void Palette_EveryToolAppearsExactlyOnce()
        {
            var vm = CreateViewModel();
            var all = vm.ToolTree.SelectMany(c => c.Tools).Select(t => t.ToolType).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
        }

        [Fact]
        public void AvailableTools_MatchesPaletteCategories()
        {
            // VisionService.GetAvailableTools 는 직렬화 왕복 테스트가 전수 순회하는 목록 —
            // 팔레트와 같은 카테고리 배치를 유지해야 한다 (Image Processing 은 팔레트에서 2개로 나뉜 예외).
            var available = VisionService.GetAvailableTools();
            Assert.Equal(AlignToolTypes, available["Alignment"]);
            Assert.DoesNotContain(available["Pattern Matching"], t => AlignToolTypes.Contains(t));
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
