using System;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    public class HttpClientPolicyTests
    {
        // HttpClient 의 public 속성(Timeout, UserAgent, MaxResponseContentBufferSize) 만 검증.
        // 내부 HttpClientHandler 의 cert callback 동작 은 .NET 의 API 가 노출하지 않아 단위 테스트
        // 격리가 어려움 — SecurityOptions 분기와 InsecureUrlGuard 의 별도 테스트로 간접 검증.

        [Fact]
        public void Build_DefaultTimeout_IsSet()
        {
            using var client = HttpClientPolicy.Build(TimeSpan.FromSeconds(7), SecurityOptions.Development);
            Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
        }

        [Fact]
        public void Build_DefaultUserAgent_Applied()
        {
            using var client = HttpClientPolicy.Build(TimeSpan.FromSeconds(5), SecurityOptions.Development);
            Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
            // SecurityOptions.UserAgent 기본값이 "BODA-VMS/{version}" 형식.
            var ua = client.DefaultRequestHeaders.UserAgent.ToString();
            Assert.Contains("BODA-VMS", ua);
        }

        [Fact]
        public void Build_DefaultMaxResponseBytes_IsDefault()
        {
            using var client = HttpClientPolicy.Build(TimeSpan.FromSeconds(5), SecurityOptions.Development);
            Assert.Equal((long)HttpClientPolicy.DefaultMaxResponseBytes, client.MaxResponseContentBufferSize);
        }

        [Fact]
        public void Build_MaxResponseBytesOverride_AppliedExactly()
        {
            const long custom = 200L * 1024 * 1024;  // PaddleOcr 모델 다운로드 예시
            using var client = HttpClientPolicy.Build(
                TimeSpan.FromMinutes(5),
                SecurityOptions.Development,
                maxResponseBytes: custom);
            Assert.Equal(custom, client.MaxResponseContentBufferSize);
        }

        [Fact]
        public void Build_ProductionMode_AppliesSamePublicSettings()
        {
            // Production 모드에서도 public 속성(Timeout/UA/MaxBytes) 동작은 동일 — cert 검증 정책만 차이.
            using var client = HttpClientPolicy.Build(TimeSpan.FromSeconds(5), SecurityOptions.Production);
            Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
            Assert.Equal((long)HttpClientPolicy.DefaultMaxResponseBytes, client.MaxResponseContentBufferSize);
            Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
        }

        [Fact]
        public void Build_NullOptions_UsesCurrent()
        {
            // options=null → SecurityOptions.Current 사용. 격리 위해 원본 보존/복원.
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Development;
                using var client = HttpClientPolicy.Build(TimeSpan.FromSeconds(3));
                Assert.Equal(TimeSpan.FromSeconds(3), client.Timeout);
                Assert.NotEmpty(client.DefaultRequestHeaders.UserAgent);
            }
            finally
            {
                SecurityOptions.Current = original;
            }
        }

        [Fact]
        public void DefaultMaxResponseBytes_Is10MB()
        {
            // GS 인증/문서에서 명시한 상한 — 회귀 방지.
            Assert.Equal(10L * 1024 * 1024, HttpClientPolicy.DefaultMaxResponseBytes);
        }
    }
}
