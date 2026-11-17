using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private readonly RecordingAuditLogger _audit = new();
        private readonly string _target = $"audit{Guid.NewGuid():N}"[..20];

        public UserServiceAuditTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_audit_{Guid.NewGuid():N}");
            // 감사 기록은 메모리로 받는다 — 실제 %LocalAppData% 파일은 다른 테스트 프로세스와 함께 써서
            // 공유 위반으로 줄이 빠질 수 있었다(2026-11-17 CI 간헐 실패).
            _service = new UserService(_tempDir, _audit);
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

        /// <summary>받은 감사 기록 중 이 테스트 대상 사용자 줄만 (파일 한 줄과 같은 JSON).</summary>
        private string[] AuditLinesForTarget(string action) =>
            _audit.Lines
                .Where(l => l.Contains(_target) && l.Contains($"\"{action}\""))
                .ToArray();

        /// <summary>감사 기록을 파일 대신 메모리에 JSON 한 줄씩 모은다.</summary>
        private sealed class RecordingAuditLogger : IAuditLogger
        {
            private readonly List<string> _lines = new();
            public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToArray(); } }

            public void Log(AuditCategory category, string action, AuditOutcome outcome = AuditOutcome.Success,
                string? userName = null, string? source = null, string? details = null)
            {
                var json = JsonSerializer.Serialize(new { category = category.ToString(), action, outcome = outcome.ToString(), userName, source, details });
                lock (_lines) _lines.Add(json);
            }
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
