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
    /// 캘리브레이션 매니저의 [Apply to Current Recipe] 가 실제로 무엇을 바꾸는지 고정한다.
    ///
    /// <para><b>왜 필요한가 (dev PC 검증 2026-09-15).</b> Apply 는 recipe.Calibration 만 채우고
    /// 스텝의 Resolution(mm/px)은 건드리지 않았다. 그래서 VisionSetup 왼쪽 Steps 표의 Resolution 은
    /// 기본값 0.05 그대로였고, 사용자 입장에서는 "적용했는데 어디에도 안 보인다" 가 됐다.
    /// 여기서는 ① 캘리브레이션 저장 ② 고른 스텝만 Resolution 갱신 ③ 레시피 저장 세 가지를 못 박는다.</para>
    /// </summary>
    public class CalibrationApplyToRecipeTests : IDisposable
    {
        // VisionService 는 싱글턴이라 Apply 가 남긴 캘리브레이션이 같은 어셈블리의 다른 테스트
        // (MatchAlign/MultiStepAlign 의 "캘리브레이션 없음" 시나리오)로 새 나간다 — 테스트마다 되돌린다.
        private readonly CalibrationMetadata? _savedCalibration = VisionService.Instance.CurrentCalibrationMetadata;
        private readonly double _savedStepResolution = VisionService.Instance.CurrentStepResolutionMmPerPx;

        public void Dispose()
        {
            VisionService.Instance.CurrentCalibrationMetadata = _savedCalibration;
            VisionService.Instance.CurrentStepResolutionMmPerPx = _savedStepResolution;
        }

        private static CalibrationMetadata Meta(double mmPerPx) => new()
        {
            Mode = CalibrationMode.CirclesGrid,
            PixelSizeMm = mmPerPx,
            ReprojectionError = 0.42,
            CameraMatrix = new[]
            {
                new[] { 1000.0, 0.0, 640.0 },
                new[] { 0.0, 1000.0, 480.0 },
                new[] { 0.0, 0.0, 1.0 }
            },
            DistortionCoeffs = new[] { 0.0, 0.0, 0.0, 0.0, 0.0 },
            ImageWidth = 1280,
            ImageHeight = 960
        };

        private static Recipe RecipeWithSteps()
        {
            var recipe = new Recipe { Name = "calib-test" };
            recipe.Steps.Add(new InspectionStep { Name = "A-1", Sequence = 1, CameraId = "camA", Resolution = 0.05 });
            recipe.Steps.Add(new InspectionStep { Name = "A-2", Sequence = 2, CameraId = "camA", Resolution = 0.05 });
            recipe.Steps.Add(new InspectionStep { Name = "B-1", Sequence = 3, CameraId = "camB", Resolution = 0.08 });
            return recipe;
        }

        private static (CalibrationManagerViewModel Vm, FakeRecipeService Svc) Open(Recipe recipe)
        {
            var svc = new FakeRecipeService { CurrentRecipe = recipe };
            return (new CalibrationManagerViewModel(svc, new FakeDialogService()), svc);
        }

        /// <summary>계산 결과 주입 — Run 은 실제 패턴 이미지를 요구하므로 결과만 넣는다.</summary>
        private static void Inject(CalibrationManagerViewModel vm, CalibrationMetadata meta)
        {
            typeof(CalibrationManagerViewModel)
                .GetField("_lastResultMetadata",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(vm, meta);
            vm.PixelSizeMm = meta.PixelSizeMm;
            vm.HasResult = true;
        }

        [Fact]
        public void Apply_writes_calibration_and_selected_step_resolution_and_saves()
        {
            var recipe = RecipeWithSteps();
            var (vm, svc) = Open(recipe);
            Inject(vm, Meta(0.01234));

            // 소스 카메라를 모르면(파일 소스) 전체가 기본 선택 — B-1 만 빼고 적용한다
            Assert.Equal(3, vm.ApplyTargets.Count);
            Assert.All(vm.ApplyTargets, t => Assert.True(t.IsSelected));
            vm.ApplyTargets.Single(t => t.StepName.StartsWith("B-1")).IsSelected = false;

            vm.ApplyToRecipeCommand.Execute(null);

            Assert.NotNull(recipe.Calibration);
            Assert.Equal(0.01234, recipe.Calibration!.PixelSizeMm, 6);
            Assert.Equal(0.01234, recipe.Steps[0].Resolution, 6);
            Assert.Equal(0.01234, recipe.Steps[1].Resolution, 6);
            Assert.Equal(0.08, recipe.Steps[2].Resolution, 6);   // 해제한 스텝은 그대로
            Assert.Equal(1, svc.SaveCount);

            vm.Dispose();
        }

        [Fact]
        public void Apply_with_no_target_selected_saves_calibration_only()
        {
            var recipe = RecipeWithSteps();
            var (vm, svc) = Open(recipe);
            Inject(vm, Meta(0.02));

            vm.SelectNoApplyTargetsCommand.Execute(null);
            vm.ApplyToRecipeCommand.Execute(null);

            Assert.NotNull(recipe.Calibration);
            Assert.All(recipe.Steps, s => Assert.NotEqual(0.02, s.Resolution));
            Assert.Equal(1, svc.SaveCount);

            vm.Dispose();
        }

        [Fact]
        public void Preview_shows_before_and_after_and_refreshes_once_applied()
        {
            var recipe = RecipeWithSteps();
            var (vm, _) = Open(recipe);
            Inject(vm, Meta(0.0075));

            var row = vm.ApplyTargets[0];
            Assert.Equal(0.05, row.CurrentResolution, 6);
            Assert.Equal(0.0075, row.NewResolution, 6);
            Assert.Contains("0.05000", row.ChangeText);
            Assert.Contains("0.00750", row.ChangeText);

            vm.ApplyToRecipeCommand.Execute(null);

            // 적용 후 "현재 값" 이 새 값으로 다시 읽혀야 한다 (연속 적용 시 혼동 방지)
            Assert.Equal(0.0075, row.CurrentResolution, 6);
            Assert.Contains("0.00750", vm.AppliedSummary);

            vm.Dispose();
        }

        [Fact]
        public void Clear_removes_calibration_but_keeps_step_resolution()
        {
            var recipe = RecipeWithSteps();
            var (vm, _) = Open(recipe);
            Inject(vm, Meta(0.01));
            vm.ApplyToRecipeCommand.Execute(null);

            vm.ClearRecipeCalibrationCommand.Execute(null);

            Assert.Null(recipe.Calibration);
            // 지워도 스텝 Resolution 은 남는다 — 측정 도구의 mm 폴백으로 계속 쓰인다
            Assert.Equal(0.01, recipe.Steps[0].Resolution, 6);
            Assert.Contains("없음", vm.AppliedSummary);

            vm.Dispose();
        }

        [Fact]
        public void CirclesGrid_metadata_is_valid()
        {
            // CirclesGrid 가 IsValid 의 switch 에서 빠져 있어 원형 타겟 결과는 늘 무효로 떨어졌다.
            Assert.True(Meta(0.01).IsValid());
        }

        #region Fakes — 헤드리스 ViewModel 생성용 최소 구현

        private sealed class FakeRecipeService : IRecipeService
        {
            public int SaveCount { get; private set; }
            public Recipe? CurrentRecipe { get; set; }
            public string RecipeFolderPath => string.Empty;
            public event EventHandler<Recipe?>? CurrentRecipeChanged { add { } remove { } }
            public Recipe? LoadRecipe(string filePath) => null;
            public Recipe? ReadRecipeFile(string filePath) => null;
            public bool SaveRecipe(Recipe recipe, string? filePath = null) { SaveCount++; return true; }
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

        private sealed class FakeDialogService : IDialogService
        {
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => true;   // Clear 확인은 "예"
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
            public OpenCvSharp.Mat? ShowTrainMaskEditorDialog(OpenCvSharp.Mat templateImage, OpenCvSharp.Mat? existingMask,
                double cannyLow = 50, double cannyHigh = 150) => null;
        }

        #endregion
    }
}
