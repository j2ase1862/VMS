using System;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Models.Updates;

namespace VMS.Core.Interfaces
{
    /// <summary>
    /// 최신 릴리스 조회 + 현재 버전 비교를 수행하는 업데이트 알림 체커.
    /// 실제 다운로드/설치는 다루지 않음 — 사용자가 브라우저로 직접 받아 실행.
    /// 구현체는 통신 실패/파싱 실패 시 예외를 발산하지 않고 null 반환 (best-effort).
    /// </summary>
    public interface IUpdateService : IDisposable
    {
        /// <summary>
        /// 원격 릴리스 정보를 조회하고 현재 버전과 비교하여 UpdateInfo 반환.
        /// 실패 시 null. 호출 측은 null 을 "정보 없음" 으로 처리해야 한다.
        /// </summary>
        Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);
    }
}
