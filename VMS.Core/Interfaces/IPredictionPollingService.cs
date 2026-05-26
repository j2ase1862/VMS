using System;
using System.Threading.Tasks;
using VMS.Core.Models.Predictive;

namespace VMS.Core.Interfaces
{
    /// <summary>
    /// Predictive_DefectRate_Plan §5.3 — VMS 메인 화면 위젯용 Web 예측 폴링 서비스.
    /// HeartbeatService/ParameterSyncService 와 동일 패턴 — HttpClient + Timer.
    /// ViewModel 은 PredictionUpdated 이벤트만 구독.
    /// </summary>
    public interface IPredictionPollingService : IDisposable
    {
        /// <summary>
        /// 폴링 결과 수신 시 발생. 실패 시에도 Status="error"/"no_model" 등으로 전달.
        /// UI 스레드 마샬링은 구독자 책임.
        /// </summary>
        event Action<PredictionCurrentDto>? PredictionUpdated;

        /// <summary>가장 최근 폴링 결과(없으면 null).</summary>
        PredictionCurrentDto? Current { get; }

        /// <summary>주기적 폴링 시작 (기본 60초 — Web PredictionService 캐시 TTL 과 일치).</summary>
        void StartPeriodicPolling(int intervalSeconds = 60);

        /// <summary>즉시 한 번 폴링 (시작 직후/수동 새로고침).</summary>
        Task PollAsync();
    }
}
