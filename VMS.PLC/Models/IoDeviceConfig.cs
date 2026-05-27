namespace VMS.PLC.Models
{
    /// <summary>
    /// IO 보드 1개에 대한 설정. SystemConfiguration.IoBoards 리스트에 N개 등록 가능.
    /// IoBoardConnectionFactory.Create(IoDeviceConfig) 로 적절한 IIoBoardConnection 구현체 생성.
    /// </summary>
    public class IoDeviceConfig
    {
        /// <summary>
        /// 디바이스 고유 식별자. SequenceNodeConfig.DeviceId 와 매칭됨.
        /// 예: "ADLink_1", "Advantech_Main", "Backup_IO" 등 운영자가 자유롭게 명명.
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>제조사 (ADLink / Advantech).</summary>
        public IoBoardVendor Vendor { get; set; } = IoBoardVendor.None;

        /// <summary>
        /// 보드 모델 (예: "PCI-7432", "PCI-1716"). 같은 Vendor 라도 모델별로 채널 구성 다름.
        /// Factory 에서 Vendor + Model 조합으로 적절한 SDK 초기화 분기.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Driver-level board index. ADLink DASK 는 0,1,... 순으로 카운트.
        /// Advantech DAQNavi 는 device description 문자열 ("BID#0") 사용 — Factory 가 변환.
        /// </summary>
        public int BoardId { get; set; }

        /// <summary>
        /// 입력 채널 수 (보드 모델에 따라 결정 — 운영자가 모델 선택 시 자동 채움 또는 수동 지정).
        /// </summary>
        public int InputChannelCount { get; set; } = 16;

        /// <summary>출력 채널 수.</summary>
        public int OutputChannelCount { get; set; } = 16;

        /// <summary>활성 여부 (false 면 Factory 가 인스턴스 생성 skip).</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>설명 — UI 표시용.</summary>
        public string Description { get; set; } = string.Empty;
    }
}
