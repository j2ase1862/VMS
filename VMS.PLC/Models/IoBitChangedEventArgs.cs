using System;

namespace VMS.PLC.Models
{
    /// <summary>
    /// IIoBoardConnection.BitChanged 이벤트 인자 — 채널 비트 상태 변화.
    /// PlcBitChangedEventArgs 와 별도 — PLC 는 string 어드레스, IO 보드는 정수 채널.
    /// </summary>
    public class IoBitChangedEventArgs : EventArgs
    {
        /// <summary>변화가 발생한 채널 번호 (0 base).</summary>
        public int Channel { get; }

        /// <summary>새 비트 값.</summary>
        public bool Value { get; }

        /// <summary>변화 발생 시각 (UTC).</summary>
        public DateTime Timestamp { get; }

        public IoBitChangedEventArgs(int channel, bool value)
        {
            Channel = channel;
            Value = value;
            Timestamp = DateTime.UtcNow;
        }
    }
}
