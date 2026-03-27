using System.IO;
using System.Net.Sockets;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Mitsubishi MC Protocol (Binary 3E frame) implementation over TCP.
    /// Compatible with MELSEC-Q/L/R series PLCs.
    /// </summary>
    public class MitsubishiMcConnection : IPlcConnection
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private PlcConnectionConfig _config = new();
        private readonly SemaphoreSlim _commLock = new(1, 1);
        private readonly Dictionary<string, (PlcAddress Address, int IntervalMs, CancellationTokenSource Cts)> _monitors = new();
        private PlcConnectionState _connectionState = PlcConnectionState.Disconnected;
        private bool _disposed;

        // MC Protocol constants
        private const ushort Subheader = 0x0050; // Binary 3E frame
        private const ushort ReadCommand = 0x0401;
        private const ushort WriteCommand = 0x1401;
        private const ushort SubcommandWord = 0x0000;
        private const ushort SubcommandBit = 0x0001;

        public bool IsConnected => _connectionState == PlcConnectionState.Connected;
        public PlcConnectionState ConnectionState => _connectionState;
        public event EventHandler<PlcBitChangedEventArgs>? BitChanged;
        public event EventHandler<PlcConnectionStateChangedEventArgs>? ConnectionStateChanged;

        public async Task<bool> ConnectAsync(PlcConnectionConfig config)
        {
            _config = config;
            SetConnectionState(PlcConnectionState.Connecting);

            try
            {
                _client = new TcpClient();
                using var cts = new CancellationTokenSource(config.ConnectTimeoutMs);
                await _client.ConnectAsync(config.IpAddress, config.Port, cts.Token);
                _stream = _client.GetStream();
                _stream.ReadTimeout = config.ReadTimeoutMs;
                _stream.WriteTimeout = config.WriteTimeoutMs;
                SetConnectionState(PlcConnectionState.Connected);
                return true;
            }
            catch
            {
                SetConnectionState(PlcConnectionState.Error);
                Cleanup();
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            await StopAllMonitoringAsync();
            Cleanup();
            SetConnectionState(PlcConnectionState.Disconnected);
        }

        // --- Bit operations ---

        public async Task<bool> ReadBitAsync(PlcAddress address)
        {
            var deviceCode = GetDeviceCode(address.DeviceCode);
            int headDevice = address.Offset * 16 + Math.Max(0, address.BitPosition);
            var response = await SendReadCommand(deviceCode, headDevice, 1, isBit: true);
            return response.Length > 0 && response[0] != 0;
        }

        public async Task WriteBitAsync(PlcAddress address, bool value)
        {
            var deviceCode = GetDeviceCode(address.DeviceCode);
            int headDevice = address.Offset * 16 + Math.Max(0, address.BitPosition);
            byte[] data = [(byte)(value ? 0x10 : 0x00)];
            await SendWriteCommand(deviceCode, headDevice, 1, data, isBit: true);
        }

        // --- Word (16-bit) operations ---

        public async Task<short> ReadWordAsync(PlcAddress address)
        {
            var words = await ReadWordsAsync(address, 1);
            return words[0];
        }

        public async Task WriteWordAsync(PlcAddress address, short value)
        {
            await WriteWordsAsync(address, [value]);
        }

        // --- DWord (32-bit) operations ---

        public async Task<int> ReadDWordAsync(PlcAddress address)
        {
            var words = await ReadWordsAsync(address, 2);
            return (words[1] << 16) | (ushort)words[0];
        }

        public async Task WriteDWordAsync(PlcAddress address, int value)
        {
            short low = (short)(value & 0xFFFF);
            short high = (short)((value >> 16) & 0xFFFF);
            await WriteWordsAsync(address, [low, high]);
        }

        // --- Block operations ---

        public async Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count)
        {
            var deviceCode = GetDeviceCode(startAddress.DeviceCode);
            var response = await SendReadCommand(deviceCode, startAddress.Offset, count, isBit: false);

            var result = new short[count];
            for (int i = 0; i < count && i * 2 + 1 < response.Length; i++)
            {
                result[i] = (short)(response[i * 2] | (response[i * 2 + 1] << 8));
            }
            return result;
        }

        public async Task WriteWordsAsync(PlcAddress startAddress, short[] values)
        {
            var deviceCode = GetDeviceCode(startAddress.DeviceCode);
            var data = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                data[i * 2] = (byte)(values[i] & 0xFF);
                data[i * 2 + 1] = (byte)((values[i] >> 8) & 0xFF);
            }
            await SendWriteCommand(deviceCode, startAddress.Offset, values.Length, data, isBit: false);
        }

        // --- Monitoring ---

        public Task StartMonitoringAsync(PlcAddress address, int pollingIntervalMs = 50)
        {
            var key = address.RawAddress.ToUpperInvariant();
            if (_monitors.ContainsKey(key)) return Task.CompletedTask;

            var cts = new CancellationTokenSource();
            _monitors[key] = (address, pollingIntervalMs, cts);
            _ = MonitorLoopAsync(address, pollingIntervalMs, cts.Token);
            return Task.CompletedTask;
        }

        public Task StopMonitoringAsync(PlcAddress address)
        {
            var key = address.RawAddress.ToUpperInvariant();
            if (_monitors.Remove(key, out var entry))
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

        private async Task MonitorLoopAsync(PlcAddress address, int intervalMs, CancellationToken ct)
        {
            bool lastValue = false;
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    if (!IsConnected) continue;
                    try
                    {
                        var current = await ReadBitAsync(address);
                        if (current != lastValue)
                        {
                            lastValue = current;
                            BitChanged?.Invoke(this, new PlcBitChangedEventArgs(address, current));
                        }
                    }
                    catch
                    {
                        // Ignore transient read errors during monitoring
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        // --- MC Protocol frame builders ---

        private async Task<byte[]> SendReadCommand(byte deviceCode, int headDevice, int numPoints, bool isBit)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                var subcommand = isBit ? SubcommandBit : SubcommandWord;
                // Data portion: command(2) + subcommand(2) + headDevice(3) + deviceCode(1) + numPoints(2) = 10
                int dataLength = 10;

                // 3E frame: subheader(2) + network(1) + pc(1) + moduleIO(2) + station(1) + dataLen(2) + timer(2) + data(10) = 21
                var request = new byte[2 + 7 + 2 + dataLength];
                int pos = 0;

                // Subheader (3E binary: 0x5000 → LE bytes 0x50, 0x00)
                WriteUInt16LE(request, ref pos, Subheader);
                // Network number
                request[pos++] = _config.NetworkNumber;
                // PC number
                request[pos++] = _config.StationNumber;
                // Request destination module I/O (0x03FF = own station)
                WriteUInt16LE(request, ref pos, 0x03FF);
                // Request destination module station (0x00)
                request[pos++] = 0x00;
                // Request data length (from monitoring timer onward)
                WriteUInt16LE(request, ref pos, (ushort)(dataLength + 2));
                // Monitoring timer (timeout in 250ms units, 0x0004 = 1 second)
                WriteUInt16LE(request, ref pos, 0x0004);
                // Command
                WriteUInt16LE(request, ref pos, ReadCommand);
                // Subcommand
                WriteUInt16LE(request, ref pos, subcommand);
                // Head device number (3 bytes LE)
                request[pos++] = (byte)(headDevice & 0xFF);
                request[pos++] = (byte)((headDevice >> 8) & 0xFF);
                request[pos++] = (byte)((headDevice >> 16) & 0xFF);
                // Device code
                request[pos++] = deviceCode;
                // Number of device points
                WriteUInt16LE(request, ref pos, (ushort)numPoints);

                await _stream!.WriteAsync(request.AsMemory(0, pos));
                await _stream.FlushAsync();

                // Read 3E response header: subheader(2) + network(1) + pc(1) + moduleIO(2) + station(1) + dataLen(2) = 9
                var header = await ReadExactAsync(9);
                int respDataLen = (header[7] | (header[8] << 8));
                var respData = await ReadExactAsync(respDataLen);

                // Check end code (first 2 bytes of respData)
                int endCode = respData[0] | (respData[1] << 8);
                if (endCode != 0)
                    throw new IOException($"MC Protocol error: 0x{endCode:X4}");

                // Return data portion (skip 2-byte end code)
                return respData[2..];
            }
            finally
            {
                _commLock.Release();
            }
        }

        private async Task SendWriteCommand(byte deviceCode, int headDevice, int numPoints, byte[] data, bool isBit)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                var subcommand = isBit ? SubcommandBit : SubcommandWord;
                int dataLength = 10 + data.Length;

                // 3E frame: subheader(2) + network(1) + pc(1) + moduleIO(2) + station(1) + dataLen(2) + timer(2) + data
                var request = new byte[2 + 7 + 2 + dataLength];
                int pos = 0;

                WriteUInt16LE(request, ref pos, Subheader);
                request[pos++] = _config.NetworkNumber;
                request[pos++] = _config.StationNumber;
                WriteUInt16LE(request, ref pos, 0x03FF);
                request[pos++] = 0x00;
                WriteUInt16LE(request, ref pos, (ushort)(dataLength + 2));
                WriteUInt16LE(request, ref pos, 0x0004);
                WriteUInt16LE(request, ref pos, WriteCommand);
                WriteUInt16LE(request, ref pos, subcommand);
                request[pos++] = (byte)(headDevice & 0xFF);
                request[pos++] = (byte)((headDevice >> 8) & 0xFF);
                request[pos++] = (byte)((headDevice >> 16) & 0xFF);
                request[pos++] = deviceCode;
                WriteUInt16LE(request, ref pos, (ushort)numPoints);
                Array.Copy(data, 0, request, pos, data.Length);
                pos += data.Length;

                await _stream!.WriteAsync(request.AsMemory(0, pos));
                await _stream.FlushAsync();

                // Read 3E response header (9 bytes)
                var header = await ReadExactAsync(9);
                int respDataLen = header[7] | (header[8] << 8);
                var respData = await ReadExactAsync(respDataLen);

                int endCode = respData[0] | (respData[1] << 8);
                if (endCode != 0)
                    throw new IOException($"MC Protocol write error: 0x{endCode:X4}");
            }
            finally
            {
                _commLock.Release();
            }
        }

        // --- Device code mapping ---

        private static byte GetDeviceCode(string device) => device.ToUpper() switch
        {
            "D" => 0xA8,
            "M" => 0x90,
            "X" => 0x9C,
            "Y" => 0x9D,
            "R" => 0xAF,
            "W" => 0xB4,
            "B" => 0xA0,
            "L" => 0x92,
            "F" => 0x93,
            "TN" => 0xC2,
            "CN" => 0xC5,
            _ => throw new NotSupportedException($"Unsupported Mitsubishi device: {device}")
        };

        // --- Helpers ---

        private static void WriteUInt16LE(byte[] buffer, ref int pos, ushort value)
        {
            buffer[pos++] = (byte)(value & 0xFF);
            buffer[pos++] = (byte)((value >> 8) & 0xFF);
        }

        private async Task<byte[]> ReadExactAsync(int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset));
                if (read == 0) throw new IOException("Connection closed by PLC");
                offset += read;
            }
            return buffer;
        }

        private void SetConnectionState(PlcConnectionState newState, string? reason = null)
        {
            var oldState = _connectionState;
            if (oldState == newState) return;
            _connectionState = newState;
            ConnectionStateChanged?.Invoke(this, new PlcConnectionStateChangedEventArgs(oldState, newState, reason));
        }

        private void EnsureConnected()
        {
            if (_stream == null || !IsConnected)
                throw new InvalidOperationException("Not connected to PLC");
        }

        private void Cleanup()
        {
            _stream?.Dispose();
            _stream = null;
            _client?.Dispose();
            _client = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAllMonitoringAsync().GetAwaiter().GetResult();
            Cleanup();
            _commLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
