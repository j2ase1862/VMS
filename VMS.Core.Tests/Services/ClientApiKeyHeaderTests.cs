using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// VMS→Web 머신·키오스크 클라이언트가 <c>X-API-Key</c> 를 실제로 붙이는지.
    ///
    /// <para><b>왜 이 테스트가 있어야 하는가.</b> Web 은 운영 전환 시
    /// <c>ClientApiKey:Required=true</c> 로 바꾸도록 설계돼 있고, 그 순간 키를 안 붙이는
    /// 클라이언트는 전부 401 을 받는다. 그런데 이 호출부들은 실패를 조용히 삼킨다 —
    /// 작업지시는 빈 목록, Lot 자동채움은 무동작, 작업자 로그인은 "사번 또는 PIN이 올바르지
    /// 않습니다". 현장에서는 "데이터가 없다" 로만 보여 원인 추적이 사실상 불가능하다.
    /// 실제로 이 4종은 생성자에 키 인자 자체가 없었다.</para>
    ///
    /// <para>URL 은 https 라 보안 모드와 무관하다(정적 상태를 건드리지 않는다).</para>
    /// </summary>
    public class ClientApiKeyHeaderTests
    {
        private const string Url = "https://boda-vms.example";
        private const string Key = "test-api-key";

        public static TheoryData<string, Func<string, IDisposable>> Clients => new()
        {
            { nameof(OperatorAuthService),     key => new OperatorAuthService(Url, 1, key) },
            { nameof(WorkOrderClient),         key => new WorkOrderClient(Url, 1, key) },
            { nameof(LotClient),               key => new LotClient(Url, key) },
            { nameof(PredictionPollingService), key => new PredictionPollingService(Url, 1, key) },
        };

        [Theory]
        [MemberData(nameof(Clients))]
        public void Key_is_sent_as_header(string name, Func<string, IDisposable> create)
        {
            using var client = create(Key);
            var header = HeaderOf(client);

            Assert.True(header != null, $"{name} 이(가) X-API-Key 를 붙이지 않는다 — Required=true 전환 시 401");
            Assert.Equal(Key, header);
        }

        /// <summary>키가 비어 있으면 헤더를 붙이지 않는다 — 서버 호환 모드(Required=false)에서 통과.</summary>
        [Theory]
        [MemberData(nameof(Clients))]
        public void No_key_means_no_header(string name, Func<string, IDisposable> create)
        {
            using var client = create("");
            Assert.True(HeaderOf(client) is null, $"{name} 이(가) 빈 키로도 헤더를 붙였다");
        }

        private static string? HeaderOf(IDisposable client)
        {
            var field = client.GetType().GetField("_httpClient",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            var http = (HttpClient?)field!.GetValue(client);
            Assert.NotNull(http);

            return http!.DefaultRequestHeaders.TryGetValues("X-API-Key", out var values)
                ? values.FirstOrDefault()
                : null;
        }
    }
}
