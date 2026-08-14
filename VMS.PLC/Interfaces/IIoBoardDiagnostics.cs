namespace VMS.PLC.Interfaces
{
    /// <summary>
    /// 연결 실패 사유를 노출하는 IO 디바이스 — 호출자(App 부팅, 시퀀스 편집기)가 로그에 남긴다.
    ///
    /// IIoBoardConnection 본체에 넣지 않은 이유: Mock 구현들은 실패할 일이 없어 의미가 없고,
    /// 진단이 필요한 실 SDK 구현(ADLink/Advantech)만 선택적으로 구현하면 되기 때문.
    /// 사용 측은 <c>board is IIoBoardDiagnostics d ? d.LastError : null</c> 형태로 조회.
    /// </summary>
    public interface IIoBoardDiagnostics
    {
        /// <summary>마지막 연결 실패 사유 (성공했거나 시도 전이면 null).</summary>
        string? LastError { get; }
    }
}
