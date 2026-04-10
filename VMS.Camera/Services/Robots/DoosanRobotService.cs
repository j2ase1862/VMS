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
    /// Doosan Robotics 전용 TCP 서비스
    ///
    /// Doosan DRL (Doosan Robot Language) 소켓 프로토콜:
    ///   - JSON 기반 요청/응답 구조
    ///   - 요청: {"cmd": "get_current_posx", "id": N}
    ///   - 응답: {"result": [x, y, z, a, b, c], "id": N, "status": "OK"}
    ///   - 좌표 단위: mm, 각도: degrees (Z-Y-X intrinsic)
    ///   - Delimiter: '\n' (JSON per line)
    ///
    /// 커스텀 소켓 서버 모드:
    ///   - PoseRequestCommand를 변경하면 텍스트 CSV 프로토콜로 폴백
    /// </summary>
    public class DoosanRobotService : TcpRobotService
    {
        private int _requestId;

        /// <summary>JSON 프로토콜 사용 여부 (false면 텍스트 CSV 폴백)</summary>
        public bool UseJsonProtocol { get; set; } = true;

        public DoosanRobotService()
        {
            Convention = EulerConvention.Doosan_ZYX;
            PoseRequestCommand = "get_current_posx";
            Delimiter = "\n";
        }

        public override async Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            if (!UseJsonProtocol)
                return await base.GetCurrentPoseAsync(timeoutMs);

            if (Stream == null || !IsConnected) return null;

            try
            {
                int id = Interlocked.Increment(ref _requestId);

                // JSON 요청 전송
                var request = JsonSerializer.Serialize(new
                {
                    cmd = PoseRequestCommand,
                    id
                });

                var sendData = Encoding.UTF8.GetBytes(request + Delimiter);
                using var cts = new CancellationTokenSource(timeoutMs);

                await Stream.WriteAsync(sendData, cts.Token);
                await Stream.FlushAsync(cts.Token);

                // JSON 응답 수신
                var response = await ReceiveResponseAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(response))
                    return null;

                return ParseDoosanJsonResponse(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DoosanRobotService] Pose read error: {ex.Message}");
                return null;
            }
        }

        protected override RobotPose ParsePoseResponse(string response)
        {
            if (UseJsonProtocol)
                return ParseDoosanJsonResponse(response);

            return base.ParsePoseResponse(response);
        }

        private RobotPose ParseDoosanJsonResponse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 상태 확인
            if (root.TryGetProperty("status", out var statusProp))
            {
                var status = statusProp.GetString();
                if (status != "OK" && status != "ok")
                    throw new InvalidOperationException($"Doosan error status: {status}");
            }

            // 결과 배열: [x, y, z, a, b, c]
            var result = root.GetProperty("result");

            return new RobotPose
            {
                X = result[0].GetDouble(),
                Y = result[1].GetDouble(),
                Z = result[2].GetDouble(),
                Rx = result[3].GetDouble(),   // a (deg)
                Ry = result[4].GetDouble(),   // b (deg)
                Rz = result[5].GetDouble(),   // c (deg)
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
                int id = Interlocked.Increment(ref _requestId);
                var request = JsonSerializer.Serialize(new { cmd = command, id });
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
