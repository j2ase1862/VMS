using System.Net.Sockets;
using System.Text;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// TCP/IP 소켓 기반 로봇 통신 서비스
    /// 로봇 컨트롤러에서 CSV 형식의 포즈 데이터를 수신
    /// </summary>
    public class TcpRobotService : IRobotService
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly object _lock = new();
        private bool _disposed;

        public bool IsConnected => _client?.Connected == true;

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

        public async Task<bool> ConnectAsync(string ipAddress, int port)
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
            var response = await SendCommandAsync(PoseRequestCommand, timeoutMs);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            try
            {
                return RobotPose.Parse(response, Convention);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<string?> SendCommandAsync(string command, int timeoutMs = 3000)
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
                var buffer = new byte[ReceiveBufferSize];
                var sb = new StringBuilder();

                while (true)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
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
            catch (Exception)
            {
                return null;
            }
        }

        public async Task WaitForSettlingAsync(int settlingTimeMs = 200)
        {
            await Task.Delay(settlingTimeMs);
        }

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
