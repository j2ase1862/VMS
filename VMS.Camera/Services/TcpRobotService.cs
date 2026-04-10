using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// TCP/IP 소켓 기반 로봇 통신 서비스
    /// 로봇 컨트롤러에서 CSV 형식의 포즈 데이터를 수신
    /// 제조사별 서비스가 상속하여 프로토콜을 커스터마이즈할 수 있음
    /// </summary>
    public class TcpRobotService : IRobotService
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly object _lock = new();
        private bool _disposed;

        /// <summary>하위 클래스에서 소켓 스트림에 직접 접근</summary>
        protected NetworkStream? Stream => _stream;

        /// <summary>하위 클래스에서 TcpClient에 직접 접근</summary>
        protected TcpClient? Client => _client;

        public virtual bool IsConnected => _client?.Connected == true;

        public EulerConvention Convention { get; set; } = EulerConvention.UR_RotationVector;

        /// <summary>
        /// 포즈 요청 시 로봇에 보낼 커맨드 (로봇 프로그램에서 정의)
        /// 기본값: "GET_POSE\n"
        /// </summary>
        public string PoseRequestCommand { get; set; } = "GET_POSE";

        /// <summary>
        /// 수신 데이터 종료 구분자
        /// </summary>
        public string Delimiter { get; set; } = "\n";

        /// <summary>
        /// 수신 버퍼 크기 (bytes)
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 1024;

        public virtual async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            try
            {
                await DisconnectAsync();

                _client = new TcpClient
                {
                    ReceiveTimeout = 5000,
                    SendTimeout = 5000,
                    NoDelay = true
                };

                await _client.ConnectAsync(ipAddress, port);
                _stream = _client.GetStream();

                // 연결 후 핸드셰이크 (하위 클래스에서 오버라이드)
                await OnConnectedAsync();

                return true;
            }
            catch (Exception)
            {
                _client?.Dispose();
                _client = null;
                _stream = null;
                return false;
            }
        }

        /// <summary>
        /// 연결 직후 호출되는 핸드셰이크 훅 (하위 클래스에서 오버라이드 가능)
        /// 예: 초기 패킷 수신, 버전 확인, 인증 등
        /// </summary>
        protected virtual Task OnConnectedAsync() => Task.CompletedTask;

        public virtual async Task DisconnectAsync()
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

        public virtual async Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            var response = await SendCommandAsync(PoseRequestCommand, timeoutMs);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            try
            {
                return ParsePoseResponse(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TcpRobotService] Pose parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 응답 문자열을 RobotPose로 파싱 (하위 클래스에서 프로토콜별 파서 구현)
        /// 기본 구현은 CSV 형태: "x, y, z, rx, ry, rz"
        /// </summary>
        protected virtual RobotPose ParsePoseResponse(string response)
        {
            return RobotPose.Parse(response, Convention);
        }

        public virtual async Task<string?> SendCommandAsync(string command, int timeoutMs = 3000)
        {
            if (_stream == null || !IsConnected)
                return null;

            try
            {
                // 커맨드 전송
                var sendData = Encoding.UTF8.GetBytes(command + Delimiter);
                using var cts = new CancellationTokenSource(timeoutMs);

                await _stream.WriteAsync(sendData, cts.Token);
                await _stream.FlushAsync(cts.Token);

                // 응답 수신
                return await ReceiveResponseAsync(cts.Token);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 응답 수신 로직 (하위 클래스에서 오버라이드하여 프로토콜별 수신 방식 구현)
        /// 기본 구현은 Delimiter까지 텍스트를 읽음
        /// </summary>
        protected virtual async Task<string?> ReceiveResponseAsync(CancellationToken ct)
        {
            if (_stream == null) return null;

            var buffer = new byte[ReceiveBufferSize];
            var sb = new StringBuilder();

            while (true)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0)
                    break;

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                sb.Append(chunk);

                // 종료 구분자 확인
                if (chunk.Contains(Delimiter))
                    break;
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// 바이너리 데이터 수신 (UR Real-Time 인터페이스 등에서 사용)
        /// </summary>
        protected async Task<byte[]?> ReceiveBytesAsync(int expectedBytes, CancellationToken ct)
        {
            if (_stream == null) return null;

            var buffer = new byte[expectedBytes];
            int totalRead = 0;

            while (totalRead < expectedBytes)
            {
                int bytesRead = await _stream.ReadAsync(
                    buffer, totalRead, expectedBytes - totalRead, ct);
                if (bytesRead == 0) return null;
                totalRead += bytesRead;
            }

            return buffer;
        }

        public virtual async Task WaitForSettlingAsync(int settlingTimeMs = 200)
        {
            await Task.Delay(settlingTimeMs);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            OnDisposing();

            _stream?.Close();
            _stream?.Dispose();
            _client?.Close();
            _client?.Dispose();
        }

        /// <summary>Dispose 시 하위 클래스 리소스 정리 훅</summary>
        protected virtual void OnDisposing() { }
    }
}
