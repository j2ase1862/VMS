using System.Collections.Concurrent;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// In-memory simulated PLC connection for development and testing.
    /// All read/write operations work against in-memory dictionaries.
    /// </summary>
    public class SimulatedPlcConnection : IPlcConnection
    {
        private readonly ConcurrentDictionary<string, short> _wordMemory = new();
        private readonly ConcurrentDictionary<string, bool> _bitMemory = new();
        private readonly ConcurrentDictionary<string, (PlcAddress Address, int IntervalMs, CancellationTokenSource Cts)> _monitors = new();
        private readonly SemaphoreSlim _lock = new(1, 1);

        private PlcConnectionState _connectionState = PlcConnectionState.Disconnected;
        private int _simulatedDelayMs;
        private bool _disposed;

        public bool IsConnected => _connectionState == PlcConnectionState.Connected;
        public PlcConnectionState ConnectionState => _connectionState;

        public event EventHandler<PlcBitChangedEventArgs>? BitChanged;
        public event EventHandler<PlcConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>
        /// Optional simulated response delay in milliseconds.
        /// Set to 0 for instant responses (default).
        /// </summary>
        public int SimulatedDelayMs
        {
            get => _simulatedDelayMs;
            set => _simulatedDelayMs = Math.Max(0, value);
        }

        public async Task<bool> ConnectAsync(PlcConnectionConfig config)
        {
            SetConnectionState(PlcConnectionState.Connecting);
            await SimulateDelay();
            SetConnectionState(PlcConnectionState.Connected);
            return true;
        }

        public async Task DisconnectAsync()
        {
            await StopAllMonitoringAsync();
            SetConnectionState(PlcConnectionState.Disconnected);
        }

        // --- Bit operations ---

        public async Task<bool> ReadBitAsync(PlcAddress address)
        {
            await SimulateDelay();
            var key = GetBitKey(address);
            return _bitMemory.GetValueOrDefault(key, false);
        }

        public async Task WriteBitAsync(PlcAddress address, bool value)
        {
            await SimulateDelay();
            var key = GetBitKey(address);
            _bitMemory[key] = value;
        }

        // --- Word (16-bit) operations ---

        public async Task<short> ReadWordAsync(PlcAddress address)
        {
            await SimulateDelay();
            var key = GetWordKey(address);
            return _wordMemory.GetValueOrDefault(key, (short)0);
        }

        public async Task WriteWordAsync(PlcAddress address, short value)
        {
            await SimulateDelay();
            var key = GetWordKey(address);
            _wordMemory[key] = value;
        }

        // --- DWord (32-bit) operations ---

        public async Task<int> ReadDWordAsync(PlcAddress address)
        {
            await SimulateDelay();
            var keyLow = GetWordKey(address);
            var keyHigh = GetWordKey(address, 1);
            short low = _wordMemory.GetValueOrDefault(keyLow, (short)0);
            short high = _wordMemory.GetValueOrDefault(keyHigh, (short)0);
            return (high << 16) | (ushort)low;
        }

        public async Task WriteDWordAsync(PlcAddress address, int value)
        {
            await SimulateDelay();
            var keyLow = GetWordKey(address);
            var keyHigh = GetWordKey(address, 1);
            _wordMemory[keyLow] = (short)(value & 0xFFFF);
            _wordMemory[keyHigh] = (short)((value >> 16) & 0xFFFF);
        }

        // --- Block operations ---

        public async Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count)
        {
            await SimulateDelay();
            var result = new short[count];
            for (int i = 0; i < count; i++)
            {
                var key = GetWordKey(startAddress, i);
                result[i] = _wordMemory.GetValueOrDefault(key, (short)0);
            }
            return result;
        }

        public async Task WriteWordsAsync(PlcAddress startAddress, short[] values)
        {
            await SimulateDelay();
            for (int i = 0; i < values.Length; i++)
            {
                var key = GetWordKey(startAddress, i);
                _wordMemory[key] = values[i];
            }
        }

        // --- Monitoring ---

        public Task StartMonitoringAsync(PlcAddress address, int pollingIntervalMs = 50)
        {
            var key = GetBitKey(address);
            if (_monitors.ContainsKey(key))
                return Task.CompletedTask;

            var cts = new CancellationTokenSource();
            _monitors[key] = (address, pollingIntervalMs, cts);

            _ = MonitorLoopAsync(address, key, pollingIntervalMs, cts.Token);

            return Task.CompletedTask;
        }

        public Task StopMonitoringAsync(PlcAddress address)
        {
            var key = GetBitKey(address);
            if (_monitors.TryRemove(key, out var entry))
            {
                entry.Cts.Cancel();
                entry.Cts.Dispose();
            }
            return Task.CompletedTask;
        }

        public Task StopAllMonitoringAsync()
        {
            foreach (var kvp in _monitors)
            {
                kvp.Value.Cts.Cancel();
                kvp.Value.Cts.Dispose();
            }
            _monitors.Clear();
            return Task.CompletedTask;
        }

        private async Task MonitorLoopAsync(PlcAddress address, string key, int intervalMs, CancellationToken ct)
        {
            bool lastValue = _bitMemory.GetValueOrDefault(key, false);

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    var currentValue = _bitMemory.GetValueOrDefault(key, false);
                    if (currentValue != lastValue)
                    {
                        lastValue = currentValue;
                        BitChanged?.Invoke(this, new PlcBitChangedEventArgs(address, currentValue));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
        }

        // --- Simulation helpers ---

        /// <summary>
        /// Directly set a bit value in simulation memory (for external test triggers).
        /// </summary>
        public void SimSetBit(string addressKey, bool value)
        {
            _bitMemory[addressKey] = value;
        }

        /// <summary>
        /// Directly set a word value in simulation memory.
        /// </summary>
        public void SimSetWord(string addressKey, short value)
        {
            _wordMemory[addressKey] = value;
        }

        /// <summary>
        /// Read a bit directly from simulation memory by key.
        /// </summary>
        public bool SimGetBit(string addressKey)
        {
            return _bitMemory.GetValueOrDefault(addressKey, false);
        }

        /// <summary>
        /// Read a word directly from simulation memory by key.
        /// </summary>
        public short SimGetWord(string addressKey)
        {
            return _wordMemory.GetValueOrDefault(addressKey, (short)0);
        }

        // --- Key helpers ---

        private static string GetBitKey(PlcAddress address)
        {
            return address.RawAddress.ToUpperInvariant();
        }

        private static string GetWordKey(PlcAddress address, int offsetDelta = 0)
        {
            return $"{address.DeviceCode}:{address.Offset + offsetDelta}";
        }

        private void SetConnectionState(PlcConnectionState newState, string? reason = null)
        {
            var oldState = _connectionState;
            if (oldState == newState) return;
            _connectionState = newState;
            ConnectionStateChanged?.Invoke(this, new PlcConnectionStateChangedEventArgs(oldState, newState, reason));
        }

        private async Task SimulateDelay()
        {
            if (_simulatedDelayMs > 0)
                await Task.Delay(_simulatedDelayMs);
        }

        // --- Dispose ---

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopAllMonitoringAsync().GetAwaiter().GetResult();
            _lock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
