using System.IO;
using System.Net.Sockets;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Modbus TCP protocol implementation.
    /// Supports FC1/2/3/4/5/6/15/16 across 4 standard areas (Coil/Discrete Input/Input Register/Holding Register).
    /// MBAP header: TxId(2) + ProtoId(2,=0) + Length(2) + UnitId(1) + PDU.
    /// </summary>
    public class ModbusTcpConnection : IPlcConnection
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private PlcConnectionConfig _config = new();
        private readonly SemaphoreSlim _commLock = new(1, 1);
        private readonly Dictionary<string, (PlcAddress Address, int IntervalMs, CancellationTokenSource Cts)> _monitors = new();
        private PlcConnectionState _connectionState = PlcConnectionState.Disconnected;
        private ushort _txId;
        private bool _disposed;

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
            catch (Exception ex)
            {
                SetConnectionState(PlcConnectionState.Error, ex.Message);
                Cleanup();
                return false;
            }
        }

        public Task DisconnectAsync()
        {
            Cleanup();
            SetConnectionState(PlcConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        // ─── Bit operations ─────────────────────────────────────────────────────────

        public async Task<bool> ReadBitAsync(PlcAddress address)
        {
            EnsureConnected();
            switch (address.DeviceCode)
            {
                case "0x":
                    return (await ReadCoilsAsync(address.Offset, 1))[0];
                case "1x":
                    return (await ReadDiscreteInputsAsync(address.Offset, 1))[0];
                case "4x" when address.IsBitAddress:
                    var reg = (await ReadHoldingRegistersAsync(address.Offset, 1))[0];
                    return GetBit(reg, address.BitPosition);
                default:
                    throw new InvalidOperationException(
                        $"Cannot read bit from area '{address.DeviceCode}' (use 0x/1x or 4x with bit suffix)");
            }
        }

        public async Task WriteBitAsync(PlcAddress address, bool value)
        {
            EnsureConnected();
            switch (address.DeviceCode)
            {
                case "0x":
                    await WriteSingleCoilAsync(address.Offset, value);
                    break;
                case "4x" when address.IsBitAddress:
                    var reg = (await ReadHoldingRegistersAsync(address.Offset, 1))[0];
                    reg = SetBit(reg, address.BitPosition, value);
                    await WriteSingleRegisterAsync(address.Offset, reg);
                    break;
                case "1x":
                case "3x":
                    throw new InvalidOperationException($"Area '{address.DeviceCode}' is read-only");
                default:
                    throw new InvalidOperationException($"Cannot write bit to area '{address.DeviceCode}'");
            }
        }

        // ─── Word operations ────────────────────────────────────────────────────────

        public async Task<short> ReadWordAsync(PlcAddress address)
        {
            EnsureConnected();
            var regs = address.DeviceCode switch
            {
                "4x" => await ReadHoldingRegistersAsync(address.Offset, 1),
                "3x" => await ReadInputRegistersAsync(address.Offset, 1),
                _ => throw new InvalidOperationException($"Word read not supported on area '{address.DeviceCode}'")
            };
            return (short)regs[0];
        }

        public async Task WriteWordAsync(PlcAddress address, short value)
        {
            EnsureConnected();
            if (address.DeviceCode != "4x")
                throw new InvalidOperationException($"Word write only supported on '4x' (got '{address.DeviceCode}')");
            await WriteSingleRegisterAsync(address.Offset, (ushort)value);
        }

        public async Task<int> ReadDWordAsync(PlcAddress address)
        {
            EnsureConnected();
            var regs = address.DeviceCode switch
            {
                "4x" => await ReadHoldingRegistersAsync(address.Offset, 2),
                "3x" => await ReadInputRegistersAsync(address.Offset, 2),
                _ => throw new InvalidOperationException($"DWord read not supported on area '{address.DeviceCode}'")
            };
            return CombineDWord(regs[0], regs[1]);
        }

        public async Task WriteDWordAsync(PlcAddress address, int value)
        {
            EnsureConnected();
            if (address.DeviceCode != "4x")
                throw new InvalidOperationException($"DWord write only supported on '4x' (got '{address.DeviceCode}')");
            var (hi, lo) = SplitDWord(value);
            await WriteMultipleRegistersAsync(address.Offset, new[] { hi, lo });
        }

        // ─── Block operations ───────────────────────────────────────────────────────

        public async Task<short[]> ReadWordsAsync(PlcAddress startAddress, int count)
        {
            EnsureConnected();
            var regs = startAddress.DeviceCode switch
            {
                "4x" => await ReadHoldingRegistersAsync(startAddress.Offset, count),
                "3x" => await ReadInputRegistersAsync(startAddress.Offset, count),
                _ => throw new InvalidOperationException($"Block read not supported on area '{startAddress.DeviceCode}'")
            };
            var result = new short[regs.Length];
            for (int i = 0; i < regs.Length; i++) result[i] = (short)regs[i];
            return result;
        }

        public async Task WriteWordsAsync(PlcAddress startAddress, short[] values)
        {
            EnsureConnected();
            if (startAddress.DeviceCode != "4x")
                throw new InvalidOperationException($"Block write only supported on '4x' (got '{startAddress.DeviceCode}')");
            var regs = new ushort[values.Length];
            for (int i = 0; i < values.Length; i++) regs[i] = (ushort)values[i];
            await WriteMultipleRegistersAsync(startAddress.Offset, regs);
        }

        // ─── Monitoring (polling) ───────────────────────────────────────────────────

        public Task StartMonitoringAsync(PlcAddress address, int pollingIntervalMs = 50)
        {
            var key = address.ToKey();
            if (_monitors.ContainsKey(key)) return Task.CompletedTask;

            var cts = new CancellationTokenSource();
            _monitors[key] = (address, pollingIntervalMs, cts);

            _ = Task.Run(async () =>
            {
                bool? last = null;
                while (!cts.IsCancellationRequested && IsConnected)
                {
                    try
                    {
                        var cur = await ReadBitAsync(address);
                        if (last != cur)
                        {
                            last = cur;
                            BitChanged?.Invoke(this, new PlcBitChangedEventArgs(address, cur));
                        }
                    }
                    catch { /* swallow; next poll retries */ }
                    try { await Task.Delay(pollingIntervalMs, cts.Token); } catch { break; }
                }
            }, cts.Token);

            return Task.CompletedTask;
        }

        public Task StopMonitoringAsync(PlcAddress address)
        {
            var key = address.ToKey();
            if (_monitors.TryGetValue(key, out var m))
            {
                m.Cts.Cancel();
                m.Cts.Dispose();
                _monitors.Remove(key);
            }
            return Task.CompletedTask;
        }

        public Task StopAllMonitoringAsync()
        {
            foreach (var m in _monitors.Values) { m.Cts.Cancel(); m.Cts.Dispose(); }
            _monitors.Clear();
            return Task.CompletedTask;
        }

        // ─── Modbus PDU helpers ─────────────────────────────────────────────────────

        private async Task<bool[]> ReadCoilsAsync(int startOffset, int count) =>
            await ReadBitsAsync(0x01, startOffset, count);

        private async Task<bool[]> ReadDiscreteInputsAsync(int startOffset, int count) =>
            await ReadBitsAsync(0x02, startOffset, count);

        private async Task<ushort[]> ReadHoldingRegistersAsync(int startOffset, int count) =>
            await ReadRegistersAsync(0x03, startOffset, count);

        private async Task<ushort[]> ReadInputRegistersAsync(int startOffset, int count) =>
            await ReadRegistersAsync(0x04, startOffset, count);

        private async Task<bool[]> ReadBitsAsync(byte fc, int startOffset, int count)
        {
            // PDU: FC(1) + StartAddr(2) + Quantity(2) = 5
            var pdu = new byte[5];
            pdu[0] = fc;
            pdu[1] = (byte)(startOffset >> 8);
            pdu[2] = (byte)(startOffset & 0xFF);
            pdu[3] = (byte)(count >> 8);
            pdu[4] = (byte)(count & 0xFF);

            var resp = await SendRequestAsync(pdu);
            // Response: FC(1) + ByteCount(1) + Bits(N)
            if (resp.Length < 2) throw new IOException("Modbus: short read-bits response");
            int byteCount = resp[1];
            if (resp.Length < 2 + byteCount) throw new IOException("Modbus: truncated read-bits response");

            var bits = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int byteIdx = i / 8;
                int bitIdx = i % 8;
                bits[i] = (resp[2 + byteIdx] & (1 << bitIdx)) != 0;
            }
            return bits;
        }

        private async Task<ushort[]> ReadRegistersAsync(byte fc, int startOffset, int count)
        {
            var pdu = new byte[5];
            pdu[0] = fc;
            pdu[1] = (byte)(startOffset >> 8);
            pdu[2] = (byte)(startOffset & 0xFF);
            pdu[3] = (byte)(count >> 8);
            pdu[4] = (byte)(count & 0xFF);

            var resp = await SendRequestAsync(pdu);
            // Response: FC(1) + ByteCount(1) + Data(N*2)
            if (resp.Length < 2) throw new IOException("Modbus: short read-registers response");
            int byteCount = resp[1];
            if (resp.Length < 2 + byteCount) throw new IOException("Modbus: truncated read-registers response");

            var regs = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                regs[i] = (ushort)((resp[2 + i * 2] << 8) | resp[3 + i * 2]);
            }
            return regs;
        }

        private async Task WriteSingleCoilAsync(int offset, bool value)
        {
            // FC=05, Addr(2), Value(2): FF00=on, 0000=off
            var pdu = new byte[5];
            pdu[0] = 0x05;
            pdu[1] = (byte)(offset >> 8);
            pdu[2] = (byte)(offset & 0xFF);
            pdu[3] = value ? (byte)0xFF : (byte)0x00;
            pdu[4] = 0x00;
            await SendRequestAsync(pdu);
        }

        private async Task WriteSingleRegisterAsync(int offset, ushort value)
        {
            // FC=06, Addr(2), Value(2)
            var pdu = new byte[5];
            pdu[0] = 0x06;
            pdu[1] = (byte)(offset >> 8);
            pdu[2] = (byte)(offset & 0xFF);
            pdu[3] = (byte)(value >> 8);
            pdu[4] = (byte)(value & 0xFF);
            await SendRequestAsync(pdu);
        }

        private async Task WriteMultipleRegistersAsync(int offset, ushort[] values)
        {
            // FC=16, Addr(2), Qty(2), ByteCount(1), Data(N*2)
            int n = values.Length;
            var pdu = new byte[6 + n * 2];
            pdu[0] = 0x10;
            pdu[1] = (byte)(offset >> 8);
            pdu[2] = (byte)(offset & 0xFF);
            pdu[3] = (byte)(n >> 8);
            pdu[4] = (byte)(n & 0xFF);
            pdu[5] = (byte)(n * 2);
            for (int i = 0; i < n; i++)
            {
                pdu[6 + i * 2] = (byte)(values[i] >> 8);
                pdu[7 + i * 2] = (byte)(values[i] & 0xFF);
            }
            await SendRequestAsync(pdu);
        }

        // ─── Wire framing (MBAP) ────────────────────────────────────────────────────

        /// <summary>Send PDU and return the response PDU. Handles MBAP header and Modbus exceptions.</summary>
        private async Task<byte[]> SendRequestAsync(byte[] pdu)
        {
            if (_stream == null) throw new InvalidOperationException("Modbus: not connected");

            await _commLock.WaitAsync();
            try
            {
                ushort tx = ++_txId;
                int len = pdu.Length + 1;  // +1 for UnitId
                var frame = new byte[7 + pdu.Length];
                frame[0] = (byte)(tx >> 8);
                frame[1] = (byte)(tx & 0xFF);
                frame[2] = 0x00;  // ProtoId hi
                frame[3] = 0x00;  // ProtoId lo
                frame[4] = (byte)(len >> 8);
                frame[5] = (byte)(len & 0xFF);
                frame[6] = _config.UnitId;
                Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);

                await _stream.WriteAsync(frame.AsMemory(0, frame.Length));

                // Read MBAP header (7 bytes), then payload
                var hdr = new byte[7];
                await ReadExactAsync(_stream, hdr, 7);
                ushort rxTx = (ushort)((hdr[0] << 8) | hdr[1]);
                int rxLen = (hdr[4] << 8) | hdr[5];
                if (rxTx != tx) throw new IOException($"Modbus: TxId mismatch (expected {tx}, got {rxTx})");
                if (rxLen < 2) throw new IOException("Modbus: invalid length field");

                var payload = new byte[rxLen - 1];  // -1 to exclude UnitId already in hdr[6]
                await ReadExactAsync(_stream, payload, payload.Length);

                // Modbus exception: high bit of function code set
                if ((payload[0] & 0x80) != 0)
                {
                    byte exCode = payload.Length > 1 ? payload[1] : (byte)0;
                    throw new IOException($"Modbus exception {exCode} (function 0x{payload[0]:X2})");
                }
                return payload;
            }
            finally
            {
                _commLock.Release();
            }
        }

        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, count - read));
                if (n == 0) throw new IOException("Modbus: connection closed");
                read += n;
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────────────

        private static bool GetBit(ushort word, int bit) => (word & (1 << bit)) != 0;
        private static ushort SetBit(ushort word, int bit, bool value) =>
            (ushort)(value ? (word | (1 << bit)) : (word & ~(1 << bit)));

        private int CombineDWord(ushort hi, ushort lo)
        {
            // Default little-endian word order (lo word first), matching most PLCs
            return _config.EndianMode == PlcEndianMode.BigEndian
                ? ((hi << 16) | lo)
                : ((lo << 16) | hi);
        }

        private (ushort hi, ushort lo) SplitDWord(int value)
        {
            ushort high = (ushort)((value >> 16) & 0xFFFF);
            ushort low = (ushort)(value & 0xFFFF);
            return _config.EndianMode == PlcEndianMode.BigEndian
                ? (high, low)
                : (low, high);
        }

        private void EnsureConnected()
        {
            if (!IsConnected) throw new InvalidOperationException("Modbus: not connected");
        }

        private void SetConnectionState(PlcConnectionState state, string? reason = null)
        {
            if (_connectionState == state) return;
            var old = _connectionState;
            _connectionState = state;
            ConnectionStateChanged?.Invoke(this,
                new PlcConnectionStateChangedEventArgs(old, state, reason));
        }

        private void Cleanup()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _stream = null;
            _client = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAllMonitoringAsync().GetAwaiter().GetResult();
            Cleanup();
            _commLock.Dispose();
        }
    }
}
