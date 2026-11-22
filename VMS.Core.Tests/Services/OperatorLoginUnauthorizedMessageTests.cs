using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 작업자 로그인 401 안내 문구 — 사용자 매뉴얼 4.3 · 10.2 가 이 문구로 원인을 가르게 안내한다
    /// ("서버가 이 PC 의 키를 거부" 면 설정 마법사 고급 설정의 Web Client API Key 확인).
    /// 종전 문구는 화면에 없는 칸 이름 "[Web API 키]" 를 가리켰다 (GS 매뉴얼 대조 02 #3 · 06 #20).
    /// </summary>
    public class OperatorLoginUnauthorizedMessageTests
    {
        private static HttpResponseMessage Unauthorized(bool apiKeyGate)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (apiKeyGate) resp.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("ApiKey"));
            return resp;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Api_key_gate_points_to_the_wizard_field_by_its_screen_name(bool hasApiKey)
        {
            using var resp = Unauthorized(apiKeyGate: true);

            var msg = OperatorAuthService.DescribeUnauthorized(resp, hasApiKey);

            Assert.Contains("서버가 이 PC 의 키를 거부", msg);   // 매뉴얼이 인용하는 글자
            Assert.Contains("Web Client API Key", msg);          // 설정 마법사 칸 이름
            Assert.DoesNotContain("[Web API 키]", msg);
        }

        [Fact]
        public void Wrong_pin_with_key_configured_says_only_that()
        {
            using var resp = Unauthorized(apiKeyGate: false);

            Assert.Equal("사번 또는 PIN이 올바르지 않습니다.", OperatorAuthService.DescribeUnauthorized(resp, hasApiKey: true));
        }

        [Fact]
        public void Wrong_pin_without_key_hints_at_the_same_field_name()
        {
            using var resp = Unauthorized(apiKeyGate: false);

            var msg = OperatorAuthService.DescribeUnauthorized(resp, hasApiKey: false);

            Assert.StartsWith("사번 또는 PIN이 올바르지 않습니다.", msg);
            Assert.Contains("Web Client API Key", msg);
        }
    }
}
