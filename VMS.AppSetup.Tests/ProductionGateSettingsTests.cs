using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Models;
using VMS.AppSetup.ViewModels;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// 생산 관리 스위치(작업자 로그인 필수 · 작업지시 선택 필수) 저장/복원 — 둘 다 기본 켬(종전 동작)이고,
    /// WO 목록은 작업자 로그인으로 조회하므로 로그인을 끄면 WO 필수도 꺼진 채 저장돼야 한다.
    /// </summary>
    public class ProductionGateSettingsTests
    {
        private sealed class CapturingConfigService : IConfigurationService
        {
            public SetupConfiguration? Config;
            public SetupConfiguration? Saved;
            public string? LastLoadError { get; set; }
            public string ConfigFilePath => @"test:\system_config.json";
            public string InvalidBackupPath => @"test:\system_config.json.invalid.bak";
            public SetupConfiguration? LoadConfiguration() => Config;
            public bool SaveConfiguration(SetupConfiguration config) { Saved = config; return true; }
            public bool ConfigurationExists() => Config != null;
            public bool ExportConfiguration(SetupConfiguration config, string exportPath) => true;
        }

        private sealed class StubDialogService : IDialogService
        {
            public void ShowInformation(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => true;
            public string? ShowOpenFileDialog(string title, string filter) => null;
        }

        private static (SetupViewModel vm, CapturingConfigService svc) CreateVm(SetupConfiguration config)
        {
            var svc = new CapturingConfigService { Config = config };
            return (new SetupViewModel(svc, new StubDialogService(), () => { }), svc);
        }

        private static void SaveViaWizard(SetupViewModel vm)
        {
            for (int i = 0; i < 10 && vm.NextButtonText != "Finish"; i++)
                vm.GoNextCommand.Execute(null);
            vm.GoNextCommand.Execute(null);
        }

        [Fact]
        public void Defaults_are_both_required()
        {
            var config = new SetupConfiguration();
            Assert.True(config.RequireOperatorLogin);
            Assert.True(config.RequireWorkOrder);

            var (vm, _) = CreateVm(config);
            Assert.True(vm.RequireOperatorLogin);
            Assert.True(vm.RequireWorkOrder);
        }

        [Fact]
        public void Turning_login_off_turns_work_order_off_and_saves_both_off()
        {
            var (vm, svc) = CreateVm(new SetupConfiguration { WebServerUrl = "http://192.168.0.10:5292" });

            vm.RequireOperatorLogin = false;
            Assert.False(vm.RequireWorkOrder);

            SaveViaWizard(vm);
            Assert.NotNull(svc.Saved);
            Assert.False(svc.Saved!.RequireOperatorLogin);
            Assert.False(svc.Saved.RequireWorkOrder);
        }

        [Fact]
        public void Login_only_round_trips()
        {
            var (vm, svc) = CreateVm(new SetupConfiguration
            {
                WebServerUrl = "http://192.168.0.10:5292",
                RequireOperatorLogin = true,
                RequireWorkOrder = false,
            });
            Assert.True(vm.RequireOperatorLogin);
            Assert.False(vm.RequireWorkOrder);

            SaveViaWizard(vm);
            Assert.True(svc.Saved!.RequireOperatorLogin);
            Assert.False(svc.Saved.RequireWorkOrder);
        }

        [Fact]
        public void Inconsistent_file_loads_with_work_order_off()
        {
            // 손으로 고친 파일 등 — 로그인 없이 WO 필수는 성립하지 않는다.
            var (vm, _) = CreateVm(new SetupConfiguration
            {
                WebServerUrl = "http://192.168.0.10:5292",
                RequireOperatorLogin = false,
                RequireWorkOrder = true,
            });
            Assert.False(vm.RequireWorkOrder);
        }
    }
}
