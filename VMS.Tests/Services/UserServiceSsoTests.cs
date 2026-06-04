using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// UserService.AuthenticateViaWebAsync — Web SSO 분기 + AuditLog 정합성 검증 (SSO PR2).
    /// WebAuthClient 에 StubHandler 주입 → 네트워크 없이 모든 결과 분기 커버.
    /// </summary>
    public class UserServiceSsoTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly UserService _service;

        public UserServiceSsoTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_sso_{Guid.NewGuid():N}");
            _service = new UserService(_tempDir);
            // Option C: admin 명시 시드 (디폴트 시드 제거됨)
            _service.SeedInitialAdmin("admin123", "Administrator");
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private static WebAuthClient BuildClient(HttpStatusCode status, string body) =>
            new("http://localhost:5292", new HttpClient(new StubHandler(status, body)));

        // ─── Success ─────────────────────────────────────────────

        [Fact]
        public async Task AuthenticateViaWebAsync_success_sets_CurrentUser_with_mapped_grade()
        {
            using var client = BuildClient(HttpStatusCode.OK,
                "{\"token\":\"jwt-x\",\"username\":\"admin\",\"displayName\":\"Web Admin\",\"role\":\"Admin\"}");

            var ok = await _service.AuthenticateViaWebAsync("admin", "secret12", client);

            Assert.True(ok);
            Assert.NotNull(_service.CurrentUser);
            Assert.Equal("admin", _service.CurrentUser!.Username);
            Assert.Equal("Web Admin", _service.CurrentUser.DisplayName);
            Assert.Equal(UserGrade.Admin, _service.CurrentUser.Grade);
            Assert.Equal(-1, _service.CurrentUser.UserId);  // in-memory 마커
            Assert.Equal(string.Empty, _service.CurrentUser.PasswordHash); // SSO — VMS 비밀번호 보관 X
        }

        [Theory]
        [InlineData("Admin", UserGrade.Admin)]
        [InlineData("Manager", UserGrade.Engineer)]
        [InlineData("User", UserGrade.Engineer)]
        [InlineData("Unknown", UserGrade.Operator)]  // 미지원 role 은 안전 디폴트
        [InlineData("", UserGrade.Operator)]
        public async Task AuthenticateViaWebAsync_maps_web_role_to_grade(string webRole, UserGrade expected)
        {
            using var client = BuildClient(HttpStatusCode.OK,
                $"{{\"token\":\"t\",\"username\":\"u\",\"displayName\":\"d\",\"role\":\"{webRole}\"}}");

            var ok = await _service.AuthenticateViaWebAsync("u", "p", client);

            Assert.True(ok);
            Assert.Equal(expected, _service.CurrentUser!.Grade);
        }

        // ─── InvalidCredentials ─────────────────────────────────

        [Fact]
        public async Task AuthenticateViaWebAsync_401_returns_false_and_no_CurrentUser()
        {
            using var client = BuildClient(HttpStatusCode.Unauthorized, "");

            var ok = await _service.AuthenticateViaWebAsync("admin", "wrong", client);

            Assert.False(ok);
            Assert.Null(_service.CurrentUser);
        }

        // ─── WebUnreachable ────────────────────────────────────

        [Fact]
        public async Task AuthenticateViaWebAsync_5xx_returns_false_caller_can_fallback()
        {
            using var client = BuildClient(HttpStatusCode.ServiceUnavailable, "");

            var ok = await _service.AuthenticateViaWebAsync("admin", "x", client);

            Assert.False(ok);
            Assert.Null(_service.CurrentUser);
            // AuditLog details 에 "Web unreachable" 포함 — 호출자가 폴백 판단 가능
        }

        [Fact]
        public async Task AuthenticateViaWebAsync_network_error_returns_false()
        {
            var http = new HttpClient(new ThrowingHandler(new HttpRequestException("connection refused")));
            using var client = new WebAuthClient("http://localhost:5292", http);

            var ok = await _service.AuthenticateViaWebAsync("admin", "x", client);

            Assert.False(ok);
            Assert.Null(_service.CurrentUser);
        }

        // ─── 입력 검증 ──────────────────────────────────────────

        [Theory]
        [InlineData(null, "pw")]
        [InlineData("", "pw")]
        [InlineData("user", "")]
        [InlineData("user\0null", "pw")]  // control char — CredentialGuard 가 거부
        public async Task AuthenticateViaWebAsync_invalid_input_rejected_without_web_call(string? user, string? pw)
        {
            // ThrowingHandler — Web 호출되면 throw → 호출 안 되어야 함
            var http = new HttpClient(new ThrowingHandler(new InvalidOperationException("should not call Web")));
            using var client = new WebAuthClient("http://localhost:5292", http);

            var ok = await _service.AuthenticateViaWebAsync(user!, pw!, client);

            Assert.False(ok);
            Assert.Null(_service.CurrentUser);
        }

        // ─── 기존 로컬 Authenticate 회귀 ─────────────────────────

        [Fact]
        public async Task AuthenticateViaWebAsync_success_does_not_touch_local_users_table()
        {
            // SSO 사용자는 in-memory 만 — DB 의 admin 계정 그대로
            using var client = BuildClient(HttpStatusCode.OK,
                "{\"token\":\"t\",\"username\":\"webuser\",\"displayName\":\"d\",\"role\":\"Admin\"}");

            await _service.AuthenticateViaWebAsync("webuser", "p", client);

            var users = _service.GetAllUsers();
            Assert.DoesNotContain(users, u => u.Username == "webuser");
            // admin seed 는 그대로
            Assert.Contains(users, u => u.Username == "admin");
        }

        [Fact]
        public async Task Logout_after_sso_clears_in_memory_user()
        {
            using var client = BuildClient(HttpStatusCode.OK,
                "{\"token\":\"t\",\"username\":\"u\",\"displayName\":\"d\",\"role\":\"Admin\"}");

            await _service.AuthenticateViaWebAsync("u", "p", client);
            Assert.NotNull(_service.CurrentUser);

            _service.Logout();
            Assert.Null(_service.CurrentUser);
        }

        // ─── null client 거부 ───────────────────────────────────

        [Fact]
        public async Task AuthenticateViaWebAsync_null_client_throws_ArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _service.AuthenticateViaWebAsync("u", "p", null!));
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
    }
}
