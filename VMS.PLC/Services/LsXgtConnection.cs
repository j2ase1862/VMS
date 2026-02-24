using System.IO;
using System.Net.Sockets;
using System.Text;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// LS Electric XGT Protocol implementation over TCP.
    /// Compatible with XGK/XGB/XGI/XGR series PLCs.
    /// </summary>
    public class LsXgtConnection : IPlcConnection
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private PlcConnectionConfig _config = new();
        private readonly SemaphoreSlim _commLock = new(1, 1);
        private readonly Dictionary<string, (PlcAddress Address, int IntervalMs, CancellationTokenSource Cts)> _monitors = new();
        private PlcConnectionState _connectionState = PlcConnectionState.Disconnected;
        private bool _disposed;

        // XGT Protocol constants
        private static readonly byte[] CompanyId = Encoding.ASCII.GetBytes("LSIS-XGT");
        private const ushort ReadCommand = 0x0054;
        private const ushort WriteCommand = 0x0058;
        private const ushort DataTypeContinuous = 0x0014;

        public bool IsConnected => _connectionState == PlcConnectionState.Connected;
        public PlcConnectionState ConnectionState => _connectionState;
        public event EventHandler<PlcBitChangedEventArgs>? BitChanged;

        public async Task<bool> ConnectAsync(PlcConnectionConfig config)
        {
            _config = config;
            _connectionState = PlcConnectionState.Connecting;

            try
            {
                _client = new TcpClient();
                using var cts = new CancellationTokenSource(config.ConnectTimeoutMs);
                await _client.ConnectAsync(config.IpAddress, config.Port, cts.Token);
                _stream = _client.GetStream();
                _stream.ReadTimeout = config.ReadTimeoutMs;
                _stream.WriteTimeout = config.WriteTimeoutMs;
                _connectionState = PlcConnectionState.Connected;
                return true;
            }
            catch
            {
                _connectionState = PlcConnectionState.Error;
                Cleanup();
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            await StopAllMonitoringAsync();
            Cleanup();
            _connectionState = PlcConnectionState.Disconnected;
        }

        // --- Bit operations ---

        public async Task<bool> ReadBitAsync(PlcAddress address)
        {
            var varName = ToXgtVariableName(address, isBit: true);
            var data = await XgtReadAsync(varName, 1);
            return data.Length > 0 && data[0] != 0;
        }

        public async Task WriteBitAsync(PlcAddress address, bool value)
        {
            var varName = ToXgtVariableName(address, isBit: true);
            byte[] data = [(byte)(value ? 0x01 : 0x00)];
            await XgtWriteAsync(varName, data, 1);
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
            var varName = ToXgtVariableName(address, isBit: false, sizePrefix: "DD");
            var data = await XgtReadAsync(varName, 2);
            if (data.Length < 4) return 0;
            return data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
        }

        public async Task WriteDWordAsync(PlcAddress address, int value)
        {
            var varName = ToXgtVariableName(address, isBit: false, sizePrefix: "DD");
            byte[] data =
            [
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF)
            ];
            await XgtWriteAsync(varName, data, 2);
        }

        // --- Block operations ---

        public async Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count)
        {
            var varName = ToXgtVariableName(startAddress, isBit: false);
            var data = await XgtReadAsync(varName, count);
            var result = new short[count];
            for (int i = 0; i < count && i * 2 + 1 < data.Length; i++)
            {
                result[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            }
            return result;
        }

        public async Task WriteWordsAsync(PlcAddress startAddress, short[] values)
        {
            var varName = ToXgtVariableName(startAddress, isBit: false);
            var data = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                data[i * 2] = (byte)(values[i] & 0xFF);
                data[i * 2 + 1] = (byte)((values[i] >> 8) & 0xFF);
            }
            await XgtWriteAsync(varName, data, values.Length);
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
                    catch { }
                }
            }
            catch (OperationCanceledException) { }
        }

        // --- XGT Protocol frame builders ---

        private async Task<byte[]> XgtReadAsync(string variableName, int count)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                var varBytes = Encoding.ASCII.GetBytes(variableName);

                // Build application data
                using var appData = new MemoryStream();
                using var writer = new BinaryWriter(appData);

                // Data type
                writer.Write(DataTypeContinuous);
                // Reserved
                writer.Write((ushort)0x0000);
                // Number of blocks
                writer.Write((ushort)0x0001);
                // Variable name length
                writer.Write((ushort)varBytes.Length);
                // Variable name
                writer.Write(varBytes);
                // Number of data to read
                writer.Write((ushort)count);

                var appBytes = appData.ToArray();
                var frame = BuildXgtFrame(ReadCommand, appBytes);

                await _stream!.WriteAsync(frame);
                await _stream.FlushAsync();

                var response = await ReadXgtResponse();
                return response;
            }
            finally
            {
                _commLock.Release();
            }
        }

        private async Task XgtWriteAsync(string variableName, byte[] data, int count)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                var varBytes = Encoding.ASCII.GetBytes(variableName);

                using var appData = new MemoryStream();
                using var writer = new BinaryWriter(appData);

                writer.Write(DataTypeContinuous);
                writer.Write((ushort)0x0000);
                writer.Write((ushort)0x0001);
                writer.Write((ushort)varBytes.Length);
                writer.Write(varBytes);
                writer.Write((ushort)count);
                writer.Write(data);

                var appBytes = appData.ToArray();
                var frame = BuildXgtFrame(WriteCommand, appBytes);

                await _stream!.WriteAsync(frame);
                await _stream.FlushAsync();

                // Read and verify response
                await ReadXgtResponse();
            }
            finally
            {
                _commLock.Release();
            }
        }

        private byte[] BuildXgtFrame(ushort command, byte[] applicationData)
        {
            // XGT Header: CompanyId(8) + Reserved(2) + Reserved(2) + CPU Info(2) + Source(2) +
            //             InvokeId(2) + Length(2) + FEnet Pos(1) + Reserved(1) + Command(2) + DataType(2) + Reserved(2) + BlockCount(2)
            // Followed by application data

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // Company ID: "LSIS-XGT" (8 bytes)
            writer.Write(CompanyId);
            // Reserved (2 bytes)
            writer.Write((ushort)0x0000);
            // Reserved (2 bytes)
            writer.Write((ushort)0x0000);
            // CPU Info (2 bytes): XGK = 0xA0
            writer.Write((ushort)0x00A0);
            // Source Info (2 bytes): Client = 0x33
            writer.Write((ushort)0x0033);
            // Invoke ID
            writer.Write(_config.InvokeId);
            // Data length (application data length)
            writer.Write((ushort)applicationData.Length);
            // FEnet position (1 byte)
            writer.Write((byte)0x00);
            // Reserved (1 byte)
            writer.Write((byte)0x00);
            // BCC (2 bytes) - XOR checksum (simplified: 0)
            writer.Write((ushort)0x0000);
            // Command
            writer.Write(command);
            // Application data
            writer.Write(applicationData);

            return ms.ToArray();
        }

        private async Task<byte[]> ReadXgtResponse()
        {
            // Read XGT header (minimum 28 bytes)
            var header = await ReadExactAsync(28);

            // Verify company ID
            var compId = Encoding.ASCII.GetString(header, 0, 8);
            if (compId != "LSIS-XGT")
                throw new IOException($"Invalid XGT response header: {compId}");

            // Data length at offset 16 (2 bytes LE)
            int dataLen = header[16] | (header[17] << 8);

            if (dataLen <= 0) return [];

            // Read application response data
            var appData = await ReadExactAsync(dataLen);

            // Check for error: first 2 bytes of appData are typically the error code block count
            // In read response: DataType(2) + Reserved(2) + ErrorCode(2) + BlockCount(2) + ...
            if (appData.Length >= 6)
            {
                int errorCode = appData[4] | (appData[5] << 8);
                if (errorCode != 0)
                    throw new IOException($"XGT error: 0x{errorCode:X4}");
            }

            // Skip header portion of appData to get raw data
            // Read response format: DataType(2) + Reserved(2) + BlockCount(2) + VarNameLen(2) + VarName(n) + DataCount(2) + Data(n)
            if (appData.Length < 8) return [];

            int blockCount = appData[4] | (appData[5] << 8);
            if (blockCount == 0) return [];

            // Parse first block
            int pos = 6;
            if (pos + 2 > appData.Length) return [];
            int varNameLen = appData[pos] | (appData[pos + 1] << 8);
            pos += 2 + varNameLen;

            if (pos + 2 > appData.Length) return [];
            int dataCount = appData[pos] | (appData[pos + 1] << 8);
            pos += 2;

            if (pos >= appData.Length) return [];
            var result = new byte[Math.Min(dataCount, appData.Length - pos)];
            Array.Copy(appData, pos, result, 0, result.Length);
            return result;
        }

        // --- Variable name formatting ---

        private static string ToXgtVariableName(PlcAddress address, bool isBit, string? sizePrefix = null)
        {
            string prefix = sizePrefix ?? (isBit ? "X" : "W");
            // Format: %{DeviceCode}{SizePrefix}{Offset:D4}
            // For bit: %MX0000, for word: %DW0100
            return $"%{address.DeviceCode}{prefix}{address.Offset:D4}";
        }

        // --- Helpers ---

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
