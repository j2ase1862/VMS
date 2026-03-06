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
        WordLessThan
    }
}
