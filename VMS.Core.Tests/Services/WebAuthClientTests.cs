using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// WebAuthClient — BODA.VMS.Web /api/auth/login 호출 결과 분류 검증 (SSO Migration PR1).
    /// HttpMessageHandler stub 으로 네트워크 없이 모든 경로 커버.
    /// </summary>
    public class WebAuthClientTests
    {
        private const string WebUrl = "http://localhost:5292";

        [Fact]
        public async Task LoginAsync_returns_Success_when_Web_returns_200_with_token()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK,
                "{\"token\":\"jwt-abc\",\"username\":\"admin\",\"displayName\":\"Administrator\",\"role\":\"Admin\"}"));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "admin123");

            Assert.Equal(WebAuthResultKind.Success, result.Kind);
            Assert.True(result.IsSuccess);
            Assert.Equal("jwt-abc", result.Token);
            Assert.Equal("admin", result.Username);
            Assert.Equal("Administrator", result.DisplayName);
            Assert.Equal("Admin", result.Role);
        }

        [Fact]
        public async Task LoginAsync_returns_InvalidCredentials_on_401()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.Unauthorized, ""));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "wrong");

            Assert.Equal(WebAuthResultKind.InvalidCredentials, result.Kind);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Token);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public async Task LoginAsync_returns_WebUnreachable_on_5xx(HttpStatusCode status)
        {
            using var http = new HttpClient(new StubHandler(status, ""));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "x");

            Assert.Equal(WebAuthResultKind.WebUnreachable, result.Kind);
        }

        [Fact]
        public async Task LoginAsync_returns_WebUnreachable_on_network_error()
        {
            using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("connection refused")));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "x");

            Assert.Equal(WebAuthResultKind.WebUnreachable, result.Kind);
            Assert.Contains("connection refused", result.ErrorDetail);
        }

        [Fact]
        public async Task LoginAsync_returns_WebUnreachable_on_timeout()
        {
            // HttpClient 가 Timeout 도달 시 TaskCanceledException throw — Unreachable 로 분류
            using var http = new HttpClient(new ThrowingHandler(new TaskCanceledException("timeout")));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "x");

            Assert.Equal(WebAuthResultKind.WebUnreachable, result.Kind);
            Assert.Contains("timeout", result.ErrorDetail);
        }

        [Fact]
        public async Task LoginAsync_returns_ServerError_on_empty_token()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK,
                "{\"token\":\"\",\"username\":\"admin\",\"displayName\":\"\",\"role\":\"Admin\"}"));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "x");

            Assert.Equal(WebAuthResultKind.ServerError, result.Kind);
            Assert.Contains("empty token", result.ErrorDetail);
        }

        [Fact]
        public async Task LoginAsync_returns_ServerError_on_malformed_json()
        {
            using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{ not json"));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "x");

            Assert.Equal(WebAuthResultKind.ServerError, result.Kind);
        }

        [Fact]
        public async Task LoginAsync_returns_ServerError_on_unexpected_4xx()
        {
            // 400 / 404 등 401 외 4xx 는 ServerError (운영 진단 메시지로 분류)
            using var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound, ""));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync("admin", "x");

            Assert.Equal(WebAuthResultKind.ServerError, result.Kind);
        }

        [Theory]
        [InlineData(null, "pw")]
        [InlineData("", "pw")]
        [InlineData("   ", "pw")]
        [InlineData("user", null)]
        [InlineData("user", "")]
        public async Task LoginAsync_returns_InvalidCredentials_for_empty_input(string? user, string? pw)
        {
            // 빈 입력은 Web 호출 없이 즉시 Invalid — DoS 방어 + UI 메시지 일관
            using var http = new HttpClient(new ThrowingHandler(new InvalidOperationException("should not call Web")));
            using var client = new WebAuthClient(WebUrl, http);

            var result = await client.LoginAsync(user!, pw!);

            Assert.Equal(WebAuthResultKind.InvalidCredentials, result.Kind);
        }

        [Fact]
        public void Ctor_throws_on_empty_url()
        {
            Assert.Throws<ArgumentException>(() => new WebAuthClient(""));
            Assert.Throws<ArgumentException>(() => new WebAuthClient("   "));
        }

        [Fact]
        public async Task LoginAsync_calls_correct_url_path()
        {
            // /api/auth/login 으로 정확히 호출 — base URL 끝에 / 있어도 중복 안 됨
            var captured = new StringBuilder();
            using var http = new HttpClient(new CapturingHandler(captured, HttpStatusCode.OK,
                "{\"token\":\"t\",\"username\":\"u\",\"displayName\":\"d\",\"role\":\"r\"}"));
            using var client = new WebAuthClient("http://localhost:5292/", http);

            await client.LoginAsync("u", "p");

            Assert.Contains("/api/auth/login", captured.ToString());
        }

        // ─── helpers ──────────────────────────────────────────────

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public StubHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            private readonly Exception _ex;
            public ThrowingHandler(Exception ex) { _ex = ex; }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw _ex;
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            private readonly StringBuilder _sink;
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public CapturingHandler(StringBuilder sink, HttpStatusCode status, string body)
            { _sink = sink; _status = status; _body = body; }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _sink.Append(request.RequestUri?.AbsoluteUri ?? "");
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
