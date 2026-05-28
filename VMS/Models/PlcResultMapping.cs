using VMS.PLC.Models;

namespace VMS.Models
{
    /// <summary>
    /// PLC / IO 보드 결과 매핑 항목 (런타임 전용 POCO)
    /// </summary>
    public class PlcResultMapping
    {
        public string ResultKey { get; set; } = "Success";

        /// <summary>
        /// 출력 대상 디바이스 식별자 (Phase B).
        /// "MainPLC" = 기본 PLC, 그 외 = IO 보드 (IoDeviceRegistry 등록 DeviceId).
        /// 기존 레시피에서 필드 누락 시 "MainPLC" 로 폴백.
        /// </summary>
        public string DeviceId { get; set; } = "MainPLC";

        /// <summary>
        /// 출력 주소 — PLC: 어드레스 문자열 / IO 보드: 채널 번호 문자열.
        /// </summary>
        public string PlcAddress { get; set; } = string.Empty;

        public PlcDataType DataType { get; set; } = PlcDataType.Bit;
    }
}
