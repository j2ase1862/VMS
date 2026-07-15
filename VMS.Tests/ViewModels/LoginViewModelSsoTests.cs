using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Security;
using VMS.Core.Services;
using VMS.Services;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// LoginViewModel 의 SSO 라우팅 검증 (SSO PR4):
    /// - WebSsoConfig.Enabled=false → 기존 로컬 Authenticate
    /// - WebSsoConfig.Enabled=true + local-admin → 로컬 Authenticate 폴백 (Web 미호출)
    /// - WebSsoConfig.Enabled=true + 일반 사용자 → AuthenticateViaWebAsync
    ///   - Web 거부 / 도달 불가 시 일반 사용자는 폴백 미허용, 안내 메시지
    /// </summary>
    public class LoginViewModelSsoTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly UserService _userService;

        public LoginViewModelSsoTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_lvm_{Guid.NewGuid():N}");
            _userService = new UserService(_tempDir);
            // Option C: admin 명시 시드 (디폴트 시드 제거됨)
            _userService.SeedInitialAdmin("admin123", "Administrator");
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private static WebAuthClient ClientFactory(HttpStatusCode status, string body)
            => new("http://localhost:5292", new HttpClient(new StubHandler(status, body)));

        // ─── SSO 비활성 — 기존 동작 ─────────────────────────────

        [Fact]
        public async Task SSO_disabled_uses_local_Authenticate()
        {
            var vm = new LoginViewModel(_userService,
                WebSsoConfig.Disabled,
                url => throw new InvalidOperationException("Web should not be called"));

            vm.Username = "admin";
            vm.Password = "admin123";
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.True(vm.IsAuthenticated);
            Assert.Equal(string.Empty, vm.ErrorMessage);
        }

        [Fact]
        public async Task SSO_disabled_with_wrong_password_returns_error()
        {
            var vm = new LoginViewModel(_userService,
                WebSsoConfig.Disabled,
                url => throw new InvalidOperationException("Web should not be called"));

            vm.Username = "admin";
            vm.Password = "wrong";
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.False(vm.IsAuthenticated);
            Assert.Contains("올바르지 않습니다", vm.ErrorMessage);
        }

        // ─── SSO 활성 + local-admin 폴백 ────────────────────────

        [Fact]
        public async Task SSO_enabled_local_admin_uses_local_Authenticate_bypassing_Web()
        {
            // Web 호출되면 throw → local-admin 은 Web 미호출 보장
            var ssoOn = new WebSsoConfig { Enabled = true, WebServerUrl = "http://localhost:5292" };
            var vm = new LoginViewModel(_userService, ssoOn,
                url => throw new InvalidOperationException("local-admin must not call Web"));

            vm.Username = UserService.LocalFallbackUsername;
            vm.Password = UserService.LocalFallbackDefaultPassword;
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.True(vm.IsAuthenticated);
            Assert.True(_userService.CurrentUser!.IsLocalFallback);
        }

        [Fact]
        public async Task SSO_enabled_local_admin_uppercase_also_bypasses_Web()
        {
            // username 비교 대소문자 무관
            var ssoOn = new WebSsoConfig { Enabled = true, WebServerUrl = "http://localhost:5292" };
            var vm = new LoginViewModel(_userService, ssoOn,
                url => throw new InvalidOperationException("local-admin must not call Web"));

            vm.Username = "LOCAL-ADMIN";
            vm.Password = UserService.LocalFallbackDefaultPassword;
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.True(vm.IsAuthenticated);
        }

        // ─── SSO 활성 + 일반 사용자 → Web 위임 ──────────────────

        [Fact]
        public async Task SSO_enabled_general_user_routes_to_Web()
        {
            var ssoOn = new WebSsoConfig { Enabled = true, WebServerUrl = "http://localhost:5292" };
            var vm = new LoginViewModel(_userService, ssoOn,
                url => ClientFactory(HttpStatusCode.OK,
                    "{\"token\":\"t\",\"username\":\"alice\",\"displayName\":\"Alice\",\"role\":\"Admin\"}"));

            vm.Username = "alice";
            vm.Password = "secret12";
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.True(vm.IsAuthenticated);
            Assert.False(_userService.CurrentUser!.IsLocalFallback);
        }

        [Fact]
        public async Task SSO_enabled_general_user_web_rejects_returns_no_fallback_message()
        {
            var ssoOn = new WebSsoConfig { Enabled = true, WebServerUrl = "http://localhost:5292" };
            var vm = new LoginViewModel(_userService, ssoOn,
                url => ClientFactory(HttpStatusCode.Unauthorized, ""));

            vm.Username = "alice";
            vm.Password = "wrong";
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.False(vm.IsAuthenticated);
            // 일반 사용자는 로컬 폴백 불가 — 안내 메시지에 local-admin 안내
            Assert.Contains(UserService.LocalFallbackUsername, vm.ErrorMessage);
        }

        [Fact]
        public async Task SSO_enabled_general_user_web_unreachable_returns_no_fallback_message()
        {
            var ssoOn = new WebSsoConfig { Enabled = true, WebServerUrl = "http://localhost:5292" };
            var vm = new LoginViewModel(_userService, ssoOn,
                url => ClientFactory(HttpStatusCode.ServiceUnavailable, ""));

            vm.Username = "alice";
            vm.Password = "x";
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.False(vm.IsAuthenticated);
            Assert.Contains(UserService.LocalFallbackUsername, vm.ErrorMessage);
        }

        [Fact]
        public async Task SSO_enabled_factory_blocked_by_security_policy_shows_error_not_crash()
        {
            // WebAuthClient ctor 의 InsecureUrlGuard 가 Production + 원격 http 를 거부하는
            // 상황 — RelayCommand 로 예외가 새지 않고 안내 메시지로 표시돼야 한다.
            var ssoOn = new WebSsoConfig { Enabled = true, WebServerUrl = "http://192.168.0.10:5292" };
            var vm = new LoginViewModel(_userService, ssoOn,
                url => throw new InvalidOperationException("보안 정책 위반 — Production 모드에서 HTTPS 가 필수입니다."));

            vm.Username = "alice";
            vm.Password = "x";
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.False(vm.IsAuthenticated);
            Assert.Contains("보안 정책", vm.ErrorMessage);
        }

        // ─── 입력 검증 ─────────────────────────────────────────

        [Theory]
        [InlineData("", "pw")]
        [InlineData("  ", "pw")]
        [InlineData("user", "")]
        [InlineData("user", "  ")]
        public async Task Empty_input_rejected_with_message(string username, string password)
        {
            var vm = new LoginViewModel(_userService, WebSsoConfig.Disabled,
                url => throw new InvalidOperationException("should not call Web"));

            vm.Username = username;
            vm.Password = password;
            await vm.LoginCommand.ExecuteAsync(null);

            Assert.False(vm.IsAuthenticated);
            Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        }

        [Fact]
        public void IsWebSsoEnabled_reflects_config()
        {
            var vmOff = new LoginViewModel(_userService, WebSsoConfig.Disabled, _ => null!);
            Assert.False(vmOff.IsWebSsoEnabled);

            var vmOn = new LoginViewModel(_userService,
                new WebSsoConfig { Enabled = true, WebServerUrl = "http://x" }, _ => null!);
            Assert.True(vmOn.IsWebSsoEnabled);
        }

        // ─── helper ─────────────────────────────────────────────

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
    }
}
