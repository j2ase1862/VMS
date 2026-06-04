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
            // Option C: admin 명시 시드 (디폴트 시드 제거됨)
            _service.SeedInitialAdmin("admin123", "Administrator");
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        // ─── seed ───────────────────────────────────────────────

        [Fact]
        public void InitializeDatabase_seeds_local_admin_automatically()
        {
            // Option C (2026-06-04): admin 디폴트 시드 제거. local-admin 만 자동 시드.
            // setUp 이 SeedInitialAdmin 호출했으므로 admin 도 존재.
            var users = _service.GetAllUsers();
            Assert.Contains(users, u => u.Username == "admin");
            Assert.Contains(users, u => u.Username == UserService.LocalFallbackUsername);
        }

        [Fact]
        public void InitializeDatabase_alone_seeds_only_local_admin_no_regular_admin()
        {
            // Fresh ctor (setUp 미실행 — SeedInitialAdmin 호출 안 함) → local-admin 만 존재
            var freshDir = Path.Combine(Path.GetTempPath(), $"users_freshfb_{Guid.NewGuid():N}");
            try
            {
                var fresh = new UserService(freshDir);
                var users = fresh.GetAllUsers();

                Assert.Single(users);
                Assert.Equal(UserService.LocalFallbackUsername, users[0].Username);
            }
            finally
            {
                try { Directory.Delete(freshDir, recursive: true); } catch { /* best effort */ }
            }
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

        // ─── SetLocalFallbackPassword (SSO PR4) ───────────────────

        [Fact]
        public void SetLocalFallbackPassword_updates_hash_and_old_password_no_longer_works()
        {
            var ok = _service.SetLocalFallbackPassword("new-strong-pw-123");
            Assert.True(ok);

            // 옛 디폴트 비밀번호 거부
            Assert.False(_service.Authenticate(
                UserService.LocalFallbackUsername, UserService.LocalFallbackDefaultPassword));
            // 새 비밀번호 통과
            Assert.True(_service.Authenticate(
                UserService.LocalFallbackUsername, "new-strong-pw-123"));
        }

        [Fact]
        public void SetLocalFallbackPassword_empty_value_is_noop()
        {
            Assert.False(_service.SetLocalFallbackPassword(""));
            Assert.False(_service.SetLocalFallbackPassword("   "));

            // 디폴트 그대로 동작
            Assert.True(_service.Authenticate(
                UserService.LocalFallbackUsername, UserService.LocalFallbackDefaultPassword));
        }

        [Fact]
        public void SetLocalFallbackPassword_too_short_rejected()
        {
            // 최소 길이 8 — fallback 디폴트보다 강한 정책
            Assert.False(_service.SetLocalFallbackPassword("1234"));
            Assert.False(_service.SetLocalFallbackPassword("1234567"));
            Assert.True(_service.SetLocalFallbackPassword("12345678"));
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
