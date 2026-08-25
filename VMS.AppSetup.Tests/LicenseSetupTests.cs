using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Services;
using VMS.AppSetup.ViewModels;
using VMS.Core.Security.Licensing;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// SW 라이선스 활성화 카드 — LicenseSetupService (임시 경로/임시 키쌍 격리) +
    /// SetupViewModel 가져오기 흐름 (stub 주입). docs/design/license-spec.md §6.
    /// </summary>
    public class LicenseSetupTests : IDisposable
    {
        private const string Fp = "AAAAA-BBBBB-CCCCC";

        private readonly string _tempDir;
        private readonly string _targetPath;
        private readonly string _privateKey;
        private readonly IReadOnlyDictionary<string, string> _keyring;

        public LicenseSetupTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "boda-licsetup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _targetPath = Path.Combine(_tempDir, "install", "license.lic");

            var (priv, pub) = LicenseCrypto.CreateKeyPair();
            _privateKey = priv;
            _keyring = new Dictionary<string, string> { ["test-key"] = pub };
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private LicenseSetupService CreateService() =>
            new(_targetPath, _keyring, fingerprintOverride: Fp);

        private string WriteSignedLicense(string fingerprint = Fp, string name = "source.lic")
        {
            var payload = new LicensePayload
            {
                LicenseId = "LIC-2026-0001",
                Customer = "테스트고객",
                Kind = nameof(LicenseKind.Production),
                Fingerprint = fingerprint,
                IssuedAt = "2026-08-25",
                KeyId = "test-key"
            };
            var node = (JsonObject)JsonSerializer.SerializeToNode(payload)!;
            var signature = LicenseCrypto.Sign(_privateKey, LicenseCanonicalJson.Serialize(node));
            var path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, new JsonObject { ["payload"] = node, ["signature"] = signature }.ToJsonString());
            return path;
        }

        // ─── LicenseSetupService ─────────────────────────────────

        [Fact]
        public void GetStatus_no_file_is_Missing()
        {
            Assert.Equal(LicenseStatus.Missing, CreateService().GetStatus().Status);
        }

        [Fact]
        public async Task Import_valid_file_copies_and_status_becomes_Valid()
        {
            var service = CreateService();
            var result = await service.ImportAsync(WriteSignedLicense());

            Assert.True(result.Success);
            Assert.True(File.Exists(_targetPath));  // 설치 디렉토리 자동 생성 포함
            Assert.Equal(LicenseStatus.Valid, service.GetStatus().Status);
        }

        [Fact]
        public async Task Import_tampered_file_rejected_without_copy()
        {
            // 한글 필드는 \uXXXX 로 이스케이프되어 있으므로 ASCII 필드(licenseId)를 조작
            var path = WriteSignedLicense();
            File.WriteAllText(path, File.ReadAllText(path).Replace("LIC-2026-0001", "LIC-9999-9999"));

            var result = await CreateService().ImportAsync(path);

            Assert.False(result.Success);
            Assert.False(File.Exists(_targetPath));
        }

        [Fact]
        public void ValidateCandidate_other_pc_license_is_FingerprintMismatch()
        {
            var path = WriteSignedLicense(fingerprint: "XXXXX-YYYYY-ZZZZZ");
            Assert.Equal(LicenseStatus.FingerprintMismatch, CreateService().ValidateCandidate(path).Status);
        }

        [Fact]
        public void ValidateCandidate_missing_file_is_Invalid_not_exception()
        {
            var eval = CreateService().ValidateCandidate(Path.Combine(_tempDir, "none.lic"));
            Assert.Equal(LicenseStatus.Invalid, eval.Status);
        }

        [Fact]
        public async Task Import_overwrites_existing_license()
        {
            // 재발급(PC 교체·갱신) 경로 — 기존 설치본 위에 덮어쓰기 가능해야 한다
            var service = CreateService();
            await service.ImportAsync(WriteSignedLicense(name: "old.lic"));
            var result = await service.ImportAsync(WriteSignedLicense(name: "new.lic"));

            Assert.True(result.Success);
        }

        // ─── SetupViewModel 흐름 ─────────────────────────────────

        private sealed class StubLicenseService : ILicenseSetupService
        {
            public LicenseEvaluation Status = new() { Status = LicenseStatus.Missing, Message = "미설치" };
            public LicenseEvaluation Candidate = new() { Status = LicenseStatus.Valid, Message = "정상" };
            public bool ImportCalled;
            public LicenseImportResult ImportResult = new(true, null);

            public string GetMachineFingerprint() => Fp;
            public LicenseEvaluation GetStatus() => Status;
            public LicenseEvaluation ValidateCandidate(string filePath) => Candidate;
            public Task<LicenseImportResult> ImportAsync(string sourcePath)
            {
                ImportCalled = true;
                return Task.FromResult(ImportResult);
            }
        }

        private sealed class StubDialogService : IDialogService
        {
            public string? OpenFileResult;
            public readonly List<string> Errors = new();
            public readonly List<string> Infos = new();
            public void ShowInformation(string message, string title) => Infos.Add(message);
            public void ShowError(string message, string title) => Errors.Add(message);
            public bool ShowConfirmation(string message, string title) => true;
            public string? ShowOpenFileDialog(string title, string filter) => OpenFileResult;
        }

        private sealed class StubConfigService : IConfigurationService
        {
            public string? LastLoadError => null;
            public string ConfigFilePath => @"test:\system_config.json";
            public string InvalidBackupPath => @"test:\system_config.json.invalid.bak";
            public VMS.AppSetup.Models.SetupConfiguration? LoadConfiguration() => null;
            public bool SaveConfiguration(VMS.AppSetup.Models.SetupConfiguration config) => true;
            public bool ConfigurationExists() => false;
            public bool ExportConfiguration(VMS.AppSetup.Models.SetupConfiguration config, string exportPath) => true;
        }

        private static (SetupViewModel vm, StubDialogService dialog, StubLicenseService license) CreateVm(
            StubLicenseService? license = null)
        {
            license ??= new StubLicenseService();
            var dialog = new StubDialogService();
            var vm = new SetupViewModel(new StubConfigService(), dialog, () => { }, licenseSetup: license);
            return (vm, dialog, license);
        }

        [Fact]
        public void Ctor_refreshes_fingerprint_and_status()
        {
            var (vm, _, _) = CreateVm();

            Assert.Equal(Fp, vm.MachineFingerprintCode);
            Assert.Contains("미설치", vm.LicenseStatusText);
            Assert.Equal("Warn", vm.LicenseStatusLevel);
        }

        [Fact]
        public void Status_level_maps_severity()
        {
            var license = new StubLicenseService
            {
                Status = new LicenseEvaluation { Status = LicenseStatus.Valid, Message = "정상" }
            };
            var (vm, _, _) = CreateVm(license);
            Assert.Equal("Ok", vm.LicenseStatusLevel);

            license.Status = new LicenseEvaluation { Status = LicenseStatus.FingerprintMismatch, Message = "불일치" };
            vm.RefreshLicenseStatusCommand.Execute(null);
            Assert.Equal("Error", vm.LicenseStatusLevel);
        }

        [Fact]
        public async Task Import_canceled_dialog_does_nothing()
        {
            var (vm, dialog, license) = CreateVm();
            dialog.OpenFileResult = null;

            await vm.ImportLicenseCommand.ExecuteAsync(null);

            Assert.False(license.ImportCalled);
            Assert.Empty(dialog.Errors);
        }

        [Fact]
        public async Task Import_invalid_candidate_shows_error_and_skips_import()
        {
            var (vm, dialog, license) = CreateVm();
            dialog.OpenFileResult = "C:\\usb\\license.lic";
            license.Candidate = new LicenseEvaluation { Status = LicenseStatus.Invalid, Message = "서명 불일치" };

            await vm.ImportLicenseCommand.ExecuteAsync(null);

            Assert.False(license.ImportCalled);
            Assert.Contains(dialog.Errors, e => e.Contains("서명 불일치"));
        }

        [Fact]
        public async Task Import_fingerprint_mismatch_error_includes_this_pc_code()
        {
            var (vm, dialog, license) = CreateVm();
            dialog.OpenFileResult = "C:\\usb\\license.lic";
            license.Candidate = new LicenseEvaluation { Status = LicenseStatus.FingerprintMismatch, Message = "지문 불일치" };

            await vm.ImportLicenseCommand.ExecuteAsync(null);

            Assert.False(license.ImportCalled);
            Assert.Contains(dialog.Errors, e => e.Contains(Fp));  // 재발급 요청용 지문 안내
        }

        [Fact]
        public async Task Import_success_shows_info_and_refreshes_status()
        {
            var (vm, dialog, license) = CreateVm();
            dialog.OpenFileResult = "C:\\usb\\license.lic";
            license.Candidate = new LicenseEvaluation { Status = LicenseStatus.Valid, Message = "고객A / Standard" };

            await vm.ImportLicenseCommand.ExecuteAsync(null);

            Assert.True(license.ImportCalled);
            Assert.Contains(dialog.Infos, i => i.Contains("설치 완료"));
        }

        [Fact]
        public async Task Import_failure_shows_error()
        {
            var (vm, dialog, license) = CreateVm();
            dialog.OpenFileResult = "C:\\usb\\license.lic";
            license.ImportResult = new LicenseImportResult(false, "쓰기 거부");

            await vm.ImportLicenseCommand.ExecuteAsync(null);

            Assert.Contains(dialog.Errors, e => e.Contains("쓰기 거부"));
        }

        // ─── LicenseImportApplier (상승 모드 본체) ────────────────

        [Fact]
        public void Applier_rejects_tampered_file()
        {
            // 상승 컨텍스트 이중 방어 — payload 조작(서명 불일치) 파일은 설치 경로에 놓지 않는다
            var path = WriteSignedLicense();
            File.WriteAllText(path, File.ReadAllText(path).Replace("LIC-2026-0001", "LIC-9999-9999"));

            var result = LicenseImportApplier.Apply(path, _targetPath);

            Assert.False(result.Ok);
            Assert.False(File.Exists(_targetPath));
        }
    }
}
