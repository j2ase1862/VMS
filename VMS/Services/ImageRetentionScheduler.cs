using System;
using System.Diagnostics;
using System.Threading;
using VMS.Core.Imaging;

namespace VMS.Services
{
    /// <summary>
    /// 검사 이미지 보존 기간 정리 — VMS 시작 때 1회 + 실행 중 하루 1회 (VM-41).
    ///
    /// <para>
    /// 이미지 저장 설정의 보존 기간 칸은 "시작 시 자동 삭제" 라고 안내했지만, 정리를 부르는 곳이
    /// <c>VMS.exe --cleanup-images</c>(예약 작업용) 하나뿐이고 그 예약 작업을 만드는 설치 단계도 없어
    /// 아무도 지우지 않았다. 앱이 직접 돌린다. 예약 작업으로 따로 돌려도 함께 써도 문제없다(이미 지운 폴더는 건너뜀).
    /// </para>
    /// <para>
    /// 운전을 방해하지 않게 — 스레드 풀에서 낮은 우선순위로, 시작 직후 초기화와 겹치지 않게 조금 늦춰 돈다.
    /// 매번 설정을 다시 읽으므로 보존 기간을 바꾸면 다음 회차부터 반영된다. 지운 폴더는 감사 로그에 남는다.
    /// </para>
    /// </summary>
    public sealed class ImageRetentionScheduler : IDisposable
    {
        public static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(24);

        private readonly Func<ImageSaveOptions?> _optionsProvider;
        private readonly TimeSpan _startupDelay;
        private readonly TimeSpan _interval;
        private readonly object _runLock = new();
        private Timer? _timer;
        private int _runs;

        public ImageRetentionScheduler(Func<ImageSaveOptions?> optionsProvider,
            TimeSpan? startupDelay = null, TimeSpan? interval = null)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _startupDelay = startupDelay ?? DefaultStartupDelay;
            _interval = interval ?? DefaultInterval;
        }

        /// <summary>지금까지 정리를 돈 횟수(시험 · 진단용).</summary>
        public int RunCount => Volatile.Read(ref _runs);

        public void Start()
        {
            if (_timer != null) return;
            _timer = new Timer(_ => RunOnce(_runs == 0 ? "Startup" : "Daily"), null, _startupDelay, _interval);
        }

        /// <summary>한 번 정리한다. 앞 회차가 아직 돌고 있으면 건너뛴다. 예외를 던지지 않는다.</summary>
        public ImageRetentionCleaner.Result? RunOnce(string trigger)
        {
            if (!Monitor.TryEnter(_runLock)) return null;
            var thread = Thread.CurrentThread;
            var priority = thread.Priority;
            try
            {
                try { thread.Priority = ThreadPriority.BelowNormal; } catch { }
                var result = ImageRetentionCleaner.CleanupAndAudit(_optionsProvider(), trigger);
                Interlocked.Increment(ref _runs);
                if (result.Deleted.Count > 0)
                    Debug.WriteLine($"[ImageRetention] {trigger}: {result.Deleted.Count} folder(s) removed");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageRetention] {trigger} 실패: {ex.Message}");
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
