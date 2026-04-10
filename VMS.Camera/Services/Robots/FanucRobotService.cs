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
    /// Fanuc Robotics 전용 TCP 서비스
    ///
    /// Fanuc KAREL/TP Socket Protocol:
    ///   - KAREL 프로그램이 소켓 서버로 동작
    ///   - 요청: "CURPOS\r\n"
    ///   - 응답 형식 (XYZWPR):
    ///     "CURPOS X:123.456 Y:234.567 Z:345.678 W:10.123 P:20.234 R:30.345\r\n"
    ///     또는 CSV: "123.456,234.567,345.678,10.123,20.234,30.345\r\n"
    ///   - 좌표 단위: mm, 각도: degrees (W=Yaw/Z, P=Pitch/Y, R=Roll/X)
    ///   - Fanuc WPR = Z-Y-X extrinsic Euler = X-Y-Z intrinsic
    ///
    /// 포즈 정지 확인:
    ///   - Fanuc은 모션 완료 대기가 중요 — settlingTime을 넉넉히 설정 권장
    /// </summary>
    public class FanucRobotService : TcpRobotService
    {
        /// <summary>KAREL 라벨 형식 파싱 사용 여부 (false면 CSV)</summary>
        public bool UseKarelFormat { get; set; } = true;

        public FanucRobotService()
        {
            Convention = EulerConvention.Fanuc_WPR;
            PoseRequestCommand = "CURPOS";
            Delimiter = "\r\n";
        }

        protected override RobotPose ParsePoseResponse(string response)
        {
            if (!UseKarelFormat)
                return base.ParsePoseResponse(response);

            return ParseKarelPose(response);
        }

        public override async Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            var response = await SendCommandAsync(PoseRequestCommand, timeoutMs);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            try
            {
                // 자동 형식 감지: "X:" 포함 여부로 KAREL vs CSV 판별
                if (response.Contains("X:") || response.Contains("x:"))
                    return ParseKarelPose(response);

                return ParsePoseResponse(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FanucRobotService] Pose parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// KAREL 라벨 형식 파싱
        /// "CURPOS X:123.456 Y:234.567 Z:345.678 W:10.123 P:20.234 R:30.345"
        /// 접두사(CURPOS 등)는 무시, X:/Y:/Z:/W:/P:/R: 라벨에서 값 추출
        /// </summary>
        private RobotPose ParseKarelPose(string response)
        {
            double x = 0, y = 0, z = 0, w = 0, p = 0, r = 0;

            // 공백 구분 토큰에서 라벨:값 추출
            var tokens = response.Split(new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var token in tokens)
            {
                var colonIdx = token.IndexOf(':');
                if (colonIdx < 1 || colonIdx >= token.Length - 1) continue;

                var label = token[..colonIdx].ToUpperInvariant();
                var valueStr = token[(colonIdx + 1)..];

                if (!double.TryParse(valueStr, CultureInfo.InvariantCulture, out double value))
                    continue;

                switch (label)
                {
                    case "X": x = value; break;
                    case "Y": y = value; break;
                    case "Z": z = value; break;
                    case "W": w = value; break;
                    case "P": p = value; break;
                    case "R": r = value; break;
                }
            }

            return new RobotPose
            {
                X = x,    // mm
                Y = y,
                Z = z,
                Rx = w,   // W (Yaw/Z rotation) → Rx in VMS
                Ry = p,   // P (Pitch/Y rotation)
                Rz = r,   // R (Roll/X rotation)
                Convention = Convention
            };
        }

        /// <summary>
        /// Fanuc은 모션 완료 판정이 중요 — 기본 settling time을 500ms로 확장
        /// </summary>
        public override async Task WaitForSettlingAsync(int settlingTimeMs = 500)
        {
            await Task.Delay(settlingTimeMs);
        }
    }
}
