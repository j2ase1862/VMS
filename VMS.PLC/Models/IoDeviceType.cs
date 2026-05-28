namespace VMS.PLC.Models
{
    /// <summary>
    /// IO 디바이스 종류 — IIoDevice 의 DeviceType 식별자.
    /// SequenceEngine 의 Input Check / Output Action 노드가 디바이스 타입에 따라
    /// PLC 어드레스(string) 또는 보드 채널(int) 로 분기.
    /// </summary>
    public enum IoDeviceType
    {
        /// <summary>알 수 없음 (default).</summary>
        Unknown,

        /// <summary>PLC (Modbus/Mitsubishi/Siemens/LS/Omron) — IPlcConnection 구현체.</summary>
        Plc,

        /// <summary>ADLink PCI-743x 시리즈 디지털 IO 보드 — IIoBoardConnection 구현체.</summary>
        AdLinkPci743x,

        /// <summary>Advantech PCI-17xx 시리즈 디지털 IO 보드 — IIoBoardConnection 구현체.</summary>
        AdvantechPci17xx
    }
}
