using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VMS.AppSetup.Interfaces;
using VMS.Core.Security.Licensing;

namespace VMS.AppSetup.Services
{
    /// <summary>
    /// SW 라이선스 활성화 카드의 서비스 구현 — 조회/검증은 현재 권한, 설치 경로 쓰기가
    /// 거부될 때만 AppSetup 자신을 UAC 상승 재실행 (WebServerSetupService 와 동일 패턴).
    /// ProgramData 하위는 통상 사용자도 새 파일 생성이 가능하므로 대부분 상승 없이 끝난다 —
    /// 다른 계정이 만든 기존 파일을 덮어쓰는 재발급 경로에서만 UAC 가 뜬다.
    /// </summary>
    public class LicenseSetupService : ILicenseSetupService
    {
        private readonly string _targetPath;
        private readonly IReadOnlyDictionary<string, string>? _keyring;
        private readonly string? _fingerprintOverride;
        private readonly bool _allowElevation;

        public LicenseSetupService()
            : this(LicenseFileStore.DefaultPath, keyring: null, fingerprintOverride: null, allowElevation: true)
        {
        }

        // 테스트 전용 — 임시 경로/임시 키쌍/고정 지문으로 검증 로직 격리 (InternalsVisibleTo)
        internal LicenseSetupService(
            string targetPath,
            IReadOnlyDictionary<string, string>? keyring,
            string? fingerprintOverride,
            bool allowElevation = false)
        {
            _targetPath = targetPath;
            _keyring = keyring;
            _fingerprintOverride = fingerprintOverride;
            _allowElevation = allowElevation;
        }

        public string GetMachineFingerprint() => _fingerprintOverride ?? MachineFingerprint.GetCode();

        public LicenseEvaluation GetStatus()
        {
            var (json, error) = LicenseFileStore.TryRead(_targetPath);
            if (error != null)
                return new LicenseEvaluation { Status = LicenseStatus.Invalid, Message = $"license.lic 읽기 실패: {error}" };
            if (json == null)
                return new LicenseEvaluation
                {
                    Status = LicenseStatus.Missing,
                    Message = "라이선스가 설치되어 있지 않습니다."
                };
            return LicenseValidator.Validate(json, GetMachineFingerprint(),
                DateOnly.FromDateTime(DateTime.Now), _keyring);
        }

        public LicenseEvaluation ValidateCandidate(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return new LicenseEvaluation { Status = LicenseStatus.Invalid, Message = $"파일이 없습니다: {filePath}" };
                return LicenseValidator.Validate(File.ReadAllText(filePath), GetMachineFingerprint(),
                    DateOnly.FromDateTime(DateTime.Now), _keyring);
            }
            catch (Exception ex)
            {
                return new LicenseEvaluation { Status = LicenseStatus.Invalid, Message = $"파일 읽기 실패: {ex.Message}" };
            }
        }

        public async Task<LicenseImportResult> ImportAsync(string sourcePath)
        {
            // 서명 위조/손상은 설치 자체를 거부 (상승 모드에서도 이중 검증 — LicenseImportApplier)
            var eval = ValidateCandidate(sourcePath);
            if (eval.Status == LicenseStatus.Invalid)
                return new LicenseImportResult(false, eval.Message);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_targetPath)!);
                File.Copy(sourcePath, _targetPath, overwrite: true);
                return new LicenseImportResult(true, null);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                if (!_allowElevation)
                    return new LicenseImportResult(false, $"설치 경로에 쓰지 못했습니다: {ex.Message}");
                return await RunElevatedImportAsync(sourcePath);
            }
        }

        /// <summary>
        /// AppSetup 자신을 UAC 상승으로 재실행해 "--import-license" 모드를 수행하고
        /// 결과 파일로 성공 여부를 판정한다 (WebServerSetupService.RunElevatedAsync 규약).
        /// </summary>
        private static async Task<LicenseImportResult> RunElevatedImportAsync(string sourcePath)
        {
            var resultPath = Path.Combine(Path.GetTempPath(),
                $"boda-lic-import-{Guid.NewGuid():N}.result.json");
            try
            {
                var exePath = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "VMS.AppSetup.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas", // UAC 상승 — 거부 시 Win32Exception(1223)
                };
                psi.ArgumentList.Add(LicenseImportApplier.ArgName);
                psi.ArgumentList.Add(sourcePath);
                psi.ArgumentList.Add(resultPath);

                using var proc = Process.Start(psi);
                if (proc == null)
                    return new LicenseImportResult(false, "적용 프로세스를 시작하지 못했습니다.");
                await proc.WaitForExitAsync();

                if (File.Exists(resultPath))
                {
                    var result = JsonSerializer.Deserialize<LicenseImportApplier.Result>(
                        await File.ReadAllTextAsync(resultPath));
                    if (result != null)
                        return new LicenseImportResult(result.Ok, result.Error);
                }
                return new LicenseImportResult(false, $"적용 결과를 확인하지 못했습니다 (exit={proc.ExitCode}).");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new LicenseImportResult(false, "관리자 권한 승인이 거부되어 설치하지 못했습니다.");
            }
            finally
            {
                try { if (File.Exists(resultPath)) File.Delete(resultPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
