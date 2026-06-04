using System;
using System.IO;
using System.Linq;
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
    /// 비상 local-admin 폴백 계정 + IsLocalFallback flag + HasPermission 제한 권한 검증 (SSO PR3).
    /// </summary>
    public class UserServiceLocalFallbackTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly UserService _service;

        public UserServiceLocalFallbackTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_fb_{Guid.NewGuid():N}");
            _service = new UserService(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        // ─── seed ───────────────────────────────────────────────

        [Fact]
        public void InitializeDatabase_seeds_local_admin_alongside_admin()
        {
            var users = _service.GetAllUsers();
            Assert.Contains(users, u => u.Username == "admin");
            Assert.Contains(users, u => u.Username == UserService.LocalFallbackUsername);
        }

        [Fact]
        public void InitializeDatabase_local_admin_seed_uses_default_password_for_first_boot()
        {
            // 운영 첫 가동시 운영자가 비밀번호 변경할 수 있도록 알려진 디폴트 사용 — 운영 가이드 §9 명시
            var ok = _service.Authenticate(
                UserService.LocalFallbackUsername, UserService.LocalFallbackDefaultPassword);
            Assert.True(ok);
        }

        [Fact]
        public void InitializeDatabase_does_not_duplicate_local_admin_on_second_run()
        {
            // 같은 dbFolder 로 두 번 ctor → local-admin 한 번만 시드
            var service2 = new UserService(_tempDir);
            var users = service2.GetAllUsers();
            Assert.Single(users, u => u.Username == UserService.LocalFallbackUsername);
        }

        // ─── IsLocalFallback flag ───────────────────────────────

        [Fact]
        public void Authenticate_local_admin_sets_IsLocalFallback_true()
        {
            _service.Authenticate(UserService.LocalFallbackUsername, UserService.LocalFallbackDefaultPassword);

            Assert.NotNull(_service.CurrentUser);
            Assert.True(_service.CurrentUser!.IsLocalFallback);
        }

        [Fact]
        public void Authenticate_local_admin_username_case_insensitive_sets_flag()
        {
            // 컬럼 COLLATE NOCASE — 대소문자 무관 매칭 + flag 식별도 무관
            _service.Authenticate("LOCAL-ADMIN", UserService.LocalFallbackDefaultPassword);

            Assert.NotNull(_service.CurrentUser);
            Assert.True(_service.CurrentUser!.IsLocalFallback);
        }

        [Fact]
        public void Authenticate_regular_admin_does_not_set_IsLocalFallback()
        {
            _service.Authenticate("admin", "admin123");

            Assert.NotNull(_service.CurrentUser);
            Assert.False(_service.CurrentUser!.IsLocalFallback);
        }

        [Fact]
        public async Task AuthenticateViaWebAsync_success_sets_IsLocalFallback_false()
        {
            // Web 인증은 절대 폴백 아님 — 명시적으로 false
            using var client = new WebAuthClient("http://localhost:5292",
                new HttpClient(new StubHandler(HttpStatusCode.OK,
                    "{\"token\":\"t\",\"username\":\"u\",\"displayName\":\"d\",\"role\":\"Admin\"}")));

            await _service.AuthenticateViaWebAsync("u", "p", client);

            Assert.NotNull(_service.CurrentUser);
            Assert.False(_service.CurrentUser!.IsLocalFallback);
        }

        // ─── HasPermission 제한 권한 ────────────────────────────

        [Fact]
        public void HasPermission_local_admin_allows_StartStop_ViewStatistics_RestartWebService()
        {
            _service.Authenticate(UserService.LocalFallbackUsername, UserService.LocalFallbackDefaultPassword);

            Assert.True(_service.HasPermission(UserPermission.StartStop));
            Assert.True(_service.HasPermission(UserPermission.ViewStatistics));
            Assert.True(_service.HasPermission(UserPermission.RestartWebService));
        }

        [Theory]
        [InlineData(UserPermission.ManageUsers)]
        [InlineData(UserPermission.EditRecipe)]
        [InlineData(UserPermission.DeleteRecipe)]
        [InlineData(UserPermission.SystemConfiguration)]
        [InlineData(UserPermission.CameraSettings)]
        [InlineData(UserPermission.LaunchAppSetup)]
        [InlineData(UserPermission.LaunchVisionSetup)]
        public void HasPermission_local_admin_denies_operational_changes(UserPermission denied)
        {
            // SSO Migration §2.3: 비상 폴백은 운영 데이터 변경 불가
            _service.Authenticate(UserService.LocalFallbackUsername, UserService.LocalFallbackDefaultPassword);

            Assert.False(_service.HasPermission(denied),
                $"이유: {denied} 는 local-admin 폴백 세션에서 거부돼야 함 (Web SSO 로그인 후 수행)");
        }

        [Fact]
        public void HasPermission_regular_admin_keeps_full_permissions()
        {
            // 일반 admin 은 폴백 아니므로 기존 권한 매핑 그대로
            _service.Authenticate("admin", "admin123");

            Assert.True(_service.HasPermission(UserPermission.ManageUsers));
            Assert.True(_service.HasPermission(UserPermission.EditRecipe));
            Assert.True(_service.HasPermission(UserPermission.SystemConfiguration));
        }

        [Fact]
        public async Task HasPermission_after_web_sso_admin_keeps_full_permissions()
        {
            // Web 인증 → IsLocalFallback=false → Grade 매핑 적용 (Admin → 모든 권한)
            using var client = new WebAuthClient("http://localhost:5292",
                new HttpClient(new StubHandler(HttpStatusCode.OK,
                    "{\"token\":\"t\",\"username\":\"u\",\"displayName\":\"d\",\"role\":\"Admin\"}")));
            await _service.AuthenticateViaWebAsync("u", "p", client);

            Assert.True(_service.HasPermission(UserPermission.ManageUsers));
            Assert.True(_service.HasPermission(UserPermission.EditRecipe));
        }

        // ─── helper ───────────────────────────────────────────────

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
