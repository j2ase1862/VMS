using System;
using System.IO;
using System.Linq;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// UserService.SeedInitialAdmin 검증 (Option C, 2026-06-04).
    /// 디폴트 admin/admin123 시드 제거 후 AppSetup wizard 가 호출하는 명시 시드 API.
    /// </summary>
    public class UserServiceSeedInitialAdminTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly UserService _service;

        public UserServiceSeedInitialAdminTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_seed_{Guid.NewGuid():N}");
            _service = new UserService(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void SeedInitialAdmin_creates_admin_when_none_exists()
        {
            var ok = _service.SeedInitialAdmin("strong-pw-1234");

            Assert.True(ok);
            Assert.True(_service.Authenticate("admin", "strong-pw-1234"));
            Assert.Equal(UserGrade.Admin, _service.CurrentUser!.Grade);
        }

        [Fact]
        public void SeedInitialAdmin_returns_false_when_admin_already_exists()
        {
            _service.SeedInitialAdmin("first-password-1");
            var second = _service.SeedInitialAdmin("second-password-2");

            Assert.False(second);
            // 첫 비밀번호 그대로 유지 (덮어쓰기 안 함)
            Assert.True(_service.Authenticate("admin", "first-password-1"));
            Assert.False(_service.Authenticate("admin", "second-password-2"));
        }

        [Fact]
        public void SeedInitialAdmin_uses_displayName_argument()
        {
            _service.SeedInitialAdmin("strong-pw-1234", "Plant Manager");

            _service.Authenticate("admin", "strong-pw-1234");
            Assert.Equal("Plant Manager", _service.CurrentUser!.DisplayName);
        }

        [Fact]
        public void SeedInitialAdmin_empty_password_rejected()
        {
            Assert.False(_service.SeedInitialAdmin(""));
            Assert.False(_service.SeedInitialAdmin("   "));

            // admin 안 만들어짐
            var users = _service.GetAllUsers();
            Assert.DoesNotContain(users, u => u.Username == "admin");
        }

        [Theory]
        [InlineData("1234")]    // 4자
        [InlineData("1234567")] // 7자
        public void SeedInitialAdmin_too_short_password_rejected(string password)
        {
            Assert.False(_service.SeedInitialAdmin(password));
        }

        [Fact]
        public void SeedInitialAdmin_minimum_8_chars_accepted()
        {
            Assert.True(_service.SeedInitialAdmin("12345678"));
        }

        [Fact]
        public void SeedInitialAdmin_blank_displayName_uses_default()
        {
            _service.SeedInitialAdmin("strong-pw-1234", "   ");
            _service.Authenticate("admin", "strong-pw-1234");

            Assert.Equal("Administrator", _service.CurrentUser!.DisplayName);
        }

        [Fact]
        public void Fresh_install_has_only_local_admin_until_SeedInitialAdmin_called()
        {
            // Option C 의 핵심 동작 — InitializeDatabase 단독으로는 admin 시드 X
            var users = _service.GetAllUsers();

            Assert.Single(users);
            Assert.Equal(UserService.LocalFallbackUsername, users[0].Username);

            // SeedInitialAdmin 후 admin 추가
            _service.SeedInitialAdmin("strong-pw-1234");
            users = _service.GetAllUsers();
            Assert.Equal(2, users.Count);
            Assert.Contains(users, u => u.Username == "admin");
        }
    }
}
