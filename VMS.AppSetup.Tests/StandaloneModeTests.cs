using System.Collections.Generic;
using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Models;
using VMS.AppSetup.ViewModels;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// 단독(Standalone) 모드 저장/복원 검증 — "Web 서버 미사용" 체크는
    /// webServerUrl 을 빈 값으로 저장하는 규약이고(빈 URL = VMS/VisionSetup 이
    /// 웹 연동 서비스를 만들지 않음), 로드 시 빈 URL 이면 체크 상태로 복원된다.
    /// </summary>
    public class StandaloneModeTests
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
            public readonly List<(string Message, string Title)> Errors = new();
            public void ShowInformation(string message, string title) { }
            public void ShowError(string message, string title) => Errors.Add((message, title));
            public bool ShowConfirmation(string message, string title) => true;
            public string? ShowOpenFileDialog(string title, string filter) => null;
        }

        private static (SetupViewModel vm, CapturingConfigService svc) CreateVm(SetupConfiguration? config)
        {
            var configService = new CapturingConfigService { Config = config };
            var vm = new SetupViewModel(configService, new StubDialogService(), () => { });
            return (vm, configService);
        }

        private static void SaveViaWizard(SetupViewModel vm)
        {
            // 마지막 페이지의 [Finish] = 저장 — GoNext 를 페이지 수만큼 진행
            for (int i = 0; i < 10 && vm.NextButtonText != "Finish"; i++)
                vm.GoNextCommand.Execute(null);
            vm.GoNextCommand.Execute(null);
        }

        [Fact]
        public void Load_EmptyWebServerUrl_RestoresStandaloneChecked()
        {
            var (vm, _) = CreateVm(new SetupConfiguration { WebServerUrl = "" });

            Assert.True(vm.IsStandaloneMode);
            Assert.False(vm.IsWebFieldsEnabled);
            // 입력란은 기본값 유지 — 체크 해제 시 바로 재사용 가능해야 함
            Assert.False(string.IsNullOrWhiteSpace(vm.WebServerUrl));
        }

        [Fact]
        public void Load_WithWebServerUrl_StandaloneUnchecked()
        {
            var (vm, _) = CreateVm(new SetupConfiguration { WebServerUrl = "http://192.168.0.10:7144" });

            Assert.False(vm.IsStandaloneMode);
            Assert.True(vm.IsWebFieldsEnabled);
            Assert.Equal("http://192.168.0.10:7144", vm.WebServerUrl);
        }

        [Fact]
        public void Save_StandaloneChecked_PersistsEmptyUrlAndDisablesSso()
        {
            var (vm, svc) = CreateVm(new SetupConfiguration
            {
                WebServerUrl = "http://192.168.0.10:7144",
                WebSso = new WebSsoSettings { Enabled = true, WebServerUrl = "http://192.168.0.10:7144" }
            });

            vm.IsStandaloneMode = true;
            SaveViaWizard(vm);

            Assert.NotNull(svc.Saved);
            Assert.Equal(string.Empty, svc.Saved!.WebServerUrl);
            Assert.False(svc.Saved.WebSso?.Enabled ?? false);
        }

        [Fact]
        public void Save_StandaloneUnchecked_PersistsUrlAgain()
        {
            var (vm, svc) = CreateVm(new SetupConfiguration { WebServerUrl = "" });

            Assert.True(vm.IsStandaloneMode);
            vm.IsStandaloneMode = false;   // 연동 모드로 복귀 — 입력란의 URL 이 다시 저장돼야 함
            SaveViaWizard(vm);

            Assert.NotNull(svc.Saved);
            Assert.False(string.IsNullOrWhiteSpace(svc.Saved!.WebServerUrl));
            Assert.Equal(vm.WebServerUrl, svc.Saved.WebServerUrl);
        }

        /// <summary>
        /// 단독 모드에서도 MLOps 모델 레지스트리는 쓸 수 있어야 한다.
        ///
        /// <para>MLOps 는 BODA.VMS.Web 과 <b>별개 서버</b>다 — Web 을 안 쓰는 라인도 레시피의
        /// <c>model://</c> 참조 모델은 MLOps 에서 내려받는다. 화면에서 MLOps 칸이 Web 입력
        /// 블록 안에 있어 단독 모드를 켜면 함께 잠겼고, 현장에서는 "다시 넣으려면 MSI 를
        /// 재설치해야 한다"로 읽혔다 (2026-09-16 현장 보고). 저장 규약은 그때도 값을 보존하고
        /// 있었으므로, 그 규약을 못 박아 둔다 — 화면만 고치고 저장이 지우면 되돌아간다.</para>
        /// </summary>
        [Fact]
        public void Save_StandaloneChecked_KeepsMlopsRegistrySettings()
        {
            var (vm, svc) = CreateVm(new SetupConfiguration { WebServerUrl = "http://192.168.0.10:7144" });

            vm.IsStandaloneMode = true;
            vm.MlopsServerUrl = "http://mlops.local:5310";
            vm.MlopsLineToken = "ln_line11_token";
            SaveViaWizard(vm);

            Assert.NotNull(svc.Saved);
            Assert.Equal(string.Empty, svc.Saved!.WebServerUrl);          // 단독 모드 규약은 그대로
            Assert.Equal("http://mlops.local:5310", svc.Saved.MlopsServerUrl);
            Assert.Equal("ln_line11_token", svc.Saved.MlopsLineToken);
        }

        /// <summary>
        /// 저장된 MLOps 값이 있으면 고급 설정이 자동으로 펼쳐져야 한다 —
        /// 접힌 채면 "칸이 어디 있는지 못 찾는" 같은 보고로 이어진다.
        /// </summary>
        [Fact]
        public void Load_ExistingMlopsSettings_ExpandsAdvancedSection()
        {
            var (vm, _) = CreateVm(new SetupConfiguration
            {
                WebServerUrl = "",
                MlopsServerUrl = "http://mlops.local:5310"
            });

            Assert.True(vm.IsStandaloneMode);
            Assert.True(vm.IsAdvancedWebSettingsVisible);
            Assert.Equal("http://mlops.local:5310", vm.MlopsServerUrl);
        }
    }
}
