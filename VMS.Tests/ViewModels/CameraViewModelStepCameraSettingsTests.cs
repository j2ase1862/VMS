using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Models;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 촬영 직전 스텝 카메라 설정 적용 검증 —
    /// VMS 본체(Grab · AUTO RUN · Live)는 2D 노출/게인만 넣고 3D 후처리·뎁스 범위는 넣지 않아,
    /// VisionSetup 에서 맞춘 3D 설정이 AUTO RUN 에서는 빠진 채로 찍혔다.
    /// AUTO RUN 은 시퀀스 StepChange 가 CurrentStepIndex 를 바꾸고 grabFunc 가 GrabOnceAsync 를 부른다.
    /// </summary>
    public class CameraViewModelStepCameraSettingsTests
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

        /// <summary>호출 순서까지 기록 — 설정이 촬영보다 먼저 들어가야 한다.</summary>
        private sealed class RecordingAcquisition : ICameraAcquisition
        {
            public bool IsConnected { get; set; } = true;
            public List<string> Calls { get; } = new();
            public (double ExposureUs, double Gain)? Last2D { get; private set; }
            public Scan3DSettings? Last3D { get; private set; }

            public Task<bool> ConnectAsync(CameraInfo camera) => Task.FromResult(true);
            public Task DisconnectAsync() => Task.CompletedTask;

            public Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
            {
                Calls.Add("acquire");
                return Task.FromResult(new AcquisitionResult { Success = true });
            }

            public Task<bool> ApplySettingsAsync(double exposureUs, double gain)
            {
                Calls.Add("2d");
                Last2D = (exposureUs, gain);
                return Task.FromResult(true);
            }

            public Task<bool> Apply3DSettingsAsync(Scan3DSettings settings)
            {
                Calls.Add("3d");
                Last3D = settings;
                return Task.FromResult(true);
            }

            public void Dispose() { }
        }

        private const string CameraId = "cam-1";

        private static Recipe TwoStep3DRecipe()
        {
            var recipe = new Recipe { Name = "TwoStep3D" };
            recipe.Steps.Add(new InspectionStep
            {
                Id = "s1", Name = "1-1", Sequence = 1, CameraId = CameraId,
                PointCloudPostProcess = PointCloudPostProcessPreset.Strong,
                UseDepthRange = true, DepthRangeMinMm = 300, DepthRangeMaxMm = 800
            });
            recipe.Steps.Add(new InspectionStep
            {
                Id = "s2", Name = "1-2", Sequence = 2, CameraId = CameraId,
                Use2DCameraDefault = false, Exposure = 8000, Gain = 2.0,
                PointCloudPostProcess = PointCloudPostProcessPreset.Weak,
                UseDepthRange = true, DepthRangeMinMm = 500, DepthRangeMaxMm = 1200
            });
            return recipe;
        }

        private static (CameraViewModel Vm, RecordingAcquisition Acq) MakeVm()
        {
            var vm = new CameraViewModel(new NoopDialogService(), new StubConfigService())
            {
                Id = CameraId,
                Name = "Cam1"
            };
            var acq = new RecordingAcquisition();
            vm.SetAcquisitionForTest(acq);
            vm.SetRecipe(TwoStep3DRecipe());
            return (vm, acq);
        }

        [Fact]
        public async Task AutoRunStepChange_ThenGrab_AppliesThatStepsSettingsBeforeAcquire()
        {
            var (vm, acq) = MakeVm();

            vm.CurrentStepIndex = 1;   // 시퀀스 StepChange 노드가 하는 일 (App.xaml.cs stepChangeFunc)
            var ok = await vm.GrabOnceAsync();

            Assert.True(ok);
            Assert.Equal(new[] { "2d", "3d", "acquire" }, acq.Calls);
            Assert.Equal((8000.0, 2.0), acq.Last2D);
            Assert.Equal(new Scan3DSettings
            {
                PostProcessPreset = PointCloudPostProcessPreset.Weak,
                UseDepthRange = true, DepthRangeMinMm = 500, DepthRangeMaxMm = 1200
            }, acq.Last3D);
        }

        [Fact]
        public async Task KeepCameraExposureStep_SkipsExposureButStillApplies3D()
        {
            var (vm, acq) = MakeVm();   // 첫 스텝 선택 — 2D "카메라 설정 유지"(기본)

            await vm.GrabOnceAsync();

            Assert.Equal(new[] { "3d", "acquire" }, acq.Calls);
            Assert.Null(acq.Last2D);
            Assert.Equal(PointCloudPostProcessPreset.Strong, acq.Last3D?.PostProcessPreset);
            Assert.Equal(300, acq.Last3D?.DepthRangeMinMm);
            Assert.Equal(800, acq.Last3D?.DepthRangeMaxMm);
        }

        [Fact]
        public async Task NoStep_PushesNothing()
        {
            var acq = new RecordingAcquisition();

            await CameraViewModel.ApplyStepCameraSettingsAsync(acq, null);

            Assert.Empty(acq.Calls);
        }

        [Fact]
        public void ToScan3DSettings_CarriesAllFourFields()
        {
            var step = new InspectionStep
            {
                PointCloudPostProcess = PointCloudPostProcessPreset.Normal,
                UseDepthRange = true, DepthRangeMinMm = 120, DepthRangeMaxMm = 950
            };

            Assert.Equal(new Scan3DSettings
            {
                PostProcessPreset = PointCloudPostProcessPreset.Normal,
                UseDepthRange = true, DepthRangeMinMm = 120, DepthRangeMaxMm = 950
            }, step.ToScan3DSettings());
        }
    }
}
