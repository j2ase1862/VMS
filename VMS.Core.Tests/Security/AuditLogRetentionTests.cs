using System;
using System.IO;
using System.Linq;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    /// <summary>
    /// AuditLogRetention 의 CleanupOldFiles / Clamp / LoadRetentionDays 동작을
    /// 임시 디렉토리로 격리하여 검증.
    /// </summary>
    public class AuditLogRetentionTests : IDisposable
    {
        private readonly string _tempDir;

        public AuditLogRetentionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"audit_retention_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* SQLite 이외 일반 파일 — 정리 실패 무시 */ }
        }

        private string CreateJsonlFor(DateTime date)
        {
            var path = Path.Combine(_tempDir, $"{date:yyyy-MM-dd}.jsonl");
            File.WriteAllText(path, "{\"timestamp\":\"" + date.ToString("o") + "\"}\n");
            return path;
        }

        // ─── CleanupOldFiles — 보존 기간 경계 ─────────────────────

        [Fact]
        public void CleanupOldFiles_FileOlderThanRetention_Deleted()
        {
            var old = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-400));
            var deleted = AuditLogRetention.CleanupOldFiles(_tempDir, 365);

            Assert.True(deleted >= 1);
            Assert.False(File.Exists(old));
        }

        [Fact]
        public void CleanupOldFiles_FileWithinRetention_Kept()
        {
            var recent = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-30));
            AuditLogRetention.CleanupOldFiles(_tempDir, 365);

            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void CleanupOldFiles_TodayFile_AlwaysKept()
        {
            // 오늘 파일은 보존 기간 0 이어도 유지 — clamp 이 7 로 강제되긴 하지만,
            // 진행 중인 세션의 로그를 보호하기 위함.
            var today = CreateJsonlFor(DateTime.UtcNow.Date);
            AuditLogRetention.CleanupOldFiles(_tempDir, 7);
            Assert.True(File.Exists(today));
        }

        [Fact]
        public void CleanupOldFiles_FileExactlyAtBoundary_Kept()
        {
            // threshold = today - retentionDays. fileDate < threshold 만 삭제.
            // fileDate == threshold 는 boundary 안쪽으로 간주.
            var boundary = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-365));
            AuditLogRetention.CleanupOldFiles(_tempDir, 365);
            Assert.True(File.Exists(boundary));
        }

        [Fact]
        public void CleanupOldFiles_FilenameNotDateFormat_NeverDeleted()
        {
            // 예상 패턴 외 파일은 절대 건드리지 않음 — 사용자 임의 파일 보호.
            var bogus = Path.Combine(_tempDir, "notes.jsonl");
            File.WriteAllText(bogus, "{}");
            AuditLogRetention.CleanupOldFiles(_tempDir, 7);
            Assert.True(File.Exists(bogus));
        }

        [Fact]
        public void CleanupOldFiles_MixedAges_OnlyOldDeleted()
        {
            var veryOld = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-500));
            var old = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-100));
            var recent = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-5));

            var deleted = AuditLogRetention.CleanupOldFiles(_tempDir, 90);

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(veryOld));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void CleanupOldFiles_EmptyDir_ReturnsZero_NoThrow()
        {
            var deleted = AuditLogRetention.CleanupOldFiles(_tempDir, 365);
            Assert.Equal(0, deleted);
        }

        [Fact]
        public void CleanupOldFiles_NullOrMissingDir_ReturnsZero_NoThrow()
        {
            Assert.Equal(0, AuditLogRetention.CleanupOldFiles(null!, 365));
            Assert.Equal(0, AuditLogRetention.CleanupOldFiles("", 365));
            Assert.Equal(0, AuditLogRetention.CleanupOldFiles(
                Path.Combine(_tempDir, "nonexistent_subdir"), 365));
        }

        // ─── Clamp 동작 ─────────────────────────────────────────

        [Fact]
        public void CleanupOldFiles_RetentionBelowMin_ClampedToMin()
        {
            // retentionDays=0 은 Clamp 으로 7 일로 강제 → 6일 전 파일은 유지.
            var sixDaysOld = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-6));
            AuditLogRetention.CleanupOldFiles(_tempDir, 0);
            Assert.True(File.Exists(sixDaysOld));
        }

        [Fact]
        public void CleanupOldFiles_RetentionAboveMax_ClampedToMax()
        {
            // retentionDays=99999 는 3650 으로 clamp. 10년 이내 파일은 유지.
            var tenYearOld = CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-3000));
            AuditLogRetention.CleanupOldFiles(_tempDir, 99999);
            Assert.True(File.Exists(tenYearOld));
        }

        // ─── 외부 파일 보호 ─────────────────────────────────────

        [Fact]
        public void CleanupOldFiles_DoesNotTouchNonJsonl()
        {
            // .jsonl 확장자 외 파일은 절대 검색 대상이 아님.
            var txt = Path.Combine(_tempDir, "2020-01-01.txt");
            File.WriteAllText(txt, "x");
            AuditLogRetention.CleanupOldFiles(_tempDir, 7);
            Assert.True(File.Exists(txt));
        }

        // ─── LoadRetentionDaysFromAppData ────────────────────────

        [Fact]
        public void LoadRetentionDaysFromAppData_ReturnsValueInRange()
        {
            // 환경에 따라 다르지만 항상 [MinRetentionDays, MaxRetentionDays] 보장.
            var days = AuditLogRetention.LoadRetentionDaysFromAppData();
            Assert.InRange(days,
                AuditLogRetention.MinRetentionDays,
                AuditLogRetention.MaxRetentionDays);
        }

        // ─── 정리 후 디렉토리 상태 ──────────────────────────────

        [Fact]
        public void CleanupOldFiles_RemainingFilesAreWithinRetention()
        {
            // 다양한 나이 파일 생성 후 정리 — 남은 파일들이 모두 [today-365, today] 범위 안.
            CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-500));
            CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-400));
            CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-200));
            CreateJsonlFor(DateTime.UtcNow.Date.AddDays(-50));
            CreateJsonlFor(DateTime.UtcNow.Date);

            AuditLogRetention.CleanupOldFiles(_tempDir, 365);

            var remaining = Directory.EnumerateFiles(_tempDir, "*.jsonl")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(n => DateTime.ParseExact(n, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            var minAllowed = DateTime.UtcNow.Date.AddDays(-365);
            Assert.All(remaining, d => Assert.True(d >= minAllowed,
                $"파일 날짜 {d:yyyy-MM-dd} 가 보존 경계 {minAllowed:yyyy-MM-dd} 보다 오래됨"));
        }
    }
}
