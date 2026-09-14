using System;
using System.Linq;
using System.Net.Http;
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
    /// <para><b>왜 소켓으로 확인하지 않는가.</b> 처음에는 루프백 리스너로 실제 요청 헤더를 읽었는데,
    /// 종료 통지는 fire-and-forget + 2초 타임아웃이라 CI 부하에서 요청이 끊겨 간헐 실패했다
    /// (2026-09-14 관측). 같은 리포의 <see cref="ClientApiKeyHeaderTests"/> 처럼 <b>요청을 보내는
    /// 클라이언트</b>를 직접 확인한다 — Dispose 는 이 클라이언트만 쓴다.</para>
    /// </summary>
    public class HeartbeatDisconnectKeyTests
    {
        private const string Url = "https://boda-vms.example";   // https 라 보안 모드와 무관
        private const string Key = "disconnect-key";

        [Fact]
        public void Disconnect_client_carries_the_api_key()
        {
            using var service = new HeartbeatService(Url, Url, clientIndex: 7, clientApiKey: Key);

            using var client = service.CreateDisconnectClient();

            Assert.True(client.DefaultRequestHeaders.TryGetValues("X-API-Key", out var values),
                "종료 통지에 키가 없으면 Required=true 서버에서 401 — 라인이 '접속 중' 으로 남는다");
            Assert.Equal(Key, values!.Single());
        }

        [Fact]
        public void Disconnect_client_sends_no_header_when_no_key_configured()
        {
            using var service = new HeartbeatService(Url, Url, clientIndex: 7, clientApiKey: "");

            using var client = service.CreateDisconnectClient();

            Assert.False(client.DefaultRequestHeaders.Contains("X-API-Key"),
                "키가 없으면 헤더도 없어야 한다 — 서버 호환 모드(Required=false)로 통과");
        }

        [Fact]
        public void Disconnect_client_keeps_the_short_timeout()
        {
            // 종료 경로다 — 오래 붙잡으면 앱이 닫히지 않는다.
            using var service = new HeartbeatService(Url, Url, clientIndex: 7, clientApiKey: Key);

            using var client = service.CreateDisconnectClient();

            Assert.Equal(TimeSpan.FromSeconds(2), client.Timeout);
        }
    }
}
