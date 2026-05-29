using System;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// InsecureUrlGuard.Check 는 SecurityOptions.Current 의 정적 상태에 의존.
    /// 각 테스트에서 try/finally 로 원본 모드 복원 — xUnit 병렬 실행에서도 격리 보장 위해
    /// 같은 Collection 으로 묶어 직렬화하는 옵션은 일단 미사용 (개별 테스트가 짧고
    /// finally 가 보장하므로 race condition 위험 낮음).
    /// </summary>
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
