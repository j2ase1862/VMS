using System;
using VMS.Core.Security;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// WebAuthClient 의 InsecureUrlGuard 적용 검증 — SSO 로그인은 자격증명이
    /// 평문으로 실리는 유일한 VMS→Web 경로라 다른 연동 서비스와 동일한 보안
    /// 정책을 따라야 한다. SecurityOptions.Current 정적 상태를 변형하므로
    /// "SecurityOptionsState" Collection 으로 직렬화 (WebAuthClientTests 본체는
    /// loopback URL 만 사용해 모드 무관 — 컬렉션 불필요).
    /// </summary>
    [Collection("SecurityOptionsState")]
    public class WebAuthClientGuardTests
    {
        [Theory]
        [InlineData("http://192.168.0.10:5292")]
        [InlineData("http://boda-vms.com")]
        public void Ctor_RemoteHttp_Production_Throws(string url)
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                var ex = Assert.Throws<InvalidOperationException>(() => new WebAuthClient(url));
                Assert.Contains(nameof(WebAuthClient), ex.Message);
            }
            finally { SecurityOptions.Current = original; }
        }

        [Theory]
        [InlineData("http://localhost:5292")]
        [InlineData("http://127.0.0.1:5292")]
        public void Ctor_LoopbackHttp_Production_NoThrow(string url)
        {
            var original = SecurityOptions.Current;
            try
            {
                // 오프라인 단일 PC 구성 (Kestrel HTTP 동거) — loopback 예외로 허용.
                SecurityOptions.Current = SecurityOptions.Production;
                using var client = new WebAuthClient(url);
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Ctor_RemoteHttp_Development_NoThrow()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Development;
                using var client = new WebAuthClient("http://192.168.0.10:5292");
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Ctor_Https_Production_NoThrow()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                using var client = new WebAuthClient("https://boda-vms.com");
            }
            finally { SecurityOptions.Current = original; }
        }
    }
}
