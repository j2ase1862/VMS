using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VMS.Camera.Models;

namespace VMS.Camera.Services.Robots
{
    /// <summary>
    /// Jaka Robotics 전용 TCP 서비스
    ///
    /// Jaka SDK 소켓 프로토콜:
    ///   - JSON 기반 요청/응답 구조
    ///   - 요청: {"cmdName": "get_tcp_pos"}
    ///   - 응답: {"cmdName": "get_tcp_pos", "errorCode": 0, "tcp_pos": [x, y, z, rx, ry, rz]}
    ///   - 좌표 단위: mm, 각도: degrees (X-Y-Z intrinsic)
    ///   - 응답 끝에 '\n' 구분자
    ///
    /// 커스텀 소켓 서버 모드:
    ///   - UseJsonProtocol=false로 설정 시 텍스트 CSV 프로토콜 폴백
    /// </summary>
    public class JakaRobotService : TcpRobotService
    {
        /// <summary>JSON 프로토콜 사용 여부</summary>
        public bool UseJsonProtocol { get; set; } = true;

        public JakaRobotService()
        {
            Convention = EulerConvention.Jaka_XYZ;
            PoseRequestCommand = "get_tcp_pos";
            Delimiter = "\n";
        }

        public override async Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            if (!UseJsonProtocol)
                return await base.GetCurrentPoseAsync(timeoutMs);

            if (Stream == null || !IsConnected) return null;

            try
            {
                // JSON 요청 전송
                var request = JsonSerializer.Serialize(new { cmdName = PoseRequestCommand });
                var sendData = Encoding.UTF8.GetBytes(request + Delimiter);
                using var cts = new CancellationTokenSource(timeoutMs);

                await Stream.WriteAsync(sendData, cts.Token);
                await Stream.FlushAsync(cts.Token);

                // JSON 응답 수신
                var response = await ReceiveResponseAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(response))
                    return null;

                return ParseJakaJsonResponse(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JakaRobotService] Pose read error: {ex.Message}");
                return null;
            }
        }

        protected override RobotPose ParsePoseResponse(string response)
        {
            if (UseJsonProtocol)
                return ParseJakaJsonResponse(response);

            return base.ParsePoseResponse(response);
        }

        private RobotPose ParseJakaJsonResponse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 에러 코드 확인
            if (root.TryGetProperty("errorCode", out var errProp) && errProp.GetInt32() != 0)
            {
                var errMsg = root.TryGetProperty("errorMsg", out var msgProp)
                    ? msgProp.GetString() : "unknown";
                throw new InvalidOperationException($"Jaka error {errProp.GetInt32()}: {errMsg}");
            }

            // TCP 포즈 배열: [x, y, z, rx, ry, rz]
            var tcpPos = root.GetProperty("tcp_pos");

            return new RobotPose
            {
                X = tcpPos[0].GetDouble(),
                Y = tcpPos[1].GetDouble(),
                Z = tcpPos[2].GetDouble(),
                Rx = tcpPos[3].GetDouble(),   // deg
                Ry = tcpPos[4].GetDouble(),   // deg
                Rz = tcpPos[5].GetDouble(),   // deg
                Convention = Convention
            };
        }

        public override async Task<string?> SendCommandAsync(string command, int timeoutMs = 3000)
        {
            if (!UseJsonProtocol)
                return await base.SendCommandAsync(command, timeoutMs);

            if (Stream == null || !IsConnected) return null;

            try
            {
                var request = JsonSerializer.Serialize(new { cmdName = command });
                var sendData = Encoding.UTF8.GetBytes(request + Delimiter);
                using var cts = new CancellationTokenSource(timeoutMs);

                await Stream.WriteAsync(sendData, cts.Token);
                await Stream.FlushAsync(cts.Token);

                return await ReceiveResponseAsync(cts.Token);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
