using VMS.PLC.Models.Sequence;

namespace VMS.PLC.Models
{
    /// <summary>
    /// Container for all PLC signal mappings used by AutoProcess.
    /// </summary>
    public class PlcSignalConfiguration
    {
        /// <summary>Signal maps for each camera channel</summary>
        public List<PlcSignalMap> SignalMaps { get; set; } = new();

        /// <summary>Heartbeat interval in milliseconds</summary>
        public int HeartbeatIntervalMs { get; set; } = 1000;

        /// <summary>Trigger polling interval in milliseconds</summary>
        public int TriggerPollingIntervalMs { get; set; } = 10;

        /// <summary>
        /// Reset 신호 PLC 주소.
        /// 조건 충족 감지 시 시퀀스를 즉시 Start로 복귀시킨다.
        /// 빈 문자열이면 비활성.
        /// </summary>
        public string ResetSignalAddress { get; set; } = string.Empty;

        /// <summary>Reset 신호 체크 모드 (BitOn/BitOff/WordEquals 등)</summary>
        public InputCheckMode ResetSignalCheckMode { get; set; } = InputCheckMode.BitOn;

        /// <summary>Reset 신호 Word 비교값</summary>
        public int? ResetSignalCompareValue { get; set; }
    }
}
