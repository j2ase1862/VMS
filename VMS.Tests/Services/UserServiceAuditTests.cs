using System;
using System.IO;
using System.Linq;
using VMS.Core.Security;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 사용자 등급 변경 · 비밀번호 변경 · 삭제가 감사 로그에 남는지 (2026-11-13 영상 녹화 중 발견 —
    /// 추가만 기록되고 나머지는 흔적이 없었다).
    /// </summary>
    public class UserServiceAuditTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly UserService _service;
        private readonly string _target = $"audit{Guid.NewGuid():N}"[..20];

        public UserServiceAuditTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_audit_{Guid.NewGuid():N}");
            _service = new UserService(_tempDir);
            _service.SeedInitialAdmin("admin123", "Administrator");
            Assert.True(_service.Authenticate("admin", "admin123"));
            Assert.True(_service.CreateUser(_target, "pass1234", "Audit Target", UserGrade.Operator));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* SQLite 핸들 잔존 — 무시 */ }
        }

        private int TargetId => _service.GetAllUsers().Single(u => u.Username == _target).UserId;

        /// <summary>오늘 감사 로그에서 이 테스트 대상 사용자 줄만.</summary>
        private string[] AuditLinesForTarget(string action)
        {
            var path = Path.Combine(AuditLogger.Instance.AuditDirectory, $"{DateTime.Now:yyyy-MM-dd}.jsonl");
            if (!File.Exists(path)) return Array.Empty<string>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd()
                .Split('\n')
                .Where(l => l.Contains(_target) && l.Contains($"\"{action}\""))
                .ToArray();
        }

        [Fact]
        public void UpdateUser_WritesAuditWithGradeChange()
        {
            Assert.True(_service.UpdateUser(TargetId, "Audit Target", UserGrade.Engineer));

            var line = Assert.Single(AuditLinesForTarget("UpdateUser"));
            Assert.Contains("Operator", line);
            Assert.Contains("Engineer", line);
            Assert.Contains("\"admin\"", line);  // 누가 바꿨는지
        }

        [Fact]
        public void ChangePassword_WritesAuditWithoutSecret()
        {
            Assert.True(_service.ChangePassword(TargetId, "newSecret987"));

            var line = Assert.Single(AuditLinesForTarget("ChangePassword"));
            Assert.DoesNotContain("newSecret987", line);
            Assert.DoesNotContain("$2", line);  // BCrypt 해시 접두어
        }

        [Fact]
        public void DeleteUser_WritesAudit()
        {
            Assert.True(_service.DeleteUser(TargetId));

            Assert.Single(AuditLinesForTarget("DeleteUser"));
        }
    }
}
