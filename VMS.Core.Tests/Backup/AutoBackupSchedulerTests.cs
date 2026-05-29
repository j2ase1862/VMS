using System;
using System.IO;
using System.Linq;
using VMS.Core.Backup;
using Xunit;

namespace VMS.Core.Tests.Backup
{
    /// <summary>
    /// AutoBackupScheduler 의 결정적 동작 (Clamp / 정리 / Start-Stop) 검증.
    /// Timer 의 실제 fire 는 분 단위 대기가 필요해 검증 범위 밖.
    /// </summary>
    public class AutoBackupSchedulerTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _appDataDir;
        private readonly string _backupDir;

        public AutoBackupSchedulerTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), $"autobackup_{Guid.NewGuid():N}");
            _appDataDir = Path.Combine(_tempBase, "appdata");
            _backupDir = Path.Combine(_tempBase, "backups");
            Directory.CreateDirectory(_appDataDir);
            Directory.CreateDirectory(_backupDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempBase)) Directory.Delete(_tempBase, recursive: true); }
            catch { /* 무시 */ }
        }

        private static AutoBackupOptions MakeOptions(string backupDir, int retentionDays = 30, bool enabled = false)
        {
            return new AutoBackupOptions
            {
                Enabled = enabled,
                IntervalHours = 24,
                RetentionDays = retentionDays,
                BackupDir = backupDir,
                IncludeAudit = false
            };
        }

        private string CreateAutoBackupFor(DateTime utc)
        {
            var name = $"auto-backup-{utc:yyyyMMdd-HHmmss}.zip";
            var path = Path.Combine(_backupDir, name);
            File.WriteAllText(path, "fake-zip-content");
            return path;
        }

        // ─── AutoBackupOptions.Clamp ──────────────────────────────

        [Fact]
        public void ClampInterval_BelowMin_ReturnsMin()
        {
            Assert.Equal(AutoBackupOptions.MinIntervalHours, AutoBackupOptions.ClampInterval(0));
        }

        [Fact]
        public void ClampInterval_AboveMax_ReturnsMax()
        {
            Assert.Equal(AutoBackupOptions.MaxIntervalHours, AutoBackupOptions.ClampInterval(99999));
        }

        [Fact]
        public void ClampInterval_InRange_Unchanged()
        {
            Assert.Equal(24, AutoBackupOptions.ClampInterval(24));
        }

        [Fact]
        public void ClampRetention_BelowMin_ReturnsMin()
        {
            Assert.Equal(AutoBackupOptions.MinRetentionDays, AutoBackupOptions.ClampRetention(0));
        }

        [Fact]
        public void ClampRetention_AboveMax_ReturnsMax()
        {
            Assert.Equal(AutoBackupOptions.MaxRetentionDays, AutoBackupOptions.ClampRetention(99999));
        }

        // ─── LoadFromAppData ──────────────────────────────────────

        [Fact]
        public void LoadFromAppData_ReturnsDisabledByDefault()
        {
            // 기본 사용자 환경 — autoBackup 키 누락 시 Enabled=false.
            var options = AutoBackupOptions.LoadFromAppData();
            Assert.False(options.Enabled);
        }

        // ─── Cleanup — 보존 경계 ─────────────────────────────────

        [Fact]
        public void CleanupOldBackups_FileOlderThanRetention_Deleted()
        {
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, retentionDays: 30));

            var old = CreateAutoBackupFor(DateTime.UtcNow.AddDays(-90));
            var deleted = scheduler.CleanupOldBackups();

            Assert.True(deleted >= 1);
            Assert.False(File.Exists(old));
        }

        [Fact]
        public void CleanupOldBackups_FileWithinRetention_Kept()
        {
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, retentionDays: 30));

            var recent = CreateAutoBackupFor(DateTime.UtcNow.AddDays(-5));
            scheduler.CleanupOldBackups();

            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void CleanupOldBackups_NonAutoBackupFiles_NeverDeleted()
        {
            // 패턴 외 파일 (사용자 수동 백업) 은 절대 건드리지 않음.
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, retentionDays: 1));

            var userBackup = Path.Combine(_backupDir, "my-personal-backup.zip");
            File.WriteAllText(userBackup, "x");

            scheduler.CleanupOldBackups();
            Assert.True(File.Exists(userBackup));
        }

        [Fact]
        public void CleanupOldBackups_MalformedTimestamp_Skipped()
        {
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, retentionDays: 1));

            // 패턴 일치하지만 timestamp parse 실패 — skip 되어야 함.
            var bogus = Path.Combine(_backupDir, "auto-backup-not-a-date.zip");
            File.WriteAllText(bogus, "x");

            scheduler.CleanupOldBackups();
            Assert.True(File.Exists(bogus));
        }

        [Fact]
        public void CleanupOldBackups_MixedAges_OnlyOldDeleted()
        {
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, retentionDays: 30));

            var veryOld = CreateAutoBackupFor(DateTime.UtcNow.AddDays(-365));
            var old = CreateAutoBackupFor(DateTime.UtcNow.AddDays(-90));
            var recent = CreateAutoBackupFor(DateTime.UtcNow.AddDays(-5));

            var deleted = scheduler.CleanupOldBackups();

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(veryOld));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void CleanupOldBackups_NoDirectory_ReturnsZero_NoThrow()
        {
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(Path.Combine(_tempBase, "no_such_dir")));

            Assert.Equal(0, scheduler.CleanupOldBackups());
        }

        // ─── Start / Stop / Dispose 동작 ──────────────────────────

        [Fact]
        public void Start_EnabledFalse_DoesNotRun()
        {
            using var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, enabled: false));
            scheduler.Start();
            Assert.False(scheduler.IsRunning);
        }

        [Fact]
        public void Start_EnabledTrue_IsRunning()
        {
            using var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, enabled: true));
            scheduler.Start();
            Assert.True(scheduler.IsRunning);
            scheduler.Stop();
            Assert.False(scheduler.IsRunning);
        }

        [Fact]
        public void Start_CalledTwice_NoDuplicateTimer()
        {
            using var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, enabled: true));
            scheduler.Start();
            scheduler.Start();  // 두 번째는 무시 — 예외 없이.
            Assert.True(scheduler.IsRunning);
        }

        [Fact]
        public void Dispose_AfterStart_StopsCleanly()
        {
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, enabled: true));
            scheduler.Start();
            scheduler.Dispose();
            Assert.False(scheduler.IsRunning);
        }

        [Fact]
        public void Dispose_AfterDispose_NoThrow()
        {
            var scheduler = new AutoBackupScheduler(_appDataDir,
                MakeOptions(_backupDir, enabled: true));
            scheduler.Start();
            scheduler.Dispose();
            scheduler.Dispose();  // 멱등
        }

        // ─── BackupDir 기본값 ─────────────────────────────────────

        [Fact]
        public void Ctor_NullBackupDir_DefaultsToAppDataBackups()
        {
            var defaultOptions = new AutoBackupOptions
            {
                Enabled = false,
                IntervalHours = 24,
                RetentionDays = 30,
                BackupDir = null   // → default
            };
            var scheduler = new AutoBackupScheduler(_appDataDir, defaultOptions);
            // CleanupOldBackups 가 throw 없이 동작하면 디렉토리 경로 설정 OK.
            Assert.Equal(0, scheduler.CleanupOldBackups());
        }
    }
}
