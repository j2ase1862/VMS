namespace VMS.Core.Services
{
    /// <summary>
    /// 라인 PC 의 "검사 중" 신호 — VMS 가 택트를 지켜야 하는 구간(AUTO RUN · Roller 연속 검사) 동안
    /// 명명 Mutex 핸들을 쥐고, 같은 PC 의 MLOps 학습 워커는 그 객체가 있으면 학습을 받지 않거나 중단한다.
    ///   · 존재 자체가 신호다. 소유(WaitOne)는 쓰지 않는다 — 소유는 스레드에 묶이는데 VMS 는 시작·정지 스레드가 다르다.
    ///   · 커널 객체는 마지막 핸들이 닫힐 때 사라진다 → 여러 인스턴스·여러 구간이 각자 쥐어도 먼저 끝난 쪽이 지우지 못한다.
    ///   · VMS 가 비정상 종료하면 운영체제가 핸들을 닫는다 (파일 표시는 남아서 워커가 영원히 멈춘다).
    ///   · Global\ 접두사 필수 — 워커는 LocalSystem 서비스(세션 0), VMS 는 사용자 세션이다.
    ///     인스턴스 이름(--instance)을 붙이지 않는다 — PC 단위 신호다.
    /// 규약 변경 시 MLOps 학습 워커와 함께 갱신할 것 (MLOps 개발 문서 §5.6).
    /// </summary>
    public static class InspectionActivitySignal
    {
        /// <summary>신호 객체 이름 — MLOps 학습 워커와의 규약이므로 바꾸지 않는다.</summary>
        public const string MutexName = @"Global\BODA.VMS.InspectionActive";

        /// <summary>
        /// VMS 쪽 — 검사 구간 시작 시 호출해 반환된 핸들을 쥐고, 구간이 끝나면 Dispose 한다.
        /// 실패하면 null 과 사유를 돌려준다 (예외를 던지지 않는다 — 신호 때문에 검사를 막지 않는다).
        /// </summary>
        public static IDisposable? TryAcquire(out string? error) => TryAcquire(MutexName, out error);

        /// <summary>
        /// 워커 쪽 — 지금 이 PC 에서 검사 중인지. 확인 후 핸들은 곧바로 닫는다 (쥐고 있으면 객체 수명을 늘린다).
        /// 객체는 있는데 열 권한이 없으면 검사 중으로 본다 (안전 쪽).
        /// </summary>
        public static bool IsActive() => IsActive(MutexName);

        internal static IDisposable? TryAcquire(string name, out string? error)
        {
            try
            {
                error = null;
                return new Mutex(initiallyOwned: false, name);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }

        internal static bool IsActive(string name)
        {
            try
            {
                if (!Mutex.TryOpenExisting(name, out var mutex)) return false;
                mutex.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }
}
