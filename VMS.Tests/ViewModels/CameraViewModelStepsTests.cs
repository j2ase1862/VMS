using System.IO;
using System.Linq;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Models;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// CameraViewModel 의 스텝 목록 검증 —
    /// 현장 보고(2026-09-22) 회귀: 스텝이 2개인 레시피를 열어도 VMS 카메라 화면에는
    /// "Step 1" 하나만 보였다. 목록을 레시피가 아니라 system_config.json 의 카메라
    /// 설정에서만 만들었기 때문이다. 게다가 콤보를 바꿔도 검사 대상이 그대로였다
    /// (SelectedStep 이 CurrentStepIndex 를 갱신하지 않았다).
    /// </summary>
    public class CameraViewModelStepsTests
    {
        private sealed class StubConfigService : IConfigurationService
        {
            public string ConfigDirectory => Path.GetTempPath();
            public SystemConfiguration LoadSystemConfiguration() => new();
            public bool SaveSystemConfiguration(SystemConfiguration config) => true;
            public LayoutConfiguration LoadLayoutConfiguration() => new();
            public bool SaveLayoutConfiguration(LayoutConfiguration config) => true;
            public PlcSignalConfiguration LoadPlcSignalConfiguration() => new();
            public bool SavePlcSignalConfiguration(PlcSignalConfiguration config) => true;
        }

        private sealed class NoopDialogService : IDialogService
        {
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null) => null;
            public string? ShowOpenFileDialog(string filter, string defaultExt) => null;
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public bool ShowLoginDialog(IUserService userService) => false;
        }

        private const string CameraId = "cam-1";

        private static CameraViewModel MakeVm()
        {
            var vm = new CameraViewModel(new NoopDialogService(), new StubConfigService())
            {
                Id = CameraId,
                Name = "Cam1"
            };
            // 카메라 설정에 스텝이 없는 실제 현장 상태 = 기본 1개
            vm.SetConfigSteps(new[]
            {
                new InspectionStep { Sequence = 1, Name = "Step 1", CameraId = CameraId }
            });
            return vm;
        }

        private static Recipe TwoStepRecipe()
        {
            var recipe = new Recipe { Name = "TwoStep" };
            recipe.Steps.Add(new InspectionStep { Id = "s1", Name = "1-1", Sequence = 1, CameraId = CameraId });
            recipe.Steps.Add(new InspectionStep { Id = "s2", Name = "1-2", Sequence = 2, CameraId = CameraId });
            // 다른 카메라의 스텝은 이 카메라 목록에 끼면 안 된다
            recipe.Steps.Add(new InspectionStep { Id = "s3", Name = "2-1", Sequence = 1, CameraId = "cam-2" });
            return recipe;
        }

        [Fact]
        public void SetRecipe_ShowsRecipeStepsForThisCameraOnly()
        {
            var vm = MakeVm();
            Assert.Single(vm.Steps);   // 레시피 전 — 카메라 설정 폴백

            vm.SetRecipe(TwoStepRecipe());

            Assert.Equal(new[] { "s1", "s2" }, vm.Steps.Select(s => s.Id));
            Assert.Equal("s1", vm.SelectedStep?.Id);
        }

        [Fact]
        public void SetRecipe_OrdersBySequence()
        {
            var recipe = new Recipe { Name = "OutOfOrder" };
            recipe.Steps.Add(new InspectionStep { Id = "b", Sequence = 2, CameraId = CameraId });
            recipe.Steps.Add(new InspectionStep { Id = "a", Sequence = 1, CameraId = CameraId });

            var vm = MakeVm();
            vm.SetRecipe(recipe);

            Assert.Equal(new[] { "a", "b" }, vm.Steps.Select(s => s.Id));
        }

        [Fact]
        public void SelectedStep_UpdatesCurrentStepIndex()
        {
            // 콤보에서 2번 스텝을 고르면 검사 대상도 2번이어야 한다.
            var vm = MakeVm();
            vm.SetRecipe(TwoStepRecipe());

            vm.SelectedStep = vm.Steps[1];

            Assert.Equal(1, vm.CurrentStepIndex);
        }

        [Fact]
        public void CurrentStepIndex_UpdatesSelectedStep()
        {
            // PLC 시퀀스의 StepChange 는 인덱스만 바꾼다 — 화면 선택도 따라가야 한다.
            var vm = MakeVm();
            vm.SetRecipe(TwoStepRecipe());

            vm.CurrentStepIndex = 1;

            Assert.Equal("s2", vm.SelectedStep?.Id);
        }

        [Fact]
        public void SetRecipe_KeepsSelectionByStepId()
        {
            var vm = MakeVm();
            vm.SetRecipe(TwoStepRecipe());
            vm.SelectedStep = vm.Steps[1];

            vm.SetRecipe(TwoStepRecipe());   // VisionSetup 저장 감지 → 재로드

            Assert.Equal("s2", vm.SelectedStep?.Id);
            Assert.Equal(1, vm.CurrentStepIndex);
        }

        [Fact]
        public void SetRecipe_Null_FallsBackToCameraConfigSteps()
        {
            var vm = MakeVm();
            vm.SetRecipe(TwoStepRecipe());

            vm.SetRecipe(null);

            Assert.Single(vm.Steps);
            Assert.Equal("Step 1", vm.SelectedStep?.Name);
        }

        [Fact]
        public void SetRecipe_NoStepsForThisCamera_FallsBackToCameraConfigSteps()
        {
            var recipe = new Recipe { Name = "OtherCameraOnly" };
            recipe.Steps.Add(new InspectionStep { Id = "x", Sequence = 1, CameraId = "cam-2" });

            var vm = MakeVm();
            vm.SetRecipe(recipe);

            Assert.Single(vm.Steps);
            Assert.Equal("Step 1", vm.SelectedStep?.Name);
        }

        [Fact]
        public void SelectedStep_DrivesExposureAndGain()
        {
            var recipe = TwoStepRecipe();
            recipe.Steps[0].Exposure = 1000;
            recipe.Steps[1].Exposure = 8000;
            recipe.Steps[1].Gain = 3.5;

            var vm = MakeVm();
            vm.SetRecipe(recipe);
            Assert.Equal(1000, vm.Exposure);

            vm.CurrentStepIndex = 1;

            Assert.Equal(8000, vm.Exposure);
            Assert.Equal(8, vm.ExposureMs);
            Assert.Equal(3.5, vm.Gain);
        }
    }
}
