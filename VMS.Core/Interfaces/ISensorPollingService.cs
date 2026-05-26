using System;
using System.Threading.Tasks;

namespace VMS.Core.Interfaces
{
    /// <summary>
    /// Predictive_DefectRate_Plan §5.2 — VMS → Web 환경 센서 시계열 전송 서비스.
    /// IEnvironmentSensorReader 가 반환한 값을 주기적으로 POST.
    /// 센서 미연결(모든 null) 시 자동 송신 skip — 네트워크/Web 부하 0.
    /// </summary>
    public interface ISensorPollingService : IDisposable
    {
        /// <summary>주기적 폴링 시작 (Plan §5.2 권장: 5초).</summary>
        void StartPeriodicPolling(int intervalSeconds = 5);

        /// <summary>즉시 한 번 폴링 (시작 직후/수동 트리거).</summary>
        Task PollOnceAsync();
    }
}
