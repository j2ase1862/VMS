using System.IO;
using System.Net.Sockets;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Siemens S7 Protocol (ISO-on-TCP) implementation.
    /// Compatible with S7-300/400/1200/1500 series PLCs.
    /// Connection: TCP -> COTP CR/CC -> S7 Setup Communication
    /// </summary>
    public class SiemensS7Connection : IPlcConnection
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private PlcConnectionConfig _config = new();
        private readonly SemaphoreSlim _commLock = new(1, 1);
        private readonly Dictionary<string, (PlcAddress Address, int IntervalMs, CancellationTokenSource Cts)> _monitors = new();
        private PlcConnectionState _connectionState = PlcConnectionState.Disconnected;
        private bool _disposed;

        // S7 Protocol constants
        private const byte TpktVersion = 0x03;
        private const byte ReadFunction = 0x04;
        private const byte WriteFunction = 0x05;

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

                // Step 1: COTP Connection Request
                if (!await SendCotpConnectionRequest())
                {
                    SetConnectionState(PlcConnectionState.Error, "COTP connection request failed");
                    Cleanup();
                    return false;
                }

                // Step 2: S7 Setup Communication
                if (!await SendS7SetupCommunication())
                {
                    SetConnectionState(PlcConnectionState.Error, "S7 setup communication failed");
                    Cleanup();
                    return false;
                }

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
            var area = GetAreaCode(address);
            int dbNumber = address.DbNumber > 0 ? address.DbNumber : 0;
            int byteOffset = address.Offset;
            int bitOffset = Math.Max(0, address.BitPosition);
            int s7Address = byteOffset * 8 + bitOffset;

            var data = await S7ReadAsync(area, dbNumber, s7Address, 1, isbit: true);
            return data.Length > 0 && (data[0] & 0x01) != 0;
        }

        public async Task WriteBitAsync(PlcAddress address, bool value)
        {
            var area = GetAreaCode(address);
            int dbNumber = address.DbNumber > 0 ? address.DbNumber : 0;
            int byteOffset = address.Offset;
            int bitOffset = Math.Max(0, address.BitPosition);
            int s7Address = byteOffset * 8 + bitOffset;

            byte[] data = [(byte)(value ? 0x01 : 0x00)];
            await S7WriteAsync(area, dbNumber, s7Address, data, isbit: true);
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
            var area = GetAreaCode(address);
            int dbNumber = address.DbNumber > 0 ? address.DbNumber : 0;
            int s7Address = address.Offset * 8;

            var data = await S7ReadAsync(area, dbNumber, s7Address, 4, isbit: false);
            // S7 is big-endian
            return (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        }

        public async Task WriteDWordAsync(PlcAddress address, int value)
        {
            var area = GetAreaCode(address);
            int dbNumber = address.DbNumber > 0 ? address.DbNumber : 0;
            int s7Address = address.Offset * 8;

            byte[] data =
            [
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            ];
            await S7WriteAsync(area, dbNumber, s7Address, data, isbit: false);
        }

        // --- Block operations ---

        public async Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count)
        {
            var area = GetAreaCode(startAddress);
            int dbNumber = startAddress.DbNumber > 0 ? startAddress.DbNumber : 0;
            int s7Address = startAddress.Offset * 8;

            var data = await S7ReadAsync(area, dbNumber, s7Address, count * 2, isbit: false);
            var result = new short[count];
            for (int i = 0; i < count && i * 2 + 1 < data.Length; i++)
            {
                // Big-endian
                result[i] = (short)((data[i * 2] << 8) | data[i * 2 + 1]);
            }
            return result;
        }

        public async Task WriteWordsAsync(PlcAddress startAddress, short[] values)
        {
            var area = GetAreaCode(startAddress);
            int dbNumber = startAddress.DbNumber > 0 ? startAddress.DbNumber : 0;
            int s7Address = startAddress.Offset * 8;

            var data = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                // Big-endian
                data[i * 2] = (byte)((values[i] >> 8) & 0xFF);
                data[i * 2 + 1] = (byte)(values[i] & 0xFF);
            }
            await S7WriteAsync(area, dbNumber, s7Address, data, isbit: false);
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

        // --- S7 Protocol implementation ---

        private async Task<bool> SendCotpConnectionRequest()
        {
            // TPKT + COTP Connection Request
            byte[] cotpCr =
            [
                // TPKT Header
                TpktVersion, 0x00, 0x00, 0x16,  // Version, Reserved, Length (22 bytes)
                // COTP CR PDU
                0x11,                             // Length (17 bytes remaining)
                0xE0,                             // CR (Connection Request)
                0x00, 0x00,                       // Dst reference
                0x00, 0x01,                       // Src reference
                0x00,                             // Class 0
                // Parameters
                0xC0, 0x01, 0x0A,                 // TPDU size (1024)
                0xC1, 0x02, 0x01, 0x00,           // Src TSAP
                0xC2, 0x02,                        // Dst TSAP
                (byte)((_config.Rack * 0x20) | _config.Slot), // Rack/Slot encoding
                0x00
            ];

            await _stream!.WriteAsync(cotpCr);
            await _stream.FlushAsync();

            var response = await ReadTpktPacket();
            // Verify COTP CC (Connection Confirm): byte[5] should be 0xD0
            return response.Length > 5 && response[5] == 0xD0;
        }

        private async Task<bool> SendS7SetupCommunication()
        {
            byte[] s7Setup =
            [
                // TPKT Header
                TpktVersion, 0x00, 0x00, 0x19,  // 25 bytes total
                // COTP Data DT
                0x02, 0xF0, 0x80,
                // S7 Header
                0x32,                             // Protocol ID
                0x01,                             // Job request
                0x00, 0x00,                       // Reserved
                0x00, 0x00,                       // PDU reference
                0x00, 0x08,                       // Parameter length
                0x00, 0x00,                       // Data length
                // S7 Setup Communication
                0xF0,                             // Function: Setup Communication
                0x00,                             // Reserved
                0x00, 0x01,                       // Max AmQ calling
                0x00, 0x01,                       // Max AmQ called
                0x01, 0xE0                        // PDU length (480)
            ];

            await _stream!.WriteAsync(s7Setup);
            await _stream.FlushAsync();

            var response = await ReadTpktPacket();
            // Verify S7 Ack: byte[8] should be 0x03 (Ack-Data), no error
            return response.Length > 10 && response[8] == 0x03;
        }

        private async Task<byte[]> S7ReadAsync(byte area, int dbNumber, int s7Address, int size, bool isbit)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                byte transportSize = isbit ? (byte)0x01 : (byte)0x02; // 1=BIT, 2=BYTE
                int readLength = isbit ? 1 : size;

                byte[] request =
                [
                    // TPKT
                    TpktVersion, 0x00, 0x00, 0x1F,  // 31 bytes total
                    // COTP DT
                    0x02, 0xF0, 0x80,
                    // S7 Header
                    0x32, 0x01, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x0E,  // Parameter length = 14
                    0x00, 0x00,  // Data length = 0
                    // S7 Read Var
                    ReadFunction,
                    0x01,  // Item count = 1
                    // Item
                    0x12,  // Var spec
                    0x0A,  // Length of remaining item
                    0x10,  // Syntax ID: S7ANY
                    transportSize,
                    (byte)((readLength >> 8) & 0xFF), (byte)(readLength & 0xFF),  // Count
                    (byte)((dbNumber >> 8) & 0xFF), (byte)(dbNumber & 0xFF),      // DB number
                    area,
                    (byte)((s7Address >> 16) & 0xFF), (byte)((s7Address >> 8) & 0xFF), (byte)(s7Address & 0xFF)
                ];

                // Fix TPKT length
                int totalLen = request.Length;
                request[2] = (byte)((totalLen >> 8) & 0xFF);
                request[3] = (byte)(totalLen & 0xFF);

                await _stream!.WriteAsync(request);
                await _stream.FlushAsync();

                var response = await ReadTpktPacket();

                // Parse S7 response: skip TPKT(4) + COTP(3) + S7Header(12) + ItemHeader(4)
                if (response.Length < 25)
                    throw new IOException("Invalid S7 read response");

                int errorClass = response[17];
                int errorCode = response[18];
                if (errorClass != 0 || errorCode != 0)
                    throw new IOException($"S7 read error: class=0x{errorClass:X2} code=0x{errorCode:X2}");

                // Data item starts at offset 21
                byte returnCode = response[21];
                if (returnCode != 0xFF)
                    throw new IOException($"S7 data item error: 0x{returnCode:X2}");

                // byte[22] = transport size, byte[23-24] = data length
                int dataLen = (response[23] << 8) | response[24];
                if (isbit) dataLen = 1; // bit access returns count in bits
                else dataLen /= 8; // convert bits to bytes

                var result = new byte[dataLen];
                Array.Copy(response, 25, result, 0, Math.Min(dataLen, response.Length - 25));
                return result;
            }
            finally
            {
                _commLock.Release();
            }
        }

        private async Task S7WriteAsync(byte area, int dbNumber, int s7Address, byte[] data, bool isbit)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                byte transportSize = isbit ? (byte)0x01 : (byte)0x02;
                int writeLength = isbit ? 1 : data.Length;
                int dataLenBits = isbit ? data.Length : data.Length * 8;

                // Build request
                var header = new byte[]
                {
                    // TPKT (length filled later)
                    TpktVersion, 0x00, 0x00, 0x00,
                    // COTP DT
                    0x02, 0xF0, 0x80,
                    // S7 Header
                    0x32, 0x01, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x0E,  // Parameter length = 14
                    0x00, 0x00,  // Data length (filled later)
                    // S7 Write Var
                    WriteFunction,
                    0x01,  // Item count
                    // Item
                    0x12, 0x0A, 0x10,
                    transportSize,
                    (byte)((writeLength >> 8) & 0xFF), (byte)(writeLength & 0xFF),
                    (byte)((dbNumber >> 8) & 0xFF), (byte)(dbNumber & 0xFF),
                    area,
                    (byte)((s7Address >> 16) & 0xFF), (byte)((s7Address >> 8) & 0xFF), (byte)(s7Address & 0xFF),
                    // Data item header
                    0x00,  // Return code (0 for request)
                    (byte)(isbit ? 0x03 : 0x04),  // Transport size: 3=BIT, 4=BYTE
                    (byte)((dataLenBits >> 8) & 0xFF), (byte)(dataLenBits & 0xFF)
                };

                int totalLen = header.Length + data.Length;
                header[2] = (byte)((totalLen >> 8) & 0xFF);
                header[3] = (byte)(totalLen & 0xFF);

                // Data length in S7 header (offset 15-16)
                int s7DataLen = 4 + data.Length; // item header + data
                header[15] = (byte)((s7DataLen >> 8) & 0xFF);
                header[16] = (byte)(s7DataLen & 0xFF);

                var request = new byte[totalLen];
                Array.Copy(header, request, header.Length);
                Array.Copy(data, 0, request, header.Length, data.Length);

                await _stream!.WriteAsync(request);
                await _stream.FlushAsync();

                var response = await ReadTpktPacket();

                if (response.Length > 18)
                {
                    int errorClass = response[17];
                    int errorCode = response[18];
                    if (errorClass != 0 || errorCode != 0)
                        throw new IOException($"S7 write error: class=0x{errorClass:X2} code=0x{errorCode:X2}");
                }

                // Check write result byte
                if (response.Length > 21 && response[21] != 0xFF)
                    throw new IOException($"S7 write data error: 0x{response[21]:X2}");
            }
            finally
            {
                _commLock.Release();
            }
        }

        private async Task<byte[]> ReadTpktPacket()
        {
            var tpktHeader = await ReadExactAsync(4);
            int length = (tpktHeader[2] << 8) | tpktHeader[3];
            if (length < 4) throw new IOException("Invalid TPKT packet length");

            var payload = await ReadExactAsync(length - 4);
            var packet = new byte[length];
            Array.Copy(tpktHeader, packet, 4);
            Array.Copy(payload, 0, packet, 4, payload.Length);
            return packet;
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

        // --- Area code mapping ---

        private static byte GetAreaCode(PlcAddress address)
        {
            if (address.DbNumber > 0) return 0x84; // DB

            return address.DeviceCode.ToUpper() switch
            {
                "M" or "MW" or "MB" or "MD" => 0x83,
                "I" or "IW" or "IB" or "ID" => 0x81,
                "Q" or "QW" or "QB" or "QD" => 0x82,
                "DBX" or "DBW" or "DBB" or "DBD" => 0x84,
                _ => 0x84 // Default to DB
            };
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
