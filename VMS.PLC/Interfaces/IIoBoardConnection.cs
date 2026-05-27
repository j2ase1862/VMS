using System;
using System.Threading.Tasks;
using VMS.PLC.Models;

namespace VMS.PLC.Interfaces
{
    /// <summary>
    /// Digital IO 보드 (ADLink PCI-743x / Advantech PCI-17xx 등) 추상화.
    ///
    /// PLC 와 달리:
    ///   • 어드레스 = 정수 채널 번호 (PLC 의 string PlcAddress 와 다름)
    ///   • Word/DWord/Block 연산 없음 — 단순 bit + port (32-bit 묶음) 만
    ///   • 벤더 SDK 가 무거움 (DASK / DAQNavi) — Mock 구현이 기본, 실 SDK 는 Phase 3
    ///
    /// 모든 구현체는 thread-safe 해야 함.
    /// </summary>
    public interface IIoBoardConnection : IIoDevice
    {
        /// <summary>총 채널 수 (DI + DO 합산 또는 보드 모델에 따른 표기).</summary>
        int InputChannelCount { get; }

        /// <summary>출력 채널 수.</summary>
        int OutputChannelCount { get; }

        /// <summary>설정으로 연결 시도. 성공 시 true.</summary>
        Task<bool> ConnectAsync();

        /// <summary>정상 종료 (보드 driver handle 해제).</summary>
        Task DisconnectAsync();

        // --- Digital Input ---

        /// <summary>지정 채널의 디지털 입력 비트 read.</summary>
        Task<bool> ReadBitAsync(int channel);

        /// <summary>입력 포트(8 또는 16 또는 32 bit) 한 번에 read.</summary>
        Task<uint> ReadPortAsync(int portNo);

        // --- Digital Output ---

        /// <summary>지정 채널의 디지털 출력 비트 write.</summary>
        Task WriteBitAsync(int channel, bool value);

        /// <summary>출력 포트(8 또는 16 또는 32 bit) 한 번에 write.</summary>
        Task WritePortAsync(int portNo, uint value);

        // --- Monitoring ---

        /// <summary>모니터링 중인 채널의 비트 값이 변할 때 발생.</summary>
        event EventHandler<IoBitChangedEventArgs>? BitChanged;

        /// <summary>지정 채널 폴링 시작.</summary>
        Task StartMonitoringAsync(int channel, int pollingIntervalMs = 50);

        /// <summary>지정 채널 폴링 중지.</summary>
        Task StopMonitoringAsync(int channel);

        /// <summary>모든 모니터 중지.</summary>
        Task StopAllMonitoringAsync();
    }
}
