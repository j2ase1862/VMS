using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 종료 통지(<c>POST /api/clients/disconnect</c>)도 <c>X-API-Key</c> 를 달고 나가는가 (W-134).
    ///
    /// <para><b>왜.</b> 정상 경로(heartbeat·register)는 키를 붙이는데 Dispose 의 종료 통지만
    /// 새 HttpClient 를 만들면서 키를 빠뜨렸다. 서버가 키 강제로 전환되면 이 요청만 401 이 되고,
    /// 그러면 ① 대시보드에 라인이 한동안 '접속 중' 으로 남고 ② 서버가 하는 작업자 세션 자동
    /// 종료가 실행되지 않아 <b>정상 종료했는데도 다음 검사가 이미 퇴근한 작업자에게 귀속</b>된다.
    /// 화면에 오류가 뜨지 않으므로 아무도 모른 채 추적성 데이터만 오염된다.</para>
    ///
    /// <para>실제 소켓을 열어 요청 헤더를 그대로 읽는다 — 헤더를 붙였는지를 구현이 아니라
    /// 전선 위에서 확인해야 의미가 있다. 루프백 TCP 리스너라 관리자 권한이 필요 없다.</para>
    /// </summary>
    public class HeartbeatDisconnectKeyTests
    {
        private const string Key = "disconnect-key";

        [Fact]
        public async Task Disconnect_carries_the_api_key()
        {
            var request = await CaptureDisconnectRequestAsync(Key);

            Assert.Contains("POST /api/clients/disconnect", request, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("X-API-Key: " + Key, request, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Disconnect_sends_no_header_when_no_key_configured()
        {
            var request = await CaptureDisconnectRequestAsync(apiKey: "");

            Assert.Contains("POST /api/clients/disconnect", request, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("X-API-Key", request, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>루프백 리스너를 띄우고 HeartbeatService 를 Dispose 한 뒤, 도착한 요청 헤더를 돌려준다.</summary>
        private static async Task<string> CaptureDisconnectRequestAsync(string apiKey)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var accepted = AcceptRequestAsync(listener);
            try
            {
                var service = new HeartbeatService(
                    $"http://127.0.0.1:{port}",
                    visionServerUrl: $"http://127.0.0.1:{port}",
                    clientIndex: 7,
                    clientApiKey: apiKey);

                service.Dispose();   // 종료 통지는 fire-and-forget

                var completed = await Task.WhenAny(accepted, Task.Delay(TimeSpan.FromSeconds(30)));
                Assert.True(completed == accepted, "종료 통지 요청이 도착하지 않았다");
                return await accepted;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<string> AcceptRequestAsync(TcpListener listener)
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();

            var buffer = new byte[8192];
            var text = new StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // 헤더 끝(빈 줄)까지만 읽으면 충분하다 — 본문은 clientIndex 하나뿐.
            while (!text.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (read == 0) break;
                text.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }

            var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, 0, response.Length, cts.Token);
            return text.ToString();
        }
    }
}
