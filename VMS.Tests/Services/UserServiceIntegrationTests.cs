using System;
using System.IO;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 임시 디렉토리(별도 SQLite DB)로 격리된 UserService 인스턴스로
    /// Authenticate / Logout / HasPermission / CreateUser / UpdateUser /
    /// ChangePassword / DeleteUser 전체 흐름을 검증.
    ///
    /// 각 테스트는 IDisposable 의 Dispose 에서 임시 DB 파일 정리.
    /// </summary>
    public class UserServiceIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly UserService _service;

        public UserServiceIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_int_{Guid.NewGuid():N}");
            _service = new UserService(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // SQLite 파일 핸들이 남아있을 가능성 — 다른 테스트 영향 없음.
            }
        }

        // ─── 초기화 — seed admin/admin123 ─────────────────────────

        [Fact]
        public void Ctor_CreatesDbFile()
        {
            var dbPath = Path.Combine(_tempDir, "BodaVision.db");
            Assert.True(File.Exists(dbPath));
        }

        [Fact]
        public void Ctor_SeedsDefaultAdmin()
        {
            // 빈 DB 라면 admin/admin123 으로 한 번에 로그인 가능해야 함.
            Assert.True(_service.Authenticate("admin", "admin123"));
            Assert.Equal("admin", _service.CurrentUser!.Username);
            Assert.Equal(UserGrade.Admin, _service.CurrentUser.Grade);
        }

        [Fact]
        public void Ctor_DoesNotReseed_OnExistingDb()
        {
            // 같은 디렉토리로 두 번째 인스턴스를 만들면 기존 DB 재사용 — admin 1명만 있어야 함.
            var second = new UserService(_tempDir);
            second.Authenticate("admin", "admin123");

            Assert.Single(_service.GetAllUsers());
        }

        // ─── Authenticate ─────────────────────────────────────────

        [Fact]
        public void Authenticate_Success_SetsCurrentUser()
        {
            var ok = _service.Authenticate("admin", "admin123");
            Assert.True(ok);
            Assert.True(_service.IsLoggedIn);
            Assert.Equal("admin", _service.CurrentUser!.Username);
        }

        [Fact]
        public void Authenticate_WrongPassword_ReturnsFalse_NoCurrentUser()
        {
            var ok = _service.Authenticate("admin", "wrongpw");
            Assert.False(ok);
            Assert.False(_service.IsLoggedIn);
            Assert.Null(_service.CurrentUser);
        }

        [Fact]
        public void Authenticate_NonExistentUser_ReturnsFalse()
        {
            var ok = _service.Authenticate("ghost", "anything");
            Assert.False(ok);
        }

        [Theory]
        [InlineData("", "pw")]
        [InlineData("user", "")]
        [InlineData(null, "pw")]
        public void Authenticate_InvalidInput_ReturnsFalse(string? username, string password)
        {
            // CredentialGuard 가 입력 검증 — null/빈은 즉시 false.
            var ok = _service.Authenticate(username!, password);
            Assert.False(ok);
        }

        [Fact]
        public void Authenticate_ControlCharsInInput_ReturnsFalse()
        {
            // 개행/탭 등 제어문자는 거부 (로그 위조 / SQL 주입 벡터 차단).
            var ok = _service.Authenticate("admin\n", "admin123");
            Assert.False(ok);
        }

        [Fact]
        public void Authenticate_UpdatesLastLoginAt()
        {
            _service.Authenticate("admin", "admin123");
            var first = _service.CurrentUser!.LastLoginAt;
            _service.Logout();

            System.Threading.Thread.Sleep(5);
            _service.Authenticate("admin", "admin123");
            var second = _service.CurrentUser!.LastLoginAt;

            Assert.True(second >= first);
        }

        // ─── Logout ──────────────────────────────────────────────

        [Fact]
        public void Logout_ClearsCurrentUser()
        {
            _service.Authenticate("admin", "admin123");
            Assert.True(_service.IsLoggedIn);

            _service.Logout();
            Assert.False(_service.IsLoggedIn);
            Assert.Null(_service.CurrentUser);
        }

        [Fact]
        public void Logout_WhenNotLoggedIn_NoThrow()
        {
            // 비로그인 상태에서 logout 호출도 안전해야 함.
            _service.Logout();
            Assert.Null(_service.CurrentUser);
        }

        // ─── HasPermission ────────────────────────────────────────

        [Fact]
        public void HasPermission_NoLogin_AlwaysFalse()
        {
            Assert.False(_service.HasPermission(UserPermission.ViewStatistics));
            Assert.False(_service.HasPermission(UserPermission.ManageUsers));
        }

        [Fact]
        public void HasPermission_Admin_AllGranted()
        {
            _service.Authenticate("admin", "admin123");
            Assert.True(_service.HasPermission(UserPermission.StartStop));
            Assert.True(_service.HasPermission(UserPermission.ManageUsers));
            Assert.True(_service.HasPermission(UserPermission.SystemConfiguration));
        }

        [Fact]
        public void HasPermission_Operator_LimitedSubset()
        {
            _service.CreateUser("op1", "pw1234", "Operator User", UserGrade.Operator);
            _service.Authenticate("op1", "pw1234");

            Assert.True(_service.HasPermission(UserPermission.StartStop));
            Assert.True(_service.HasPermission(UserPermission.ViewStatistics));
            Assert.False(_service.HasPermission(UserPermission.ManageUsers));
            Assert.False(_service.HasPermission(UserPermission.SystemConfiguration));
            Assert.False(_service.HasPermission(UserPermission.EditRecipe));
        }

        [Fact]
        public void HasPermission_Engineer_NoUserManagement()
        {
            _service.CreateUser("eng1", "pw1234", "Engineer User", UserGrade.Engineer);
            _service.Authenticate("eng1", "pw1234");

            Assert.True(_service.HasPermission(UserPermission.EditRecipe));
            Assert.True(_service.HasPermission(UserPermission.CameraSettings));
            Assert.True(_service.HasPermission(UserPermission.SystemConfiguration));
            Assert.False(_service.HasPermission(UserPermission.ManageUsers));
        }

        // ─── CreateUser ──────────────────────────────────────────

        [Fact]
        public void CreateUser_Valid_SavesToDb()
        {
            var ok = _service.CreateUser("user1", "pw1234", "User One", UserGrade.Operator);
            Assert.True(ok);

            // 새로 만든 사용자로 로그인 가능.
            Assert.True(_service.Authenticate("user1", "pw1234"));
        }

        [Fact]
        public void CreateUser_DuplicateUsername_ReturnsFalse()
        {
            _service.CreateUser("dup", "pw1234", "First", UserGrade.Operator);
            var second = _service.CreateUser("dup", "pw1234", "Second", UserGrade.Engineer);
            Assert.False(second);  // SQLite UNIQUE 위반 → catch → false
        }

        [Theory]
        [InlineData("user1", "pw")]   // 너무 짧음 (min 4)
        [InlineData("", "pw1234")]
        [InlineData("user1", "")]
        public void CreateUser_InvalidInput_ReturnsFalse(string username, string password)
        {
            var ok = _service.CreateUser(username, password, "Display", UserGrade.Operator);
            Assert.False(ok);
        }

        [Fact]
        public void CreateUser_StoresBcryptHash_NotPlainText()
        {
            _service.CreateUser("hashtest", "secret123", "Hash Test", UserGrade.Operator);

            // DB 직접 쿼리 — 파일 락 회피 + 정확한 해시 컬럼 확인.
            var dbPath = Path.Combine(_tempDir, "BodaVision.db");
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PasswordHash FROM Users WHERE Username = @u";
            cmd.Parameters.AddWithValue("@u", "hashtest");
            var hash = (string)cmd.ExecuteScalar()!;

            // BCrypt 해시는 $2 로 시작, 평문 secret123 과 동일하지 않음.
            Assert.NotEqual("secret123", hash);
            Assert.StartsWith("$2", hash);
            // BCrypt.Verify 가 통과해야 — 즉 실제 BCrypt 해시.
            Assert.True(BCrypt.Net.BCrypt.Verify("secret123", hash));
        }

        // ─── UpdateUser ──────────────────────────────────────────

        [Fact]
        public void UpdateUser_ChangesDisplayNameAndGrade()
        {
            _service.CreateUser("changeme", "pw1234", "Old Name", UserGrade.Operator);
            _service.Authenticate("changeme", "pw1234");
            var userId = _service.CurrentUser!.UserId;

            var ok = _service.UpdateUser(userId, "New Name", UserGrade.Engineer);
            Assert.True(ok);

            // 새 인증으로 확인.
            _service.Logout();
            _service.Authenticate("changeme", "pw1234");
            Assert.Equal("New Name", _service.CurrentUser!.DisplayName);
            Assert.Equal(UserGrade.Engineer, _service.CurrentUser.Grade);
        }

        [Fact]
        public void UpdateUser_NonExistent_ReturnsFalse()
        {
            var ok = _service.UpdateUser(999_999, "X", UserGrade.Operator);
            Assert.False(ok);
        }

        // ─── ChangePassword ──────────────────────────────────────

        [Fact]
        public void ChangePassword_AllowsLoginWithNew_NotOld()
        {
            _service.CreateUser("pwchange", "old1234", "PW Change", UserGrade.Operator);
            _service.Authenticate("pwchange", "old1234");
            var userId = _service.CurrentUser!.UserId;

            var ok = _service.ChangePassword(userId, "new5678");
            Assert.True(ok);

            _service.Logout();
            Assert.False(_service.Authenticate("pwchange", "old1234"));
            Assert.True(_service.Authenticate("pwchange", "new5678"));
        }

        // ─── DeleteUser ──────────────────────────────────────────

        [Fact]
        public void DeleteUser_RemovesFromDb()
        {
            _service.CreateUser("todelete", "pw1234", "Delete Me", UserGrade.Operator);
            var beforeCount = _service.GetAllUsers().Count;

            _service.Authenticate("todelete", "pw1234");
            var userId = _service.CurrentUser!.UserId;
            _service.Logout();

            var ok = _service.DeleteUser(userId);
            Assert.True(ok);

            Assert.Equal(beforeCount - 1, _service.GetAllUsers().Count);
            Assert.False(_service.Authenticate("todelete", "pw1234"));
        }

        // ─── GetAllUsers ────────────────────────────────────────

        [Fact]
        public void GetAllUsers_ReturnsAllSeeded_AndCreated()
        {
            // 시드 admin 1명 + 추가 2명.
            _service.CreateUser("u1", "pw1234", "U1", UserGrade.Operator);
            _service.CreateUser("u2", "pw1234", "U2", UserGrade.Engineer);

            var users = _service.GetAllUsers();
            Assert.Equal(3, users.Count);
            Assert.Contains(users, u => u.Username == "admin");
            Assert.Contains(users, u => u.Username == "u1");
            Assert.Contains(users, u => u.Username == "u2");
        }
    }
}
