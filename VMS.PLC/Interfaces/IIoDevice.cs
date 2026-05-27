using System;
using VMS.PLC.Models;

namespace VMS.PLC.Interfaces
{
    /// <summary>
    /// PLC + IO 보드 공통 추상화 — SequenceEngine 이 디바이스 종류에 무관하게 식별/관리할 수 있도록.
    ///
    /// 실제 read/write 시그니처는 종류별로 다르므로 (PLC=string PlcAddress, 보드=int channel)
    /// 하위 인터페이스 IPlcConnection / IIoBoardConnection 에 정의됨. 이 인터페이스는
    /// 최소 공통 식별 정보 + lifecycle 만 노출.
    ///
    /// 다중 디바이스 환경: SystemConfiguration.IoBoards 목록의 각 IoDeviceConfig 가
    /// DeviceId 로 디바이스를 식별. SequenceNodeConfig.DeviceId 가 null 이면 기본 PLC ("MainPLC") 사용.
    /// </summary>
    public interface IIoDevice : IDisposable
    {
        /// <summary>디바이스 고유 식별자 (예: "MainPLC", "ADLink_1", "Advantech_1").</summary>
        string DeviceId { get; }

        /// <summary>디바이스 종류 (Plc / AdLinkPci743x / AdvantechPci17xx).</summary>
        IoDeviceType DeviceType { get; }

        /// <summary>현재 연결 상태.</summary>
        bool IsConnected { get; }
    }
}
