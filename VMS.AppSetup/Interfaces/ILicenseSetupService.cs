using VMS.Core.Security.Licensing;

namespace VMS.AppSetup.Interfaces
{
    public sealed record LicenseImportResult(bool Success, string? Error);

    /// <summary>
    /// SW 라이선스 활성화 카드의 서비스 — docs/design/license-spec.md §6.
    /// 지문 표시·상태 조회·가져올 파일 사전 검증은 현재 권한으로 수행하고,
    /// ProgramData 설치 경로 쓰기가 거부되는 경우에만 UAC 상승 재실행
    /// (<see cref="Services.LicenseImportApplier"/>) 으로 위임한다.
    /// </summary>
    public interface ILicenseSetupService
    {
        /// <summary>이 PC 의 지문 코드 (XXXXX-XXXXX-XXXXX) — 발급 요청 시 본사에 전달.</summary>
        string GetMachineFingerprint();

        /// <summary>설치된 license.lic 의 현재 상태 평가 (없으면 Missing).</summary>
        LicenseEvaluation GetStatus();

        /// <summary>가져올 파일의 사전 검증 — 설치 전에 서명/지문/만료를 확인.</summary>
        LicenseEvaluation ValidateCandidate(string filePath);

        /// <summary>
        /// 검증된 파일을 설치 경로(C:\ProgramData\BODA\VMS\license.lic)로 복사.
        /// 서명이 유효하지 않은 파일은 거부한다. 쓰기 권한 부족 시 UAC 프롬프트 발생.
        /// </summary>
        Task<LicenseImportResult> ImportAsync(string sourcePath);
    }
}
