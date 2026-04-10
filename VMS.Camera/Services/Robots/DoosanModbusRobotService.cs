using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services.Robots
{
    /// <summary>
    /// Doosan 로봇 전용 Modbus-TCP 서비스
    ///
    /// Doosan 로봇 컨트롤러는 Modbus-TCP 서버(포트 502)를 내장하고 있어
    /// 별도의 DRL 프로그램 없이도 실시간으로 TCP 포즈를 모니터링할 수 있습니다.
    ///
    /// Modbus 레지스터 맵 (Holding Registers, Function Code 0x03):
    ///   기본 시작 주소 270 (설정 가능):
    ///   - Reg 270~271: TCP X (mm) — 32-bit float, Big Endian
    ///   - Reg 272~273: TCP Y (mm)
    ///   - Reg 274~275: TCP Z (mm)
    ///   - Reg 276~277: TCP Rx (deg) — ZYX Euler
    ///   - Reg 278~279: TCP Ry (deg)
    ///   - Reg 280~281: TCP Rz (deg)
    ///
    /// 프로토콜:
    ///   - MBAP 헤더 (7 bytes): TransactionID(2) + ProtocolID(2) + Length(2) + UnitID(1)
    ///   - PDU: FunctionCode(1) + StartAddress(2) + Quantity(2)
    ///   - 응답: MBAP(7) + FunctionCode(1) + ByteCount(1) + Data(N)
    /// </summary>
    public class DoosanModbusRobotService : IRobotService
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _disposed;
        private int _transactionId;

        /// <summary>Modbus Unit ID (기본 1)</summary>
        public byte UnitId { get; set; } = 1;

        /// <summary>TCP 포즈 시작 레지스터 주소 (기본 270)</summary>
        public ushort PoseStartRegister { get; set; } = 270;

        /// <summary>폴링 간격 (ms) — 연속 읽기 시 사용</summary>
        public int PollingIntervalMs { get; set; } = 50;

        public bool IsConnected => _client?.Connected == true;

        public EulerConvention Convention { get; set; } = EulerConvention.Doosan_ZYX;

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            try
            {
                await DisconnectAsync();

                _client = new TcpClient
                {
                    ReceiveTimeout = 3000,
                    SendTimeout = 3000,
                    NoDelay = true
                };

                await _client.ConnectAsync(ipAddress, port);
                _stream = _client.GetStream();

                // 연결 검증: 레지스터 1개 읽기 테스트
                var testResult = await ReadHoldingRegistersAsync(PoseStartRegister, 2);
                if (testResult == null)
                {
                    Debug.WriteLine("[DoosanModbus] Connection test failed - could not read registers");
                    await DisconnectAsync();
                    return false;
                }

                Debug.WriteLine($"[DoosanModbus] Connected to {ipAddress}:{port}, Unit ID={UnitId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DoosanModbus] Connect error: {ex.Message}");
                _client?.Dispose();
                _client = null;
                _stream = null;
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_stream != null)
            {
                try { _stream.Close(); } catch { }
                _stream = null;
            }

            if (_client != null)
            {
                try { _client.Close(); } catch { }
                _client?.Dispose();
                _client = null;
            }

            await Task.CompletedTask;
        }

        public async Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            if (_stream == null || !IsConnected)
                return null;

            try
            {
                // 6축 * 2레지스터(32-bit float) = 12 레지스터 읽기
                var registers = await ReadHoldingRegistersAsync(PoseStartRegister, 12, timeoutMs);
                if (registers == null || registers.Length < 12)
                    return null;

                return new RobotPose
                {
                    X = RegistersToFloat(registers, 0),
                    Y = RegistersToFloat(registers, 2),
                    Z = RegistersToFloat(registers, 4),
                    Rx = RegistersToFloat(registers, 6),   // deg (ZYX Euler)
                    Ry = RegistersToFloat(registers, 8),
                    Rz = RegistersToFloat(registers, 10),
                    Convention = Convention
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DoosanModbus] Pose read error: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> SendCommandAsync(string command, int timeoutMs = 3000)
        {
            // Modbus-TCP는 레지스터 읽기/쓰기만 지원 — 임의 커맨드 전송 불가
            Debug.WriteLine("[DoosanModbus] SendCommand not supported in Modbus mode. Use DRL JSON protocol for command execution.");
            return await Task.FromResult<string?>(null);
        }

        public async Task WaitForSettlingAsync(int settlingTimeMs = 200)
        {
            await Task.Delay(settlingTimeMs);
        }

        #region Modbus-TCP Protocol

        /// <summary>
        /// Modbus Function 0x03: Read Holding Registers
        /// </summary>
        /// <param name="startAddress">시작 레지스터 주소</param>
        /// <param name="quantity">읽을 레지스터 수 (최대 125)</param>
        /// <param name="timeoutMs">타임아웃</param>
        /// <returns>레지스터 값 배열 (ushort[]), 실패 시 null</returns>
        private async Task<ushort[]?> ReadHoldingRegistersAsync(
            ushort startAddress, ushort quantity, int timeoutMs = 3000)
        {
            if (_stream == null) return null;

            ushort txId = (ushort)Interlocked.Increment(ref _transactionId);

            // MBAP Header (7 bytes) + PDU (5 bytes) = 12 bytes
            var request = new byte[12];

            // MBAP Header
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0), txId);           // Transaction ID
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2), 0);              // Protocol ID (0 = Modbus)
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), 6);              // Length (Unit ID + PDU)
            request[6] = UnitId;                                                       // Unit ID

            // PDU
            request[7] = 0x03;                                                         // Function Code: Read Holding Registers
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8), startAddress);    // Start Address
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10), quantity);       // Quantity

            using var cts = new CancellationTokenSource(timeoutMs);

            // 요청 전송
            await _stream.WriteAsync(request, cts.Token);
            await _stream.FlushAsync(cts.Token);

            // MBAP 헤더 수신 (7 bytes)
            var header = new byte[7];
            if (!await ReadExactAsync(_stream, header, 7, cts.Token))
                return null;

            ushort responseTxId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0));
            ushort responseLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));

            // PDU 수신
            var pdu = new byte[responseLength - 1]; // Length에서 Unit ID 1바이트 제외 (이미 header에 포함)
            if (!await ReadExactAsync(_stream, pdu, pdu.Length, cts.Token))
                return null;

            // 에러 응답 확인 (Function Code MSB = 1)
            if ((pdu[0] & 0x80) != 0)
            {
                byte errorCode = pdu.Length > 1 ? pdu[1] : (byte)0;
                Debug.WriteLine($"[DoosanModbus] Error response: FC=0x{pdu[0]:X2}, Error=0x{errorCode:X2}");
                return null;
            }

            // 정상 응답: FC(1) + ByteCount(1) + Data(N)
            if (pdu[0] != 0x03) return null;
            byte byteCount = pdu[1];

            if (byteCount != quantity * 2 || pdu.Length < 2 + byteCount)
                return null;

            // 레지스터 값 파싱
            var registers = new ushort[quantity];
            for (int i = 0; i < quantity; i++)
            {
                registers[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(2 + i * 2));
            }

            return registers;
        }

        /// <summary>
        /// 2개의 연속 Modbus 레지스터(16-bit)를 32-bit float로 변환 (Big Endian)
        /// Doosan은 Big Endian word order 사용: Hi word first, Lo word second
        /// </summary>
        private static float RegistersToFloat(ushort[] registers, int startIndex)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), registers[startIndex]);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), registers[startIndex + 1]);

            return BinaryPrimitives.ReadSingleBigEndian(bytes);
        }

        private static async Task<bool> ReadExactAsync(
            NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, ct);
                if (read == 0) return false;
                totalRead += read;
            }
            return true;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stream?.Close();
            _stream?.Dispose();
            _client?.Close();
            _client?.Dispose();
        }
    }
}
