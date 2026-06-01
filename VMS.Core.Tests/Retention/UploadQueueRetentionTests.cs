using System;
using System.IO;
using System.Linq;
using VMS.Core.Retention;
using Xunit;

namespace VMS.Core.Tests.Retention
{
    /// <summary>
    /// UploadQueueRetention 의 보존 경계 / 패턴 매칭 / Clamp / Start-Stop 동작 검증.
    /// 임시 디렉토리로 격리.
    /// </summary>
    public class UploadQueueRetentionTests : IDisposable
    {
        private readonly string _tempDir;

        public UploadQueueRetentionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"queue_retention_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* 무시 */ }
        }

        private string CreateQueueFileFor(DateTime utc)
        {
            // ParameterSyncService.EnqueueFailed 와 동일 패턴.
            var name = $"{utc:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json";
            var path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, "{}");
            return path;
        }

        // ─── 보존 경계 ────────────────────────────────────────────

        [Fact]
        public void CleanupOldFiles_FileOlderThanRetention_Deleted()
        {
            var old = CreateQueueFileFor(DateTime.UtcNow.AddDays(-60));
            var deleted = UploadQueueRetention.CleanupOldFiles(_tempDir, 30);

            Assert.True(deleted >= 1);
            Assert.False(File.Exists(old));
        }

        [Fact]
        public void CleanupOldFiles_FileWithinRetention_Kept()
        {
            var recent = CreateQueueFileFor(DateTime.UtcNow.AddDays(-5));
            UploadQueueRetention.CleanupOldFiles(_tempDir, 30);

            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void CleanupOldFiles_MixedAges_OnlyOldDeleted()
        {
            var veryOld = CreateQueueFileFor(DateTime.UtcNow.AddDays(-90));
            var old = CreateQueueFileFor(DateTime.UtcNow.AddDays(-40));
            var recent = CreateQueueFileFor(DateTime.UtcNow.AddDays(-5));

            var deleted = UploadQueueRetention.CleanupOldFiles(_tempDir, 30);

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(veryOld));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }

        // ─── 패턴 매칭 — 사용자 파일 보호 ─────────────────────────

        [Fact]
        public void CleanupOldFiles_FilenameNoUnderscore_NeverDeleted()
        {
            // 패턴 외 파일 (사용자 임의) — 절대 미터치.
            var bogus = Path.Combine(_tempDir, "myfile.json");
            File.WriteAllText(bogus, "{}");
            UploadQueueRetention.CleanupOldFiles(_tempDir, 1);
            Assert.True(File.Exists(bogus));
        }

        [Fact]
        public void CleanupOldFiles_FilenameWrongTimestampLength_NeverDeleted()
        {
            // underscore 는 있지만 timestamp 길이가 17 이 아님 → skip.
            var bogus = Path.Combine(_tempDir, "20260101_abc.json");
            File.WriteAllText(bogus, "{}");
            UploadQueueRetention.CleanupOldFiles(_tempDir, 1);
            Assert.True(File.Exists(bogus));
        }

        [Fact]
        public void CleanupOldFiles_FilenameMalformedTimestamp_NeverDeleted()
        {
            // 17 자리이지만 parse 안 되는 문자열.
            var bogus = Path.Combine(_tempDir, "notatimestampxxx_abc.json");
            File.WriteAllText(bogus, "{}");
            UploadQueueRetention.CleanupOldFiles(_tempDir, 1);
            Assert.True(File.Exists(bogus));
        }

        [Fact]
        public void CleanupOldFiles_NonJsonExtension_NeverDeleted()
        {
            // 확장자 검색 자체가 *.json 만 — .txt 등은 검색 안 됨.
            var txt = Path.Combine(_tempDir, "20200101000000000_abc.txt");
            File.WriteAllText(txt, "{}");
            UploadQueueRetention.CleanupOldFiles(_tempDir, 1);
            Assert.True(File.Exists(txt));
        }

        // ─── Clamp ────────────────────────────────────────────────

        [Fact]
        public void CleanupOldFiles_RetentionBelowMin_ClampedToMin()
        {
            // retentionDays=0 → Clamp(1). 즉 1일 보존, 23시간 전 파일은 유지.
            var twentyThreeHrs = CreateQueueFileFor(DateTime.UtcNow.AddHours(-23));
            UploadQueueRetention.CleanupOldFiles(_tempDir, 0);
            Assert.True(File.Exists(twentyThreeHrs));
        }

        [Fact]
        public void CleanupOldFiles_RetentionAboveMax_ClampedToMax()
        {
            // 99999 → Clamp(365). 1년 이내 파일 유지.
            var oneYrAgo = CreateQueueFileFor(DateTime.UtcNow.AddDays(-300));
            UploadQueueRetention.CleanupOldFiles(_tempDir, 99999);
            Assert.True(File.Exists(oneYrAgo));
        }

        // ─── Edge cases ───────────────────────────────────────────

        [Fact]
        public void CleanupOldFiles_EmptyDir_ReturnsZero_NoThrow()
        {
            var deleted = UploadQueueRetention.CleanupOldFiles(_tempDir, 30);
            Assert.Equal(0, deleted);
        }

        [Fact]
        public void CleanupOldFiles_NullOrMissingDir_ReturnsZero_NoThrow()
        {
            Assert.Equal(0, UploadQueueRetention.CleanupOldFiles(null!, 30));
            Assert.Equal(0, UploadQueueRetention.CleanupOldFiles("", 30));
            Assert.Equal(0, UploadQueueRetention.CleanupOldFiles(
                Path.Combine(_tempDir, "nonexistent"), 30));
        }

        // ─── LoadRetentionDaysFromAppData ─────────────────────────

        [Fact]
        public void LoadRetentionDaysFromAppData_ReturnsValueInRange()
        {
            var days = UploadQueueRetention.LoadRetentionDaysFromAppData();
            Assert.InRange(days,
                UploadQueueRetention.MinRetentionDays,
                UploadQueueRetention.MaxRetentionDays);
        }

        // ─── 정리 후 남은 파일 ───────────────────────────────────

        [Fact]
        public void CleanupOldFiles_RemainingFilesWithinRetention()
        {
            CreateQueueFileFor(DateTime.UtcNow.AddDays(-90));
            CreateQueueFileFor(DateTime.UtcNow.AddDays(-50));
            CreateQueueFileFor(DateTime.UtcNow.AddDays(-20));
            CreateQueueFileFor(DateTime.UtcNow.AddDays(-1));

            UploadQueueRetention.CleanupOldFiles(_tempDir, 30);

            var remaining = Directory.EnumerateFiles(_tempDir, "*.json").ToList();
            Assert.Equal(2, remaining.Count);  // -20d + -1d
        }
    }
}
