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
    /// CameraViewModel.SaveSettingsCommand("Save Parameter" 버튼) 검증 —
    /// 현장 사고(2026-08-10) 회귀: 구성에 없는 카메라의 저장이 무증상 no-op 으로
    /// 버려져 "저장했는데 재시작하면 초기값" 미스터리를 만들었다.
    /// </summary>
    public class CameraViewModelSaveSettingsTests
    {
        private sealed class RecordingConfigService : IConfigurationService
        {
            public SystemConfiguration Config { get; set; } = new();
            public SystemConfiguration? Saved { get; private set; }
            public bool SaveResult { get; set; } = true;

            public string ConfigDirectory => Path.GetTempPath();
            public SystemConfiguration LoadSystemConfiguration() => Config;
            public bool SaveSystemConfiguration(SystemConfiguration config)
            {
                Saved = config;
                return SaveResult;
            }
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

        private static CameraViewModel MakeVm(RecordingConfigService config, string id = "cam-1")
        {
            var vm = new CameraViewModel(new NoopDialogService(), config) { Id = id, Name = "Cam1" };
            vm.Steps.Add(new StepViewModel
            {
                StepNumber = 1,
                Name = "Step 1",
                Use2DCameraDefault = false,
                Exposure = 12345,
                Gain = 2.5
            });
            vm.SelectedStep = vm.Steps[0];
            return vm;
        }

        [Fact]
        public void SaveSettings_CameraInConfig_WritesStepsAndReportsSuccess()
        {
            var config = new RecordingConfigService();
            config.Config.Cameras.Add(new CameraConfiguration { Id = "cam-1", Name = "Cam1" });
            var vm = MakeVm(config);

            vm.SaveSettingsCommand.Execute(null);

            Assert.NotNull(config.Saved);
            var cam = config.Saved!.Cameras.Single(c => c.Id == "cam-1");
            var step = Assert.Single(cam.Steps);
            Assert.Equal(12345, step.Exposure);
            Assert.Equal(2.5, step.Gain);
            Assert.False(step.Use2DCameraDefault);
            Assert.Equal(1, cam.StepCount);
            Assert.Contains("saved", vm.ResultMessage);
        }

        [Fact]
        public void SaveSettings_CameraMissingFromConfig_NotifiesInsteadOfSilentNoop()
        {
            var config = new RecordingConfigService(); // 빈 구성 — default-camera / id 불일치 상황
            var vm = MakeVm(config, id: "default-camera-1");

            vm.SaveSettingsCommand.Execute(null);

            Assert.Null(config.Saved);                       // 저장 시도 없음
            Assert.Contains("not found", vm.ResultMessage);  // 무증상 금지 — 반드시 통지
        }

        [Fact]
        public void SaveSettings_SaveFails_ReportsFailure()
        {
            var config = new RecordingConfigService { SaveResult = false };
            config.Config.Cameras.Add(new CameraConfiguration { Id = "cam-1" });
            var vm = MakeVm(config);

            vm.SaveSettingsCommand.Execute(null);

            Assert.Contains("save failed", vm.ResultMessage);
        }
    }
}
