using System;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Models.Updates;

namespace VMS.Core.Interfaces
{
    /// <summary>
    /// 인앱 업데이트 설치기 — MSI 다운로드(무결성 검증 포함) + 설치 부트스트래퍼 실행.
    /// IUpdateService(체크 전용)와 분리: 체크는 시작 시 best-effort, 설치는 명시적 사용자 동작.
    ///
    /// 흐름: DownloadAsync 로 MSI 확보 → LaunchInstaller 로 상승(UAC) 부트스트랩 스크립트 실행
    /// → 호출 측이 앱 종료 → 스크립트가 msiexec 설치 + Web 서비스 상태 복원 + VMS 재실행.
    /// </summary>
    public interface IUpdateInstallService : IDisposable
    {
        /// <summary>
        /// MSI 자산을 임시 폴더로 스트리밍 다운로드하고 SHA-256(제공 시)을 검증한다.
        /// 이미 검증된 동일 파일이 있으면 재다운로드 없이 즉시 성공 반환.
        /// 예외를 발산하지 않고 실패는 결과 객체로 보고 (취소는 OperationCanceledException 전파).
        /// </summary>
        Task<UpdateDownloadResult> DownloadAsync(
            UpdateInfo info,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 상승(UAC) 부트스트랩 스크립트를 실행한다. 성공 반환 직후 호출 측은 앱을 종료해야
        /// 설치가 진행된다 (스크립트가 현재 프로세스 종료를 대기).
        /// </summary>
        UpdateInstallLaunchResult LaunchInstaller(string msiPath);
    }
}
