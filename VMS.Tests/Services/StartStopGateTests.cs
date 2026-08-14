using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VMS.Interfaces;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// AUTO RUN·Grab·Live 의 시스템 사용자 권한 규칙 고정 (사용자 매뉴얼 §3.5).
    ///
    /// 과거 <c>_userService?.HasPermission(...) ?? true</c> 는 "서비스 미주입" 만 커버해
    /// 운영 빌드에서는 시스템 사용자 로그인이 사실상 필수였고, 매뉴얼("미로그인 시 기본 허용")·
    /// 버튼 툴팁과 어긋나 있었다. 그 회귀를 막기 위한 테스트다.
    /// </summary>
    public class StartStopGateTests
    {
        [Fact]
        public void NoUserService_Allows()
        {
            // standalone 구성 — 기존 동작 유지
            Assert.True(StartStopGate.Allows(null));
        }

        [Fact]
        public void NotLoggedIn_Allows()
        {
            // 핵심: 미로그인은 차단이 아니라 허용. 생산 신원은 작업자(사번+PIN) 로그인이 담당한다.
            var svc = new FakeUserService { IsLoggedIn = false, StartStopGranted = false };
            Assert.True(StartStopGate.Allows(svc));
        }

        [Fact]
        public void LoggedInWithPermission_Allows()
        {
            var svc = new FakeUserService { IsLoggedIn = true, StartStopGranted = true };
            Assert.True(StartStopGate.Allows(svc));
        }

        [Fact]
        public void LoggedInWithoutPermission_Denies()
        {
            // 로그인한 경우에만 등급 권한이 적용된다.
            var svc = new FakeUserService { IsLoggedIn = true, StartStopGranted = false };
            Assert.False(StartStopGate.Allows(svc));
        }

        [Fact]
        public void NotLoggedIn_DoesNotConsultPermissionMatrix()
        {
            // 미로그인 경로에서 HasPermission 을 호출하면 감사 로그에 PermissionDenied 가
            // 쌓일 수 있으므로 아예 묻지 않아야 한다.
            var svc = new FakeUserService { IsLoggedIn = false, StartStopGranted = false };
            StartStopGate.Allows(svc);
            Assert.Equal(0, svc.HasPermissionCallCount);
        }

        private sealed class FakeUserService : IUserService
        {
            public bool IsLoggedIn { get; set; }
            public bool StartStopGranted { get; set; }
            public int HasPermissionCallCount { get; private set; }

            public User? CurrentUser => IsLoggedIn ? new User { Username = "tester" } : null;

            public bool HasPermission(UserPermission permission)
            {
                HasPermissionCallCount++;
                return permission == UserPermission.StartStop && StartStopGranted;
            }

            // ── 이하 테스트에서 사용하지 않는 멤버 ──
            public bool Authenticate(string username, string password) => false;

            public Task<bool> AuthenticateViaWebAsync(string username, string password,
                VMS.Core.Services.WebAuthClient client, CancellationToken ct = default)
                => Task.FromResult(false);

            public void Logout() { }
            public bool SeedInitialAdmin(string password, string displayName = "Administrator") => false;
            public bool SetLocalFallbackPassword(string newPassword) => false;
            public bool CreateUser(string username, string password, string displayName, UserGrade grade) => false;
            public bool UpdateUser(int userId, string displayName, UserGrade grade) => false;
            public bool ChangePassword(int userId, string newPassword) => false;
            public bool DeleteUser(int userId) => false;
            public List<User> GetAllUsers() => new();
        }
    }
}
