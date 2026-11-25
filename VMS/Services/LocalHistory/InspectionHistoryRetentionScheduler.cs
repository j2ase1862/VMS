using System;
using System.Diagnostics;
using System.Threading;
using VMS.Core.Retention;

namespace VMS.Services.LocalHistory
{
    /// <summary>
    /// 로컬 검사 이력(inspection_history.db) 보존 정리 — VMS 시작 2 분 뒤 1 회 + 켜 둔 동안 1 시간마다.
    ///
    /// <para>
    /// 종전에는 VMS 시작 때 한 번만 보존 일수로 지웠다. 몇 달씩 재시작하지 않는 라인에서는 보존 일수도
    /// 지켜지지 않고 DB 가 계속 커졌다. 행 수 상한(<see cref="InspectionHistoryOptions.MaxRows"/>)도 함께
    /// 적용해 고속 라인에서 디스크가 차지 않게 한다. 상한은 정리 사이(최대 1 시간) 생산량만큼 넘칠 수 있다.
    /// </para>
    /// <para>
    /// 이미지 보존 스케줄러(<see cref="ImageRetentionScheduler"/>)와 같은 방식 — 스레드 풀에서 낮은 우선순위로,
    /// 매번 설정을 다시 읽어 Retention Settings 에서 바꾼 값이 다음 회차부터 반영된다. enabled=false 면 DB 를 열지 않는다.
    /// 감사 로그는 시작 회차는 항상(종전과 같음), 주기 회차는 지운 행이 있을 때만 남긴다.
    /// </para>
    /// </summary>
    public sealed class InspectionHistoryRetentionScheduler : IDisposable
    {
        public static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

        private readonly Func<InspectionHistoryOptions> _optionsProvider;
        private readonly Func<ILocalInspectionHistoryStore> _storeProvider;
        private readonly TimeSpan _startupDelay;
        private readonly TimeSpan _interval;
        private readonly object _runLock = new();
        private Timer? _timer;
        private int _runs;

        public InspectionHistoryRetentionScheduler(Func<InspectionHistoryOptions> optionsProvider,
            Func<ILocalInspectionHistoryStore> storeProvider, TimeSpan? startupDelay = null, TimeSpan? interval = null)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));
            _startupDelay = startupDelay ?? DefaultStartupDelay;
            _interval = interval ?? DefaultInterval;
        }

        /// <summary>지금까지 정리를 돈 횟수(시험 · 진단용). 꺼져 있어 건너뛴 회차는 세지 않는다.</summary>
        public int RunCount => Volatile.Read(ref _runs);

        public void Start()
        {
            if (_timer != null) return;
            _timer = new Timer(_ => RunOnce(RunCount == 0 ? "Startup" : "Hourly"), null, _startupDelay, _interval);
        }

        /// <summary>
        /// 한 번 정리한다. 앞 회차가 아직 돌고 있거나 이력 저장이 꺼져 있으면 null. 예외를 던지지 않는다.
        /// </summary>
        public InspectionHistoryRetentionResult? RunOnce(string trigger)
        {
            if (!Monitor.TryEnter(_runLock)) return null;
            var thread = Thread.CurrentThread;
            var priority = thread.Priority;
            try
            {
                var options = _optionsProvider();
                if (!options.Enabled) return null;
                try { thread.Priority = ThreadPriority.BelowNormal; } catch { }
                var result = _storeProvider().ApplyRetention(options.RetentionDays, options.MaxRows, trigger,
                    auditWhenNothingDeleted: trigger == "Startup");
                Interlocked.Increment(ref _runs);
                if (result.Deleted > 0)
                    Debug.WriteLine($"[InspectionHistoryRetention] {trigger}: 보존 일수 {result.DeletedByAge} · 행 수 상한 {result.DeletedByMaxRows} 행 삭제, 남음 {result.Remaining}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InspectionHistoryRetention] {trigger} 실패: {ex.Message}");
                return null;
            }
            finally
            {
                try { thread.Priority = priority; } catch { }
                Monitor.Exit(_runLock);
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
