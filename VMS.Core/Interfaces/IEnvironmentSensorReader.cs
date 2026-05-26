using VMS.Core.Models.Predictive;

namespace VMS.Core.Interfaces
{
    /// <summary>
    /// Predictive_DefectRate_Plan §5.2 — 환경 센서 측정 추상화.
    /// 현장별 다른 PLC/Modbus/시리얼 모듈을 통일된 인터페이스로 노출.
    ///
    /// 구현체:
    ///   • MockEnvironmentSensorReader  — 모든 readings null (= "센서 미연결" 안전 기본값)
    ///   • (future) ModbusEnvironmentSensorReader — VMS.PLC 의 ModbusTcpConnection 사용
    ///   • (future) SerialEnvironmentSensorReader  — RS-485 등
    ///
    /// 폴링 서비스(`ISensorPollingService`)가 5초 주기로 Read() 호출 →
    /// HasAnyReading=false 면 송신 skip(서버 400 reject 회피).
    ///
    /// 구현 시 주의: Read() 는 polling thread 에서 호출되므로 빠르게 반환해야 함.
    /// PLC 응답 타임아웃은 구현체가 내부에서 짧게 잡고 실패 시 null 반환.
    /// </summary>
    public interface IEnvironmentSensorReader
    {
        /// <summary>현재 시점의 환경 센서 측정값. 미연결/실패 시 모든 필드 null.</summary>
        SensorReadingDto Read();
    }
}
