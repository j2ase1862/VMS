using System.IO;
using System.Net.Sockets;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Omron FINS/TCP Protocol implementation.
    /// Compatible with CJ/CS/CP/NJ/NX series PLCs.
    /// Connection: TCP handshake -> FINS commands over TCP wrapper
    /// </summary>
    public class OmronFinsConnection : IPlcConnection
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private PlcConnectionConfig _config = new();
        private readonly SemaphoreSlim _commLock = new(1, 1);
        private readonly Dictionary<string, (PlcAddress Address, int IntervalMs, CancellationTokenSource Cts)> _monitors = new();
        private PlcConnectionState _connectionState = PlcConnectionState.Disconnected;
        private bool _disposed;

        private byte _clientNode;
        private byte _serverNode;
        private byte _sid;

        // FINS constants
        private static readonly byte[] FinsMagic = [0x46, 0x49, 0x4E, 0x53]; // "FINS"
        private const byte FinsIcf = 0x80;
        private const byte FinsGct = 0x02;

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

                // FINS/TCP node address exchange
                if (!await PerformFinsHandshake())
                {
                    _connectionState = PlcConnectionState.Error;
                    Cleanup();
                    return false;
                }

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
            var areaCode = GetAreaCode(address.DeviceCode, isBit: true);
            int wordOffset = address.Offset;
            int bitOffset = Math.Max(0, address.BitPosition);

            var data = await FinsReadAsync(areaCode, wordOffset, bitOffset, 1);
            return data.Length > 0 && data[1] != 0; // FINS response: high byte + low byte per element
        }

        public async Task WriteBitAsync(PlcAddress address, bool value)
        {
            var areaCode = GetAreaCode(address.DeviceCode, isBit: true);
            int wordOffset = address.Offset;
            int bitOffset = Math.Max(0, address.BitPosition);

            byte[] data = [0x00, (byte)(value ? 0x01 : 0x00)];
            await FinsWriteAsync(areaCode, wordOffset, bitOffset, data, 1);
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
            // Omron: big-endian word order
            return (words[0] << 16) | (ushort)words[1];
        }

        public async Task WriteDWordAsync(PlcAddress address, int value)
        {
            short high = (short)((value >> 16) & 0xFFFF);
            short low = (short)(value & 0xFFFF);
            await WriteWordsAsync(address, [high, low]);
        }

        // --- Block operations ---

        public async Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count)
        {
            var areaCode = GetAreaCode(startAddress.DeviceCode, isBit: false);
            var data = await FinsReadAsync(areaCode, startAddress.Offset, 0x00, count);

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
            var areaCode = GetAreaCode(startAddress.DeviceCode, isBit: false);
            var data = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                // Big-endian
                data[i * 2] = (byte)((values[i] >> 8) & 0xFF);
                data[i * 2 + 1] = (byte)(values[i] & 0xFF);
            }
            await FinsWriteAsync(areaCode, startAddress.Offset, 0x00, data, values.Length);
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

        // --- FINS/TCP Protocol ---

        private async Task<bool> PerformFinsHandshake()
        {
            // FINS/TCP handshake: client sends node address request
            byte[] handshake =
            [
                // FINS Header (magic)
                0x46, 0x49, 0x4E, 0x53,  // "FINS"
                // Length (8 bytes of payload)
                0x00, 0x00, 0x00, 0x0C,
                // Command: 0x00000000 = Node Address Data Send
                0x00, 0x00, 0x00, 0x00,
                // Error code: 0x00000000
                0x00, 0x00, 0x00, 0x00,
                // Client node address (0 = auto)
                0x00, 0x00, 0x00, (byte)(_config.SourceNode == 0 ? 0x00 : _config.SourceNode)
            ];

            await _stream!.WriteAsync(handshake);
            await _stream.FlushAsync();

            // Read response (24 bytes)
            var response = await ReadExactAsync(24);

            // Verify FINS magic
            if (response[0] != 0x46 || response[1] != 0x49 || response[2] != 0x4E || response[3] != 0x53)
                return false;

            // Check error code (bytes 12-15)
            int errorCode = (response[12] << 24) | (response[13] << 16) | (response[14] << 8) | response[15];
            if (errorCode != 0) return false;

            // Extract assigned node addresses
            _clientNode = response[19];
            _serverNode = response[23];

            return true;
        }

        private async Task<byte[]> FinsReadAsync(byte areaCode, int address, int bitAddress, int count)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                // FINS command data: MRC(1) + SRC(1) + AreaCode(1) + Address(2) + BitAddress(1) + Count(2)
                byte[] finsCommand =
                [
                    0x01, 0x01,  // Memory Area Read
                    areaCode,
                    (byte)((address >> 8) & 0xFF), (byte)(address & 0xFF),
                    (byte)bitAddress,
                    (byte)((count >> 8) & 0xFF), (byte)(count & 0xFF)
                ];

                var frame = BuildFinsTcpFrame(finsCommand);
                await _stream!.WriteAsync(frame);
                await _stream.FlushAsync();

                var response = await ReadFinsTcpResponse();
                return response;
            }
            finally
            {
                _commLock.Release();
            }
        }

        private async Task FinsWriteAsync(byte areaCode, int address, int bitAddress, byte[] data, int count)
        {
            await _commLock.WaitAsync();
            try
            {
                EnsureConnected();

                // FINS command: MRC(1) + SRC(1) + AreaCode(1) + Address(2) + BitAddress(1) + Count(2) + Data(n)
                var finsCommand = new byte[8 + data.Length];
                finsCommand[0] = 0x01;  // MRC: Memory Area Write
                finsCommand[1] = 0x02;  // SRC
                finsCommand[2] = areaCode;
                finsCommand[3] = (byte)((address >> 8) & 0xFF);
                finsCommand[4] = (byte)(address & 0xFF);
                finsCommand[5] = (byte)bitAddress;
                finsCommand[6] = (byte)((count >> 8) & 0xFF);
                finsCommand[7] = (byte)(count & 0xFF);
                Array.Copy(data, 0, finsCommand, 8, data.Length);

                var frame = BuildFinsTcpFrame(finsCommand);
                await _stream!.WriteAsync(frame);
                await _stream.FlushAsync();

                await ReadFinsTcpResponse();
            }
            finally
            {
                _commLock.Release();
            }
        }

        private byte[] BuildFinsTcpFrame(byte[] finsCommand)
        {
            // FINS header (10 bytes) + FINS command
            byte[] finsHeader =
            [
                FinsIcf,       // ICF: Command (bit 7), Response required (bit 6)
                0x00,          // RSV
                FinsGct,       // GCT: Permissible number of gateways
                _config.DestNode,   // DNA: Destination network address
                _serverNode,        // DA1: Destination node address
                0x00,               // DA2: Destination unit address
                0x00,               // SNA: Source network address
                _clientNode,        // SA1: Source node address
                0x00,               // SA2: Source unit address
                _sid++              // SID: Service ID
            ];

            int finsLen = finsHeader.Length + finsCommand.Length;

            // FINS/TCP wrapper: Magic(4) + Length(4) + Command(4) + ErrorCode(4) + FINS frame
            var frame = new byte[16 + finsLen];
            // Magic "FINS"
            Array.Copy(FinsMagic, 0, frame, 0, 4);
            // Length (total length minus header 8 bytes, but include command+error+fins)
            int payloadLen = 8 + finsLen;
            frame[4] = (byte)((payloadLen >> 24) & 0xFF);
            frame[5] = (byte)((payloadLen >> 16) & 0xFF);
            frame[6] = (byte)((payloadLen >> 8) & 0xFF);
            frame[7] = (byte)(payloadLen & 0xFF);
            // Command: 0x00000002 = FINS Frame Send
            frame[8] = 0x00; frame[9] = 0x00; frame[10] = 0x00; frame[11] = 0x02;
            // Error code: 0x00000000
            frame[12] = 0x00; frame[13] = 0x00; frame[14] = 0x00; frame[15] = 0x00;
            // FINS header + command
            Array.Copy(finsHeader, 0, frame, 16, finsHeader.Length);
            Array.Copy(finsCommand, 0, frame, 16 + finsHeader.Length, finsCommand.Length);

            return frame;
        }

        private async Task<byte[]> ReadFinsTcpResponse()
        {
            // Read FINS/TCP header (16 bytes): Magic(4) + Length(4) + Command(4) + ErrorCode(4)
            var tcpHeader = await ReadExactAsync(16);

            // Verify magic
            if (tcpHeader[0] != 0x46 || tcpHeader[1] != 0x49 || tcpHeader[2] != 0x4E || tcpHeader[3] != 0x53)
                throw new IOException("Invalid FINS/TCP response magic");

            int payloadLen = (tcpHeader[4] << 24) | (tcpHeader[5] << 16) | (tcpHeader[6] << 8) | tcpHeader[7];
            int tcpError = (tcpHeader[12] << 24) | (tcpHeader[13] << 16) | (tcpHeader[14] << 8) | tcpHeader[15];
            if (tcpError != 0)
                throw new IOException($"FINS/TCP error: 0x{tcpError:X8}");

            // Read remaining data (payloadLen - 8 for command+error already accounted)
            int remainLen = payloadLen - 8;
            if (remainLen <= 0) return [];

            var payload = await ReadExactAsync(remainLen);

            // payload: FINS header (10 bytes) + MRC(1) + SRC(1) + MRES(1) + SRES(1) + data
            if (payload.Length < 14)
                throw new IOException("FINS response too short");

            byte mres = payload[12];
            byte sres = payload[13];
            if (mres != 0x00 || sres != 0x00)
                throw new IOException($"FINS error: MRES=0x{mres:X2} SRES=0x{sres:X2}");

            // Return data portion (skip 14 bytes: FINS header + command response header)
            if (payload.Length <= 14) return [];
            return payload[14..];
        }

        // --- Area code mapping ---

        private static byte GetAreaCode(string device, bool isBit) => device.ToUpper() switch
        {
            "D" or "DM" => isBit ? (byte)0x02 : (byte)0x82,
            "CIO" => isBit ? (byte)0x30 : (byte)0xB0,
            "W" or "WR" => isBit ? (byte)0x31 : (byte)0xB1,
            "H" or "HR" => isBit ? (byte)0x32 : (byte)0xB2,
            "A" or "AR" => isBit ? (byte)0x33 : (byte)0xB3,
            "E" or "EM" => isBit ? (byte)0x20 : (byte)0xA0,
            _ => isBit ? (byte)0x02 : (byte)0x82 // Default to DM
        };

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
