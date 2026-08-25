using System;
using System.Collections.Generic;
using System.Linq;
using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 스텝 로드 → 저장 라운드트립 회귀 테스트.
    /// 비활성(IsEnabled=false) 도구가 LoadStepToWorkspace에서 제외되면
    /// SaveWorkspaceToStep의 Tools.Clear() 후 재저장 과정에서 레시피에서 영구 삭제되는 결함 방지.
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class WorkspaceStepRoundTripTests
    {
        [Fact]
        public void LoadSaveRoundTrip_PreservesDisabledToolAndItsConnections()
        {
            var vm = CreateViewModel();

            // 활성 도구 1 + 비활성 도구 1 + 활성→비활성 Image 연결을 가진 스텝 구성
            var enabledTool = VisionService.CreateTool("BlurTool")!;
            enabledTool.Name = "Blur #1";
            var enabledConfig = ToolSerializer.SerializeTool(enabledTool);
            enabledConfig.Sequence = 1;

            var disabledTool = VisionService.CreateTool("BlobTool")!;
            disabledTool.Name = "Blob #1";
            disabledTool.IsEnabled = false;
            var disabledConfig = ToolSerializer.SerializeTool(disabledTool);
            disabledConfig.Sequence = 2;
            disabledConfig.Connections.Add(new ToolConnectionConfig
            {
                SourceToolId = enabledConfig.Id,
                ConnectionType = "Image"
            });

            var step = new InspectionStep { Name = "Step 1" };
            step.Tools.Add(enabledConfig);
            step.Tools.Add(disabledConfig);

            // 로드 (SelectedStep 설정 시 LoadStepToWorkspace 자동 호출)
            vm.SelectedStep = step;

            // 비활성 도구도 워크스페이스에 로드되어야 함 (실행 스킵은 VisionService의 IsEnabled 체크가 담당)
            Assert.Equal(2, vm.DroppedTools.Count);
            var loadedDisabled = vm.DroppedTools.First(t => t.ToolType == "BlobTool");
            Assert.NotNull(loadedDisabled.VisionTool);
            Assert.False(loadedDisabled.VisionTool!.IsEnabled);

            // 저장 — Tools.Clear() 후 워크스페이스 내용으로 재구성
            vm.SaveWorkspaceToStep();

            // 라운드트립 후에도 비활성 도구와 플래그가 보존되어야 함
            Assert.Equal(2, step.Tools.Count);
            var savedDisabled = step.Tools.FirstOrDefault(t => t.ToolType == "BlobTool");
            Assert.NotNull(savedDisabled);
            Assert.False(savedDisabled!.IsEnabled);

            // 비활성 도구로 들어오는 연결도 함께 보존
            var savedConnection = Assert.Single(savedDisabled.Connections);
            Assert.Equal("Image", savedConnection.ConnectionType);
            var savedEnabled = step.Tools.First(t => t.ToolType == "BlurTool");
            Assert.Equal(savedEnabled.Id, savedConnection.SourceToolId);
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

        #region Fakes — 헤드리스 MainViewModel 생성용 최소 구현

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
            public InspectionStep? AddStep(Recipe? recipe = null, string? cameraId = null) => null;
            public void AddStep(Recipe recipe, InspectionStep step) { }
            public bool RemoveStep(Recipe? recipe, string stepId) => false;
            public bool MoveStep(Recipe? recipe, string stepId, int newSequence) => false;
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
        }

        #endregion
    }
}
