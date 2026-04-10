using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Camera.Models;

namespace VMS.Camera.Services.Robots
{
    /// <summary>
    /// Universal Robots 전용 TCP 서비스
    ///
    /// UR Real-Time Interface (포트 30003):
    ///   - 연결 즉시 125Hz(8ms 간격)로 1116바이트 바이너리 패킷 전송
    ///   - 포즈 요청 없이 최신 패킷을 파싱하여 TCP 포즈 추출
    ///   - 바이트 오프셋 444~491: Tool Vector (X,Y,Z,Rx,Ry,Rz) — double BE x6
    ///   - 단위: m/rad → mm/rad로 변환 (위치만 x1000)
    ///
    /// 커스텀 소켓 서버 모드:
    ///   - 사용자 로봇 프로그램의 소켓 서버에 텍스트 명령 전송
    ///   - PoseRequestCommand를 "REALTIME"이 아닌 값으로 설정 시 텍스트 프로토콜 사용
    /// </summary>
    public class UrRobotService : TcpRobotService
    {
        /// <summary>UR Real-Time 패킷 크기 (CB3/e-Series 공용)</summary>
        private const int UrRtPacketMinSize = 1044;

        /// <summary>Tool Vector 시작 오프셋 (bytes) — 표준 RT 인터페이스</summary>
        private const int ToolVectorOffset = 444;

        /// <summary>Real-Time 인터페이스 모드 여부</summary>
        public bool UseRealtimeInterface { get; set; } = true;

        public UrRobotService()
        {
            Convention = EulerConvention.UR_RotationVector;
            PoseRequestCommand = "GET_POSE";
            Delimiter = "\n";
            ReceiveBufferSize = 2048;
        }

        protected override async Task OnConnectedAsync()
        {
            if (!UseRealtimeInterface || Stream == null) return;

            // RT 인터페이스는 연결 즉시 데이터를 보내기 시작 — 첫 패킷 수신으로 연결 확인
            try
            {
                using var cts = new CancellationTokenSource(3000);
                var headerBuf = new byte[4];
                int read = await Stream.ReadAsync(headerBuf, 0, 4, cts.Token);
                if (read == 4)
                {
                    int packetSize = BinaryPrimitives.ReadInt32BigEndian(headerBuf);
                    // 나머지 패킷 소비 (첫 패킷 드레인)
                    var drain = new byte[packetSize - 4];
                    await ReadExactAsync(Stream, drain, packetSize - 4, cts.Token);
                    Debug.WriteLine($"[UrRobotService] RT handshake OK, packet size={packetSize}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UrRobotService] RT handshake warning: {ex.Message}");
                // 핸드셰이크 실패해도 텍스트 모드 폴백 가능
                UseRealtimeInterface = false;
            }
        }

        public override async Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            if (!UseRealtimeInterface)
            {
                // 텍스트 프로토콜 폴백 (사용자 소켓 서버)
                return await base.GetCurrentPoseAsync(timeoutMs);
            }

            // Real-Time 인터페이스: 최신 바이너리 패킷에서 포즈 추출
            if (Stream == null || !IsConnected) return null;

            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);

                // 패킷 헤더 (4바이트 BE int = 패킷 크기)
                var headerBuf = new byte[4];
                if (!await ReadExactAsync(Stream, headerBuf, 4, cts.Token))
                    return null;

                int packetSize = BinaryPrimitives.ReadInt32BigEndian(headerBuf);
                if (packetSize < UrRtPacketMinSize)
                {
                    // 예상보다 작은 패킷 — 드레인 후 재시도
                    var drain = new byte[packetSize - 4];
                    await ReadExactAsync(Stream, drain, packetSize - 4, cts.Token);
                    return null;
                }

                // 나머지 패킷 수신
                var packetBody = new byte[packetSize - 4];
                if (!await ReadExactAsync(Stream, packetBody, packetSize - 4, cts.Token))
                    return null;

                // Tool Vector 추출 (offset 444에서 헤더 4바이트 뺀 440부터)
                int bodyOffset = ToolVectorOffset - 4;
                if (bodyOffset + 48 > packetBody.Length)
                    return null;

                double x = BinaryPrimitives.ReadDoubleBigEndian(packetBody.AsSpan(bodyOffset));
                double y = BinaryPrimitives.ReadDoubleBigEndian(packetBody.AsSpan(bodyOffset + 8));
                double z = BinaryPrimitives.ReadDoubleBigEndian(packetBody.AsSpan(bodyOffset + 16));
                double rx = BinaryPrimitives.ReadDoubleBigEndian(packetBody.AsSpan(bodyOffset + 24));
                double ry = BinaryPrimitives.ReadDoubleBigEndian(packetBody.AsSpan(bodyOffset + 32));
                double rz = BinaryPrimitives.ReadDoubleBigEndian(packetBody.AsSpan(bodyOffset + 40));

                return new RobotPose
                {
                    X = x * 1000.0,  // m → mm
                    Y = y * 1000.0,
                    Z = z * 1000.0,
                    Rx = rx,          // rad 그대로
                    Ry = ry,
                    Rz = rz,
                    Convention = Convention
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UrRobotService] RT pose read error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// UR RT 인터페이스는 연속 스트림이므로 SendCommand는 텍스트 모드에서만 의미가 있음
        /// RT 모드에서는 UR Script를 Secondary Interface (30002)로 보내야 함
        /// </summary>
        public override async Task<string?> SendCommandAsync(string command, int timeoutMs = 3000)
        {
            if (UseRealtimeInterface)
            {
                // RT 모드에서는 커맨드 전송 불가 (read-only 스트림)
                Debug.WriteLine("[UrRobotService] RT interface is read-only, use Secondary port (30002) for commands");
                return null;
            }

            return await base.SendCommandAsync(command, timeoutMs);
        }

        private static async Task<bool> ReadExactAsync(
            System.Net.Sockets.NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
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
    }
}
