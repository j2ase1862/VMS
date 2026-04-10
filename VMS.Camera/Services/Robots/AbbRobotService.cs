using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Camera.Models;

namespace VMS.Camera.Services.Robots
{
    /// <summary>
    /// ABB Robotics 전용 TCP 서비스
    ///
    /// ABB RAPID Socket Messaging 프로토콜:
    ///   - RAPID 프로그램이 소켓 서버로 동작, 외부에서 TCP 클라이언트로 연결
    ///   - 요청: "GET_POSE\r\n"
    ///   - 응답: "[x,y,z],[q1,q2,q3,q4]\r\n" (robtarget 형식)
    ///     또는: "[[x,y,z],[q1,q2,q3,q4]]\r\n"
    ///   - 좌표 단위: mm, 회전: 쿼터니언 (q1,q2,q3,q4 — ABB 순서: w=q1)
    ///   - ABB 쿼터니언 순서: q1=w, q2=x, q3=y, q4=z
    ///     → VMS Convention에서는 Rx=q2, Ry=q3, Rz=q4, Q4=q1
    ///
    /// 커스텀 소켓 서버 모드:
    ///   - UseRapidProtocol=false 시 CSV 텍스트 폴백
    /// </summary>
    public class AbbRobotService : TcpRobotService
    {
        /// <summary>RAPID 프로토콜 사용 여부</summary>
        public bool UseRapidProtocol { get; set; } = true;

        public AbbRobotService()
        {
            Convention = EulerConvention.ABB_Quaternion;
            PoseRequestCommand = "GET_POSE";
            Delimiter = "\r\n";
        }

        protected override RobotPose ParsePoseResponse(string response)
        {
            if (!UseRapidProtocol)
                return base.ParsePoseResponse(response);

            return ParseRapidRobtarget(response);
        }

        public override async Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            if (!UseRapidProtocol)
                return await base.GetCurrentPoseAsync(timeoutMs);

            // RAPID 프로토콜: 텍스트 커맨드 전송 → 브래킷 구분 응답 파싱
            var response = await SendCommandAsync(PoseRequestCommand, timeoutMs);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            try
            {
                return ParseRapidRobtarget(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AbbRobotService] RAPID parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ABB robtarget 형식 파싱
        /// 형식 1: "[x,y,z],[q1,q2,q3,q4]"
        /// 형식 2: "[[x,y,z],[q1,q2,q3,q4]]"
        /// ABB 쿼터니언 순서: q1=w(스칼라), q2=x, q3=y, q4=z
        /// </summary>
        private RobotPose ParseRapidRobtarget(string response)
        {
            // 브래킷 내부 숫자만 추출
            var cleaned = response.Replace("[", "").Replace("]", "").Trim();
            var parts = cleaned.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 7)
                throw new FormatException($"ABB robtarget requires 7 values (pos+quat), got {parts.Length}: {response}");

            double x = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double y = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double z = double.Parse(parts[2], CultureInfo.InvariantCulture);

            // ABB 순서: q1=w, q2=x, q3=y, q4=z
            double q1 = double.Parse(parts[3], CultureInfo.InvariantCulture); // w (scalar)
            double q2 = double.Parse(parts[4], CultureInfo.InvariantCulture); // x
            double q3 = double.Parse(parts[5], CultureInfo.InvariantCulture); // y
            double q4 = double.Parse(parts[6], CultureInfo.InvariantCulture); // z

            return new RobotPose
            {
                X = x,      // mm 그대로
                Y = y,
                Z = z,
                Rx = q2,    // VMS Convention: Rx=qx
                Ry = q3,    // Ry=qy
                Rz = q4,    // Rz=qz
                Q4 = q1,    // Q4=qw (ABB의 q1)
                Convention = Convention
            };
        }

        /// <summary>
        /// RAPID 응답은 '\r\n'으로 종료되며, 때때로 여러 줄이 올 수 있음.
        /// 마지막 비어있지 않은 줄을 결과로 사용
        /// </summary>
        protected override async Task<string?> ReceiveResponseAsync(CancellationToken ct)
        {
            var raw = await base.ReceiveResponseAsync(ct);
            if (raw == null) return null;

            // 여러 줄일 경우 마지막 의미 있는 줄 반환
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[^1].Trim() : raw;
        }
    }
}
