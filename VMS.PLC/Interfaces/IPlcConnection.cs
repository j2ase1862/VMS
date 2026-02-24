using VMS.PLC.Models;

namespace VMS.PLC.Interfaces
{
    /// <summary>
    /// Abstraction for PLC communication.
    /// All vendor implementations must be thread-safe.
    /// </summary>
    public interface IPlcConnection : IDisposable
    {
        /// <summary>Whether the connection is currently active</summary>
        bool IsConnected { get; }

        /// <summary>Current connection state</summary>
        PlcConnectionState ConnectionState { get; }

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
