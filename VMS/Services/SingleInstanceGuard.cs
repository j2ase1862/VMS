using System;
using System.Threading;

namespace VMS.Services
{
    /// <summary>
    /// 같은 이름의 인스턴스가 한 PC(로그인 세션)에서 두 번 뜨지 않게 하는 잠금 (VM-43).
    ///
    /// <para>
    /// 인스턴스(--instance)마다 설정 파일 · 프레임 공유 이름 · 카메라를 따로 쓴다. 같은 인스턴스를 두 번 띄우면
    /// 두 프로세스가 같은 설정 파일과 공유 메모리 이름을 함께 써서, 설정이 덮이고 프레임 주고받기가 엉킨다.
    /// 다른 이름의 인스턴스는 막지 않는다.
    /// </para>
    /// <para>
    /// 잠깐 기다린다 — 재시작(업데이트 설치 뒤 등)으로 이전 프로세스가 막 끝나는 중일 수 있다.
    /// 이전 프로세스가 비정상 종료해 잠금을 놓지 못했으면(abandoned) 넘겨받는다.
    /// </para>
    /// </summary>
    public sealed class SingleInstanceGuard : IDisposable
    {
        private Mutex? _mutex;

        private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

        /// <summary>
        /// 잠금을 얻으면 그 잠금, 이미 다른 프로세스(또는 스레드)가 쥐고 있으면 null.
        /// 얻은 잠금은 프로세스가 끝날 때까지 쥐고 있으면 된다(끝나면 OS 가 푼다).
        /// </summary>
        public static SingleInstanceGuard? TryAcquire(string mutexName, TimeSpan wait)
        {
            var mutex = new Mutex(initiallyOwned: false, mutexName);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(wait);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;   // 이전 프로세스가 비정상 종료 — 잠금은 이제 우리 것
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }
            return new SingleInstanceGuard(mutex);
        }

        public void Dispose()
        {
            var m = Interlocked.Exchange(ref _mutex, null);
            if (m == null) return;
            try { m.ReleaseMutex(); } catch (ApplicationException) { /* 다른 스레드에서 해제 — 프로세스 종료가 푼다 */ }
            m.Dispose();
        }
    }
}
