using System.IO;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Models;
using VMS.ViewModels;
using VMS.VisionSetup.Models;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 권한 표 CameraSettings (Engineer · Admin) — 권한이 없으면 카메라 조작 상자에서
    /// 노출/게인을 바꾸거나 [Save Parameter] 로 저장할 수 없어야 한다 (2026-11-13: Operator 도 저장 가능했음).
    /// </summary>
    public class CameraViewModelCameraSettingsPermissionTests
    {
        private sealed class CountingConfigService : IConfigurationService
        {
            public int LoadCount { get; private set; }
            public string ConfigDirectory => Path.GetTempPath();
            public SystemConfiguration LoadSystemConfiguration() { LoadCount++; return new(); }
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

        private static (CameraViewModel vm, CountingConfigService config) MakeVm()
        {
            var config = new CountingConfigService();
            var vm = new CameraViewModel(new NoopDialogService(), config) { Id = "cam-1", Name = "Cam1" };
            var step = new InspectionStep { Use2DCameraDefault = false };
            vm.Steps.Add(step);
            vm.SelectedStep = step;
            return (vm, config);
        }

        [Fact]
        public void WithoutPermission_ExposureLockedAndSaveDisabled()
        {
            var (vm, config) = MakeVm();
            Assert.True(vm.IsManualExposureEnabled);

            vm.CanEditCameraSettings = false;

            Assert.False(vm.IsManualExposureEnabled);
            Assert.False(vm.SaveSettingsCommand.CanExecute(null));
            vm.SaveSettingsCommand.Execute(null);
            Assert.Equal(0, config.LoadCount);  // 저장 경로에 들어가지 않음
        }

        [Fact]
        public void PermissionRestored_ReenablesSave()
        {
            var (vm, _) = MakeVm();
            vm.CanEditCameraSettings = false;

            vm.CanEditCameraSettings = true;

            Assert.True(vm.IsManualExposureEnabled);
            Assert.True(vm.SaveSettingsCommand.CanExecute(null));
        }
    }
}
