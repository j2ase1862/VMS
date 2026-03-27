using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Decorator that adds automatic reconnection, keep-alive monitoring,
    /// and structured logging to any IPlcConnection implementation.
    /// </summary>
    public class ResilientPlcConnection : IPlcConnection
    {
        private readonly IPlcConnection _inner;
        private PlcConnectionConfig _config = new();
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private CancellationTokenSource? _keepAliveCts;
        private Task? _keepAliveTask;
        private bool _disposed;

        public int MaxRetryCount { get; set; } = 3;
        public int RetryIntervalMs { get; set; } = 2000;
        public int KeepAliveIntervalMs { get; set; } = 5000;

        /// <summary>
        /// Callback for structured log entries. Wire this to your logging infrastructure.
        /// </summary>
        public Action<PlcLogEntry>? LogCallback { get; set; }

        public bool IsConnected => _inner.IsConnected;
        public PlcConnectionState ConnectionState => _inner.ConnectionState;

        public event EventHandler<PlcBitChangedEventArgs>? BitChanged;
        public event EventHandler<PlcConnectionStateChangedEventArgs>? ConnectionStateChanged;

        public ResilientPlcConnection(IPlcConnection inner)
        {
            _inner = inner;
            _inner.BitChanged += (s, e) => BitChanged?.Invoke(this, e);
            _inner.ConnectionStateChanged += OnInnerConnectionStateChanged;
        }

        private void OnInnerConnectionStateChanged(object? sender, PlcConnectionStateChangedEventArgs e)
        {
            Log(PlcLogLevel.Info, $"Connection state: {e.OldState} -> {e.NewState}" +
                (e.Reason != null ? $" ({e.Reason})" : ""));
            ConnectionStateChanged?.Invoke(this, e);
        }

        public async Task<bool> ConnectAsync(PlcConnectionConfig config)
        {
            _config = config;
            Log(PlcLogLevel.Info, $"Connecting to {config.Vendor} PLC at {config.IpAddress}:{config.Port}");

            var result = await _inner.ConnectAsync(config);

            if (result)
            {
                Log(PlcLogLevel.Info, "Connected successfully");
                StartKeepAlive();
            }
            else
            {
                Log(PlcLogLevel.Error, "Initial connection failed");
            }

            return result;
        }

        public async Task DisconnectAsync()
        {
            Log(PlcLogLevel.Info, "Disconnecting");
            StopKeepAlive();
            await _inner.DisconnectAsync();
        }

        // --- Bit operations with resilience ---

        public async Task<bool> ReadBitAsync(PlcAddress address)
        {
            return await ExecuteWithReconnect(
                () => _inner.ReadBitAsync(address),
                $"ReadBit({address.RawAddress})");
        }

        public async Task WriteBitAsync(PlcAddress address, bool value)
        {
            await ExecuteWithReconnect(
                () => _inner.WriteBitAsync(address, value),
                $"WriteBit({address.RawAddress}, {value})");
        }

        // --- Word operations with resilience ---

        public async Task<short> ReadWordAsync(PlcAddress address)
        {
            return await ExecuteWithReconnect(
                () => _inner.ReadWordAsync(address),
                $"ReadWord({address.RawAddress})");
        }

        public async Task WriteWordAsync(PlcAddress address, short value)
        {
            await ExecuteWithReconnect(
                () => _inner.WriteWordAsync(address, value),
                $"WriteWord({address.RawAddress}, {value})");
        }

        // --- DWord operations with resilience ---

        public async Task<int> ReadDWordAsync(PlcAddress address)
        {
            return await ExecuteWithReconnect(
                () => _inner.ReadDWordAsync(address),
                $"ReadDWord({address.RawAddress})");
        }

        public async Task WriteDWordAsync(PlcAddress address, int value)
        {
            await ExecuteWithReconnect(
                () => _inner.WriteDWordAsync(address, value),
                $"WriteDWord({address.RawAddress}, {value})");
        }

        // --- Block operations with resilience ---

        public async Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count)
        {
            return await ExecuteWithReconnect(
                () => _inner.ReadWordsAsync(startAddress, count),
                $"ReadWords({startAddress.RawAddress}, count={count})");
        }

        public async Task WriteWordsAsync(PlcAddress startAddress, short[] values)
        {
            await ExecuteWithReconnect(
                () => _inner.WriteWordsAsync(startAddress, values),
                $"WriteWords({startAddress.RawAddress}, count={values.Length})");
        }

        // --- Monitoring (delegated directly) ---

        public Task StartMonitoringAsync(PlcAddress address, int pollingIntervalMs = 50)
        {
            Log(PlcLogLevel.Debug, $"StartMonitoring({address.RawAddress}, interval={pollingIntervalMs}ms)");
            return _inner.StartMonitoringAsync(address, pollingIntervalMs);
        }

        public Task StopMonitoringAsync(PlcAddress address)
        {
            Log(PlcLogLevel.Debug, $"StopMonitoring({address.RawAddress})");
            return _inner.StopMonitoringAsync(address);
        }

        public Task StopAllMonitoringAsync()
        {
            Log(PlcLogLevel.Debug, "StopAllMonitoring");
            return _inner.StopAllMonitoringAsync();
        }

        // --- Reconnection logic ---

        private async Task<T> ExecuteWithReconnect<T>(Func<Task<T>> operation, string operationName)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (!_disposed)
            {
                Log(PlcLogLevel.Warning, $"{operationName} failed, attempting reconnect", ex);

                if (await TryReconnectAsync())
                {
                    Log(PlcLogLevel.Info, $"Reconnected, retrying {operationName}");
                    return await operation();
                }

                Log(PlcLogLevel.Error, $"{operationName} failed after reconnect attempts", ex);
                throw;
            }
        }

        private async Task ExecuteWithReconnect(Func<Task> operation, string operationName)
        {
            try
            {
                await operation();
            }
            catch (Exception ex) when (!_disposed)
            {
                Log(PlcLogLevel.Warning, $"{operationName} failed, attempting reconnect", ex);

                if (await TryReconnectAsync())
                {
                    Log(PlcLogLevel.Info, $"Reconnected, retrying {operationName}");
                    await operation();
                    return;
                }

                Log(PlcLogLevel.Error, $"{operationName} failed after reconnect attempts", ex);
                throw;
            }
        }

        private async Task<bool> TryReconnectAsync()
        {
            if (!await _reconnectLock.WaitAsync(0))
            {
                // Another reconnect is in progress, wait for it
                await _reconnectLock.WaitAsync();
                _reconnectLock.Release();
                return _inner.IsConnected;
            }

            try
            {
                for (int attempt = 1; attempt <= MaxRetryCount; attempt++)
                {
                    Log(PlcLogLevel.Info, $"Reconnect attempt {attempt}/{MaxRetryCount}");

                    try
                    {
                        await _inner.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        Log(PlcLogLevel.Debug, "Disconnect during reconnect threw", ex);
                    }

                    try
                    {
                        var connected = await _inner.ConnectAsync(_config);
                        if (connected)
                        {
                            Log(PlcLogLevel.Info, $"Reconnected on attempt {attempt}");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(PlcLogLevel.Warning, $"Reconnect attempt {attempt} failed", ex);
                    }

                    if (attempt < MaxRetryCount)
                    {
                        await Task.Delay(RetryIntervalMs);
                    }
                }

                Log(PlcLogLevel.Error, $"All {MaxRetryCount} reconnect attempts failed");
                return false;
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        // --- Keep-alive ---

        private void StartKeepAlive()
        {
            StopKeepAlive();
            _keepAliveCts = new CancellationTokenSource();
            _keepAliveTask = RunKeepAliveAsync(_keepAliveCts.Token);
        }

        private void StopKeepAlive()
        {
            _keepAliveCts?.Cancel();
            _keepAliveCts?.Dispose();
            _keepAliveCts = null;
            _keepAliveTask = null;
        }

        private async Task RunKeepAliveAsync(CancellationToken ct)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(KeepAliveIntervalMs));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    if (!_inner.IsConnected)
                    {
                        Log(PlcLogLevel.Warning, "KeepAlive detected disconnection, attempting reconnect");
                        await TryReconnectAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
        }

        // --- Logging ---

        private void Log(PlcLogLevel level, string message, Exception? ex = null)
        {
            LogCallback?.Invoke(new PlcLogEntry(level, message, ex));
        }

        // --- Dispose ---

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopKeepAlive();
            _inner.BitChanged -= (s, e) => BitChanged?.Invoke(this, e);
            _inner.ConnectionStateChanged -= OnInnerConnectionStateChanged;
            _inner.Dispose();
            _reconnectLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
