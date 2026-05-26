using VMS.Core.Interfaces;
using VMS.Core.Models.Predictive;

namespace VMS.Core.Services
{
    /// <summary>
    /// 기본 reader — 모든 측정값 null 반환. = "PLC/센서 모듈 미연결" 상태.
    /// SensorPollingService 는 HasAnyReading=false 이면 송신 skip → 네트워크 부하 0.
    ///
    /// 운영 환경에서 실제 센서 reader (예: ModbusEnvironmentSensorReader) 가 준비되면
    /// App.xaml.cs 에서 IEnvironmentSensorReader 주입을 교체하기만 하면 즉시 동작.
    /// </summary>
    public class MockEnvironmentSensorReader : IEnvironmentSensorReader
    {
        public SensorReadingDto Read() => new();
    }
}
