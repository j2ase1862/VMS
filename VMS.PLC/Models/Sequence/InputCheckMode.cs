namespace VMS.PLC.Models.Sequence
{
    /// <summary>
    /// PLC 입력 체크 조건 모드
    /// </summary>
    public enum InputCheckMode
    {
        BitOn,
        BitOff,
        WordEquals,
        WordGreaterThan,
        WordLessThan,

        /// <summary>
        /// OFF→ON 전환(rising edge)에서만 통과 — 신호가 이미 ON 인 채 유지되고 있으면
        /// 한 번 OFF 로 떨어질 때까지 재통과하지 않는다. 스위치를 길게 누르고 있는 동안
        /// 사이클 시간마다 트리거가 반복 성립하던 문제의 해법 (2026-08-19 현장).
        /// BitOn 은 레벨 검사(상태 확인용)로 그대로 유지 — 트리거 대기에는 이 모드 권장.
        /// </summary>
        BitRisingEdge,

        /// <summary>ON→OFF 전환(falling edge)에서만 통과.</summary>
        BitFallingEdge
    }
}
