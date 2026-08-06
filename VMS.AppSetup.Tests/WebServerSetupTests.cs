using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Models;
using VMS.AppSetup.Services;
using VMS.AppSetup.ViewModels;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// MSI 동봉 Web 서버 초기 구성 (AppSetup Page 2 카드) 검증.
    /// 실제 UAC 상승·서비스 시작은 통합 환경 전용이므로, 여기서는
    /// 상태 판정 / 시크릿·설정 생성 / 적용 전 가드 / VM 흐름을 검증한다.
    /// </summary>
    public class WebServerSetupTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "vms-websetup-" + Guid.NewGuid().ToString("N"));

        public WebServerSetupTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

        private string WebDir(bool exe, bool config)
        {
            var webDir = Path.Combine(_dir, "Web");
            Directory.CreateDirectory(webDir);
            if (exe) File.WriteAllText(Path.Combine(webDir, WebServerSetupService.ExeName), "");
            if (config) File.WriteAllText(Path.Combine(webDir, WebServerSetupService.ConfigFileName), "{}");
            return webDir;
        }

        private WebServerSetupService CreateService(bool exe, bool config, string serviceName = "NoSuchBodaSvc") =>
            new(WebDir(exe, config), Path.Combine(_dir, "data"), serviceName);

        // ── 상태 판정 ──

        [Fact]
        public void GetStatus_NoExe_NotInstalled()
        {
            var status = CreateService(exe: false, config: false).GetStatus();
            Assert.Equal(WebServerInstallState.NotInstalled, status.State);
        }

        [Fact]
        public void GetStatus_ExeWithoutConfig_NotConfigured_ServiceUnknown()
        {
            var status = CreateService(exe: true, config: false).GetStatus();
            Assert.Equal(WebServerInstallState.NotConfigured, status.State);
            Assert.Null(status.ServiceStatus);   // 미등록 서비스명 — 상태 조회 불가가 곧 미등록
        }

        [Fact]
        public void GetStatus_ExeAndConfig_Configured()
        {
            var status = CreateService(exe: true, config: true).GetStatus();
            Assert.Equal(WebServerInstallState.Configured, status.State);
        }

        // ── 시크릿·설정 생성 ──

        [Fact]
        public void GenerateJwtKey_MeetsAppBootRequirement_AndIsRandom()
        {
            var key1 = WebServerSetupService.GenerateJwtKey();
            var key2 = WebServerSetupService.GenerateJwtKey();
            Assert.True(key1.Length >= 32);              // 앱 부팅 검증 조건
            Assert.Equal(64, Convert.FromBase64String(key1).Length);
            Assert.NotEqual(key1, key2);
        }

        [Fact]
        public void BuildConfigJson_MatchesAspNetCoreConsumptionShape()
        {
            var json = WebServerSetupService.BuildConfigJson("pw12345678", "k".PadRight(40, 'k'), @"C:\ProgramData\BODA\VMS\BodaVision.db");

            // 소비측(ASP.NET Core 설정 로더)과 동일한 방식으로 파싱해 키 계약을 고정
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("k".PadRight(40, 'k'), doc.RootElement.GetProperty("Jwt").GetProperty("Key").GetString());
            Assert.Equal("pw12345678", doc.RootElement.GetProperty("Initial").GetProperty("AdminPassword").GetString());
            Assert.Equal(@"Data Source=C:\ProgramData\BODA\VMS\BodaVision.db",
                doc.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString());
        }

        // ── 적용 전 가드 (UAC 상승 없이 반환되어야 하는 경로) ──

        [Fact]
        public async Task ConfigureAsync_NotInstalled_FailsWithoutElevation()
        {
            var result = await CreateService(exe: false, config: false).ConfigureAsync("pw12345678");
            Assert.False(result.Success);
            Assert.Contains("설치되어 있지", result.Error);
        }

        [Fact]
        public async Task ConfigureAsync_AlreadyConfigured_RefusesOverwrite()
        {
            var result = await CreateService(exe: true, config: true).ConfigureAsync("pw12345678");
            Assert.False(result.Success);
            Assert.Contains("이미 구성", result.Error);
        }

        [Fact]
        public async Task ConfigureAsync_ShortPassword_Rejected()
        {
            var result = await CreateService(exe: true, config: false).ConfigureAsync("short");
            Assert.False(result.Success);
            Assert.Contains("8자", result.Error);
        }

        // ── 상승 모드 본체 (WebServerConfigApplier.Apply) ──

        [Fact]
        public void Apply_MissingExe_Fails()
        {
            var result = WebServerConfigApplier.Apply(new WebServerConfigApplier.Payload(
                Path.Combine(_dir, "nonexistent"), Path.Combine(_dir, "data"), "NoSuchBodaSvc", "{}"));
            Assert.False(result.Ok);
        }

        [Fact]
        public void Apply_ExistingConfig_RefusesOverwrite()
        {
            var webDir = WebDir(exe: true, config: true);
            File.WriteAllText(Path.Combine(webDir, WebServerSetupService.ConfigFileName), "{\"keep\":true}");

            var result = WebServerConfigApplier.Apply(new WebServerConfigApplier.Payload(
                webDir, Path.Combine(_dir, "data"), "NoSuchBodaSvc", "{\"new\":true}"));

            Assert.False(result.Ok);
            // 기존 운영 시크릿 보존 — 재구성 시 Jwt:Key 가 덮어써지면 발급 토큰 전체 무효화
            Assert.Contains("keep", File.ReadAllText(Path.Combine(webDir, WebServerSetupService.ConfigFileName)));
        }

        [Fact]
        public void Apply_WritesConfigAndDataDir_BeforeServiceStep()
        {
            var webDir = WebDir(exe: true, config: false);
            var dataDir = Path.Combine(_dir, "data");

            // 존재하지 않는 서비스명 → sc config 단계에서 실패해야 정상 (순서 검증:
            // 설정 파일과 데이터 폴더는 이미 만들어져 있어야 한다)
            var result = WebServerConfigApplier.Apply(new WebServerConfigApplier.Payload(
                webDir, dataDir, "NoSuchBodaSvc", "{\"cfg\":1}"));

            Assert.False(result.Ok);
            Assert.True(Directory.Exists(dataDir));
            Assert.Equal("{\"cfg\":1}", File.ReadAllText(Path.Combine(webDir, WebServerSetupService.ConfigFileName)));
        }

        // ── ViewModel 카드 흐름 ──

        private sealed class StubConfigService : IConfigurationService
        {
            public string? LastLoadError => null;
            public string ConfigFilePath => @"test:\system_config.json";
            public string InvalidBackupPath => @"test:\system_config.json.invalid.bak";
            public SetupConfiguration? LoadConfiguration() => null;
            public bool SaveConfiguration(SetupConfiguration config) => true;
            public bool ConfigurationExists() => false;
            public bool ExportConfiguration(SetupConfiguration config, string exportPath) => true;
        }

        private sealed class StubDialogService : IDialogService
        {
            public readonly List<string> Errors = new();
            public readonly List<string> Infos = new();
            public void ShowInformation(string message, string title) => Infos.Add(message);
            public void ShowError(string message, string title) => Errors.Add(message);
            public bool ShowConfirmation(string message, string title) => true;
        }

        private sealed class StubWebServerSetupService : IWebServerSetupService
        {
            public WebServerStatus Status = new(WebServerInstallState.NotConfigured, "Stopped", @"test:\Web");
            public WebServerConfigureResult ConfigureResult = new(true, null);
            public bool SmokeResult = true;
            public string? ConfiguredWith;
            public WebServerStatus GetStatus() => Status;
            public Task<WebServerConfigureResult> ConfigureAsync(string adminPassword)
            {
                ConfiguredWith = adminPassword;
                if (ConfigureResult.Success)
                    Status = Status with { State = WebServerInstallState.Configured, ServiceStatus = "Running" };
                return Task.FromResult(ConfigureResult);
            }
            public Task<bool> SmokeTestAsync() => Task.FromResult(SmokeResult);
        }

        private static (SetupViewModel vm, StubDialogService dialog, StubWebServerSetupService web) CreateVm(
            StubWebServerSetupService? web = null)
        {
            web ??= new StubWebServerSetupService();
            var dialog = new StubDialogService();
            var vm = new SetupViewModel(new StubConfigService(), dialog, () => { }, web);
            return (vm, dialog, web);
        }

        [Fact]
        public void Vm_WithoutService_ReportsNotInstalled()
        {
            var vm = new SetupViewModel(new StubConfigService(), new StubDialogService(), () => { });
            Assert.False(vm.IsWebServerInstalled);
            Assert.False(vm.IsWebServerConfigured);
        }

        [Fact]
        public void Vm_NotConfigured_StateExposedOnConstruction()
        {
            var (vm, _, _) = CreateVm();
            Assert.True(vm.IsWebServerInstalled);
            Assert.False(vm.IsWebServerConfigured);
        }

        [Fact]
        public async Task Vm_Configure_PasswordMismatch_ErrorAndNoApply()
        {
            var (vm, dialog, web) = CreateVm();
            vm.WebServerAdminPassword = "pw12345678";
            vm.WebServerAdminPasswordConfirm = "pw87654321";

            await vm.ConfigureWebServerCommand.ExecuteAsync(null);

            Assert.Contains(dialog.Errors, e => e.Contains("일치하지"));
            Assert.Null(web.ConfiguredWith);
        }

        [Fact]
        public async Task Vm_Configure_ShortPassword_ErrorAndNoApply()
        {
            var (vm, dialog, web) = CreateVm();
            vm.WebServerAdminPassword = "short";
            vm.WebServerAdminPasswordConfirm = "short";

            await vm.ConfigureWebServerCommand.ExecuteAsync(null);

            Assert.Contains(dialog.Errors, e => e.Contains("8자"));
            Assert.Null(web.ConfiguredWith);
        }

        [Fact]
        public async Task Vm_Configure_Success_DiscardsPlaintext_UpdatesState_ShowsInfo()
        {
            var (vm, dialog, web) = CreateVm();
            vm.WebServerAdminPassword = "pw12345678";
            vm.WebServerAdminPasswordConfirm = "pw12345678";

            await vm.ConfigureWebServerCommand.ExecuteAsync(null);

            Assert.Equal("pw12345678", web.ConfiguredWith);
            Assert.Equal(string.Empty, vm.WebServerAdminPassword);          // 평문 즉시 폐기
            Assert.Equal(string.Empty, vm.WebServerAdminPasswordConfirm);
            Assert.True(vm.IsWebServerConfigured);
            Assert.Contains("Running", vm.WebServerServiceStatus);
            Assert.Single(dialog.Infos);
            Assert.Empty(dialog.Errors);
        }

        [Fact]
        public async Task Vm_Configure_SmokeTestFails_ShowsEventViewerHint()
        {
            var web = new StubWebServerSetupService { SmokeResult = false };
            var (vm, dialog, _) = CreateVm(web);
            vm.WebServerAdminPassword = "pw12345678";
            vm.WebServerAdminPasswordConfirm = "pw12345678";

            await vm.ConfigureWebServerCommand.ExecuteAsync(null);

            Assert.Contains(dialog.Errors, e => e.Contains("이벤트 뷰어"));
        }

        [Fact]
        public async Task Vm_Configure_ApplyFails_ErrorSurfaced()
        {
            var web = new StubWebServerSetupService
            {
                ConfigureResult = new WebServerConfigureResult(false, "관리자 권한 승인이 거부되어 구성을 적용하지 못했습니다.")
            };
            var (vm, dialog, _) = CreateVm(web);
            vm.WebServerAdminPassword = "pw12345678";
            vm.WebServerAdminPasswordConfirm = "pw12345678";

            await vm.ConfigureWebServerCommand.ExecuteAsync(null);

            Assert.Contains(dialog.Errors, e => e.Contains("거부"));
            Assert.False(vm.IsWebServerConfigured);
        }
    }
}
