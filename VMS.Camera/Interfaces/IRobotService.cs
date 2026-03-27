using VMS.Camera.Models;

namespace VMS.Camera.Interfaces
{
    /// <summary>
    /// 로봇 통신 및 포즈 수집 인터페이스
    /// </summary>
    public interface IRobotService : IDisposable
    {
        /// <summary>연결 상태</summary>
        bool IsConnected { get; }

        /// <summary>로봇 제조사별 회전 표현 방식</summary>
        EulerConvention Convention { get; set; }

        /// <summary>
        /// 로봇 컨트롤러에 연결
        /// </summary>
        /// <param name="ipAddress">로봇 IP 주소</param>
        /// <param name="port">포트 번호</param>
        Task<bool> ConnectAsync(string ipAddress, int port);

        /// <summary>
        /// 연결 해제
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 현재 TCP 포즈 요청 (로봇이 정지 상태일 때 호출)
        /// </summary>
        /// <param name="timeoutMs">응답 대기 시간 (ms)</param>
        /// <returns>현재 로봇 TCP 포즈</returns>
        Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000);

        /// <summary>
        /// 로봇에 커맨드 전송 (포즈 요청 등)
        /// </summary>
        /// <param name="command">커맨드 문자열</param>
        /// <param name="timeoutMs">응답 대기 시간 (ms)</param>
        /// <returns>응답 문자열</returns>
        Task<string?> SendCommandAsync(string command, int timeoutMs = 3000);

        /// <summary>
        /// 로봇 정지 판정 (Settling Time 대기 후 포즈 안정 확인)
        /// </summary>
        /// <param name="settlingTimeMs">정지 대기 시간 (ms), 기본 200ms</param>
        Task WaitForSettlingAsync(int settlingTimeMs = 200);
    }
}
