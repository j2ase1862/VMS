using System;
using System.Collections.Generic;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 라이브 경량 표시 경로 회귀 테스트 (Basler 현장 2026-08-26: 라이브 몇 컷 뒤
    /// 느려지다 멈춤 — 프레임마다 CurrentImage 연쇄(VisionService Clone + 비트맵 생성
    /// 2회 + 커맨드 재평가)가 대형 할당을 누적시킨 결함의 재발 방지).
    /// - SetLiveFrame: CurrentImage 를 건드리지 않고 DisplayMat 만 최신 프레임으로
    /// - 이전 라이브 프레임은 교체 시 즉시 Dispose (네이티브 누수 방지)
    /// - CommitLiveFrame: 정지 시 마지막 프레임만 정식 CurrentImage 경로로 이관
    /// VisionService.Instance 싱글턴을 사용하므로 동일 Collection 으로 직렬 실행.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class LiveFrameDisplayPathTests
    {
        [Fact]
        public void SetLiveFrame_UpdatesDisplayMatWithoutTouchingCurrentImage()
        {
            var vm = CreateViewModel();
            var baseline = new Mat(8, 8, MatType.CV_8UC3, Scalar.Black);
            vm.CurrentImage = baseline;

            var live = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);
            vm.SetLiveFrame(live);

            Assert.Same(live, vm.DisplayMat);          // 표시는 라이브 프레임
            Assert.Same(baseline, vm.CurrentImage);    // 정식 이미지는 불변 (연쇄 미발생)
        }

        [Fact]
        public void SetLiveFrame_DisposesPreviousLiveFrame()
        {
            var vm = CreateViewModel();

            var first = new Mat(8, 8, MatType.CV_8UC3, Scalar.Black);
            var second = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);
            vm.SetLiveFrame(first);
            vm.SetLiveFrame(second);

            Assert.True(first.IsDisposed);             // 교체 즉시 해제 — 누수 방지
            Assert.False(second.IsDisposed);
            Assert.Same(second, vm.DisplayMat);
        }

        [Fact]
        public void CommitLiveFrame_PromotesLastFrameToCurrentImage()
        {
            var vm = CreateViewModel();
            var live = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);
            vm.SetLiveFrame(live);

            vm.CommitLiveFrame();

            Assert.Same(live, vm.CurrentImage);        // 마지막 프레임이 정식 경로로
            Assert.Same(live, vm.DisplayMat);          // 라이브 프레임 소거 후에도 표시 유지
            Assert.False(live.IsDisposed);

            vm.CommitLiveFrame();                      // 라이브 프레임 없으면 no-op
            Assert.Same(live, vm.CurrentImage);
        }

        [Fact]
        public void CurrentImage_Set_ResetsDisplayModeToOriginal()
        {
            // Result 표시 중 새 이미지 유입(파일 열기·Grab·수신) 시 Original 로 자동 복귀 —
            // 이전 실행 결과가 새 이미지를 가리는 혼동 방지 (현장 요청 2026-08-27)
            var vm = CreateViewModel();
            vm.CurrentImage = new Mat(8, 8, MatType.CV_8UC3, Scalar.Black);
            vm.SelectedDisplayMode = ImageDisplayMode.ResultImage;

            vm.CurrentImage = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);

            Assert.Equal(ImageDisplayMode.OriginalImage, vm.SelectedDisplayMode);
        }

        [Fact]
        public void CommitLiveFrame_ResetsDisplayModeToOriginal()
        {
            // 단발 Receive from VMS 는 ApplySharedFrame → CommitLiveFrame 으로 정식 이미지에
            // 이관된다 — 이관 시에도 Result 모드에 머물지 않아야 함 (F5/F6 활성화와 세트)
            var vm = CreateViewModel();
            vm.CurrentImage = new Mat(8, 8, MatType.CV_8UC3, Scalar.Black);
            vm.SelectedDisplayMode = ImageDisplayMode.ResultImage;

            vm.SetLiveFrame(new Mat(8, 8, MatType.CV_8UC3, Scalar.White));
            vm.CommitLiveFrame();

            Assert.Equal(ImageDisplayMode.OriginalImage, vm.SelectedDisplayMode);
            Assert.NotNull(vm.CurrentImage);
        }

        [Fact]
        public void CurrentImage_Set_ClearsPreviousRunResults()
        {
            // Grab/이미지 로드 후 Result 뷰에 이전 실행의 오버레이 그래픽이 잔존하던 버그
            // (현장 실증 전 보고 2026-08-27) — 새 이미지 유입 시 결과 3종(ResultImage/
            // ResultMat/OverlayImage)을 도구 전환 때와 동일하게 소거해야 한다.
            var vm = CreateViewModel();
            vm.CurrentImage = new Mat(8, 8, MatType.CV_8UC3, Scalar.Black);
            vm.ResultMat = new Mat(8, 8, MatType.CV_8UC3, Scalar.Red);
            vm.ResultImage = new System.Windows.Media.DrawingImage();
            vm.OverlayImage = new System.Windows.Media.DrawingImage();
            vm.SelectedDisplayMode = ImageDisplayMode.ResultImage;

            vm.CurrentImage = new Mat(8, 8, MatType.CV_8UC3, Scalar.White);

            Assert.Null(vm.ResultMat);
            Assert.Null(vm.ResultImage);
            Assert.Null(vm.OverlayImage);
            Assert.Equal(ImageDisplayMode.OriginalImage, vm.SelectedDisplayMode);
        }

        [Fact]
        public void DisplayMat_ReturnsCurrentImage_WhenNotLive()
        {
            var vm = CreateViewModel();
            var image = new Mat(8, 8, MatType.CV_8UC3, Scalar.Black);
            vm.CurrentImage = image;

            Assert.Same(image, vm.DisplayMat);         // 비라이브 동작 회귀 확인
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

        #region Fakes — 헤드리스 MainViewModel 생성용 최소 구현 (WorkspaceStepRoundTripTests 와 동일)

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
