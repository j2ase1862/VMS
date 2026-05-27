using VMS.PLC.Models;

namespace VMS.PLC.Interfaces
{
    /// <summary>
    /// Abstraction for PLC communication.
    /// All vendor implementations must be thread-safe.
    ///
    /// IIoDevice 상속 — SequenceEngine 이 PLC 와 IO 보드(IIoBoardConnection) 를 통일된
    /// 식별 모델로 다루기 위함. DeviceId/DeviceType 은 default interface methods 로
    /// "MainPLC"/Plc 기본값 제공 → 기존 구현체(Modbus/Mitsubishi/Siemens/LS/Omron/Simulated/
    /// ResilientPlcConnection) 변경 없이 자동 적용. 다중 PLC 지원 시점에 override.
    /// </summary>
    public interface IPlcConnection : IIoDevice
    {
        // ─── IIoDevice 기본값 (C# 8 default interface methods) ───
        // DeviceId/DeviceType 는 default → 모든 PLC 구현체가 자동으로 "MainPLC"/Plc.
        // IsConnected 는 IIoDevice 에서 상속 — 구현체가 한 번만 구현.
        string IIoDevice.DeviceId => "MainPLC";
        IoDeviceType IIoDevice.DeviceType => IoDeviceType.Plc;

        /// <summary>Current connection state</summary>
        PlcConnectionState ConnectionState { get; }

        /// <summary>Fired when the connection state changes</summary>
        event EventHandler<PlcConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>Connect to PLC with the given configuration</summary>
        Task<bool> ConnectAsync(PlcConnectionConfig config);

        /// <summary>Disconnect from PLC gracefully</summary>
        Task DisconnectAsync();

        // --- Bit operations ---

        /// <summary>Read a single bit from PLC</summary>
        Task<bool> ReadBitAsync(PlcAddress address);

        /// <summary>Write a single bit to PLC</summary>
        Task WriteBitAsync(PlcAddress address, bool value);

        // --- Word (16-bit) operations ---

        /// <summary>Read a single 16-bit word from PLC</summary>
        Task<short> ReadWordAsync(PlcAddress address);

        /// <summary>Write a single 16-bit word to PLC</summary>
        Task WriteWordAsync(PlcAddress address, short value);

        // --- DWord (32-bit) operations ---

        /// <summary>Read a single 32-bit double word from PLC</summary>
        Task<int> ReadDWordAsync(PlcAddress address);

        /// <summary>Write a single 32-bit double word to PLC</summary>
        Task WriteDWordAsync(PlcAddress address, int value);

        // --- Block operations ---

        /// <summary>Read multiple consecutive words starting from address</summary>
        Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count);

        /// <summary>Write multiple consecutive words starting from address</summary>
        Task WriteWordsAsync(PlcAddress startAddress, short[] values);

        // --- Monitoring (polling-based) ---

        /// <summary>Fired when a monitored bit changes value</summary>
        event EventHandler<PlcBitChangedEventArgs>? BitChanged;

        /// <summary>Start polling a bit address for changes</summary>
        Task StartMonitoringAsync(PlcAddress address, int pollingIntervalMs = 50);

        /// <summary>Stop monitoring a specific address</summary>
        Task StopMonitoringAsync(PlcAddress address);

        /// <summary>Stop all active monitors</summary>
        Task StopAllMonitoringAsync();
    }
}
