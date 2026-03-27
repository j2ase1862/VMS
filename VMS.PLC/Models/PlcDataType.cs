namespace VMS.PLC.Models
{
    /// <summary>
    /// PLC에 기록할 데이터 타입 (Vision Tool 결과 전송용)
    /// </summary>
    public enum PlcDataType
    {
        /// <summary>
        /// bool → WriteBitAsync (Success/Fail 판정)
        /// </summary>
        Bit,

        /// <summary>
        /// short → WriteWordAsync (16비트 정수)
        /// </summary>
        Int16,

        /// <summary>
        /// int → WriteDWordAsync (32비트 정수)
        /// </summary>
        Int32,

        /// <summary>
        /// float → WriteDWordAsync (IEEE 754 raw bits)
        /// </summary>
        Float
    }
}
