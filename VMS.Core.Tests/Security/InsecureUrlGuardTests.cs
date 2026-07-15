using System;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// InsecureUrlGuard.Check 는 SecurityOptions.Current 의 정적 상태에 의존.
    /// xUnit 기본 병렬 실행에서 다른 SecurityOptions 변형 테스트와 race 발생 →
    /// "SecurityOptionsState" Collection 으로 묶어 직렬화. HttpClientPolicyTests /
    /// SecurityOptionsTests 도 동일 Collection.
    /// </summary>
    [Collection("SecurityOptionsState")]
    public class InsecureUrlGuardTests
    {
        [Fact]
        public void Check_HttpsUrl_NoThrow_EitherMode()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                InsecureUrlGuard.Check("https://api.example.com", "TestSource");
                SecurityOptions.Current = SecurityOptions.Development;
                InsecureUrlGuard.Check("https://api.example.com", "TestSource");
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Check_HttpUrl_Production_Throws()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    InsecureUrlGuard.Check("http://api.example.com", "TestSource"));
                Assert.Contains("TestSource", ex.Message);
                Assert.Contains("http", ex.Message);
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Check_HttpUrl_Development_NoThrow()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Development;
                // Development 는 경고만 발생 (Debug.WriteLine), 호출자 차단 안 함.
                InsecureUrlGuard.Check("http://localhost:5292", "TestSource");
            }
            finally { SecurityOptions.Current = original; }
        }

        [Theory]
        [InlineData("http://localhost:5292")]
        [InlineData("http://LOCALHOST:5292")]
        [InlineData("http://127.0.0.1:5292")]
        [InlineData("http://127.0.0.1")]
        [InlineData("http://[::1]:5292")]
        public void Check_HttpLoopback_Production_NoThrow(string url)
        {
            var original = SecurityOptions.Current;
            try
            {
                // 오프라인 단일 PC 구성 — 같은 PC 의 Kestrel HTTP 는 트래픽이 밖으로
                // 나가지 않으므로 Production 에서도 허용.
                SecurityOptions.Current = SecurityOptions.Production;
                InsecureUrlGuard.Check(url, "TestSource");
            }
            finally { SecurityOptions.Current = original; }
        }

        [Theory]
        [InlineData("http://192.168.0.10:5292")]  // 사설망도 원격은 원격
        [InlineData("http://boda-vms.com")]
        [InlineData("http://my-local-alias:5292")] // hosts 별칭은 판정 불가 → 비허용
        public void Check_HttpNonLoopback_Production_StillThrows(string url)
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                Assert.Throws<InvalidOperationException>(() =>
                    InsecureUrlGuard.Check(url, "TestSource"));
            }
            finally { SecurityOptions.Current = original; }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Check_NullOrEmpty_NoThrow_EitherMode(string? url)
        {
            var original = SecurityOptions.Current;
            try
            {
                // null / empty URL 은 어느 모드에서도 통과 (정책 위반 아닌 미설정 상태).
                SecurityOptions.Current = SecurityOptions.Production;
                InsecureUrlGuard.Check(url, "TestSource");
                SecurityOptions.Current = SecurityOptions.Development;
                InsecureUrlGuard.Check(url, "TestSource");
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Check_CaseInsensitive_HttpDetection()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                // 대문자 HTTP:// 도 동일하게 거부.
                Assert.Throws<InvalidOperationException>(() =>
                    InsecureUrlGuard.Check("HTTP://Example.COM", "TestSource"));
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Check_ProductionError_IncludesGuidance()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    InsecureUrlGuard.Check("http://api.example.com", "MyService"));
                // 운영자가 봤을 때 무엇을 고쳐야 하는지 알 수 있도록 가이드 포함.
                Assert.Contains("securityMode", ex.Message);
            }
            finally { SecurityOptions.Current = original; }
        }
    }
}
