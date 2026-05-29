using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using VMS.Core.Backup;
using Xunit;

namespace VMS.Core.Tests.Backup
{
    /// <summary>
    /// BackupRestoreService 의 백업 / 복원 라운드트립과 보안 / edge case 검증.
    /// 모든 테스트는 임시 디렉토리로 격리.
    /// </summary>
    public class BackupRestoreServiceTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _sourceDir;
        private readonly string _restoreDir;
        private readonly string _backupZip;

        public BackupRestoreServiceTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), $"backup_{Guid.NewGuid():N}");
            _sourceDir = Path.Combine(_tempBase, "source");
            _restoreDir = Path.Combine(_tempBase, "restore");
            _backupZip = Path.Combine(_tempBase, "backup.zip");
            Directory.CreateDirectory(_sourceDir);
            Directory.CreateDirectory(_restoreDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempBase)) Directory.Delete(_tempBase, recursive: true); }
            catch { /* 무시 */ }
        }

        private void Seed(string relPath, string content)
        {
            var path = Path.Combine(_sourceDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        // ─── 기본 라운드트립 ─────────────────────────────────────

        [Fact]
        public void CreateBackup_AllWhitelistedFiles_Included()
        {
            Seed("system_config.json", "{\"a\":1}");
            Seed("layout_config.json", "{\"b\":2}");
            Seed("plc_signals.json", "{\"c\":3}");
            Seed("BodaVision.db", "fake-db-bytes");
            Seed("recipes/r1.json", "{\"r\":\"one\"}");
            Seed("recipes/r2.json", "{\"r\":\"two\"}");

            var result = BackupRestoreService.CreateBackup(_sourceDir, _backupZip);

            Assert.True(result.Success);
            Assert.Equal(6, result.FileCount);
            Assert.True(File.Exists(_backupZip));
            Assert.True(result.BytesWritten > 0);
        }

        [Fact]
        public void CreateBackup_AuditDir_ExcludedByDefault()
        {
            Seed("audit/2026-05-29.jsonl", "{}\n");
            Seed("system_config.json", "{}");

            var result = BackupRestoreService.CreateBackup(_sourceDir, _backupZip);

            using var zip = ZipFile.OpenRead(_backupZip);
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("audit/"));
            Assert.True(result.Success);
        }

        [Fact]
        public void CreateBackup_IncludeAuditOption_AddsAuditFiles()
        {
            Seed("audit/2026-05-29.jsonl", "{}\n");
            Seed("system_config.json", "{}");

            var result = BackupRestoreService.CreateBackup(_sourceDir, _backupZip,
                new BackupOptions { IncludeAudit = true });

            using var zip = ZipFile.OpenRead(_backupZip);
            Assert.Contains(zip.Entries, e => e.FullName == "audit/2026-05-29.jsonl");
            Assert.True(result.Success);
        }

        [Fact]
        public void CreateBackup_ManifestPresent_AndValid()
        {
            Seed("system_config.json", "{}");

            var result = BackupRestoreService.CreateBackup(_sourceDir, _backupZip,
                new BackupOptions { ProductVersion = "1.2.0", IncludeAudit = false });
            Assert.True(result.Success);

            using var zip = ZipFile.OpenRead(_backupZip);
            var manifest = zip.GetEntry("backup_manifest.json");
            Assert.NotNull(manifest);

            using var ms = manifest!.Open();
            using var reader = new StreamReader(ms);
            var json = reader.ReadToEnd();

            Assert.Contains("\"productVersion\": \"1.2.0\"", json);
            Assert.Contains("\"includesAudit\": false", json);
        }

        [Fact]
        public void CreateBackup_NoFiles_StillSucceeds()
        {
            // recipes/ 도 없고 최상위 파일도 없는 빈 source — manifest 만 들어있는 ZIP 생성.
            var result = BackupRestoreService.CreateBackup(_sourceDir, _backupZip);
            Assert.True(result.Success);
            Assert.Equal(0, result.FileCount);
        }

        [Fact]
        public void CreateBackup_NonExistentSourceDir_Fails()
        {
            var bogus = Path.Combine(_tempBase, "does_not_exist");
            var result = BackupRestoreService.CreateBackup(bogus, _backupZip);

            Assert.False(result.Success);
            Assert.Contains("source dir 없음", result.ErrorMessage ?? "");
        }

        [Fact]
        public void CreateBackup_OverwritesExistingZip()
        {
            Seed("system_config.json", "{}");

            // 첫 번째 생성
            BackupRestoreService.CreateBackup(_sourceDir, _backupZip);
            var firstSize = new FileInfo(_backupZip).Length;

            // 추가 파일 후 재생성 — 덮어쓰기 동작 검증.
            Seed("recipes/r1.json", "{}");
            var result = BackupRestoreService.CreateBackup(_sourceDir, _backupZip);
            Assert.True(result.Success);
            var secondSize = new FileInfo(_backupZip).Length;

            Assert.True(secondSize >= firstSize);
        }

        // ─── 복원 라운드트립 ─────────────────────────────────────

        [Fact]
        public void RestoreBackup_RoundTrip_FilesMatch()
        {
            Seed("system_config.json", "{\"key\":\"value\"}");
            Seed("recipes/r1.json", "{\"recipe\":1}");
            Seed("recipes/r2.json", "{\"recipe\":2}");

            BackupRestoreService.CreateBackup(_sourceDir, _backupZip);
            var result = BackupRestoreService.RestoreBackup(_backupZip, _restoreDir);

            Assert.True(result.Success);
            Assert.Equal(3, result.FilesRestored);
            Assert.NotNull(result.Manifest);
            Assert.Equal(
                File.ReadAllText(Path.Combine(_sourceDir, "system_config.json")),
                File.ReadAllText(Path.Combine(_restoreDir, "system_config.json")));
            Assert.Equal(
                File.ReadAllText(Path.Combine(_sourceDir, "recipes", "r1.json")),
                File.ReadAllText(Path.Combine(_restoreDir, "recipes", "r1.json")));
        }

        [Fact]
        public void RestoreBackup_OverwriteFalse_SkipsExistingFiles()
        {
            Seed("system_config.json", "{\"original\":true}");
            BackupRestoreService.CreateBackup(_sourceDir, _backupZip);

            // 복원 대상에 미리 다른 내용 — overwrite=false → 보존.
            var existing = Path.Combine(_restoreDir, "system_config.json");
            File.WriteAllText(existing, "{\"existing\":true}");

            var result = BackupRestoreService.RestoreBackup(_backupZip, _restoreDir,
                new RestoreOptions { Overwrite = false });

            Assert.True(result.Success);
            Assert.Equal(1, result.FilesSkipped);
            Assert.Equal("{\"existing\":true}", File.ReadAllText(existing));
        }

        [Fact]
        public void RestoreBackup_NoManifest_Fails()
        {
            // manifest 없는 일반 ZIP 생성
            using (var fs = new FileStream(_backupZip, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("random.txt");
                using var w = new StreamWriter(entry.Open());
                w.Write("hello");
            }

            var result = BackupRestoreService.RestoreBackup(_backupZip, _restoreDir);
            Assert.False(result.Success);
            Assert.Contains("manifest 없음", result.ErrorMessage ?? "");
        }

        [Fact]
        public void RestoreBackup_NonExistentZip_Fails()
        {
            var result = BackupRestoreService.RestoreBackup(
                Path.Combine(_tempBase, "missing.zip"), _restoreDir);

            Assert.False(result.Success);
            Assert.Contains("백업 파일 없음", result.ErrorMessage ?? "");
        }

        // ─── Path traversal 방어 ──────────────────────────────────

        [Fact]
        public void RestoreBackup_EntryEscapesTarget_Rejected()
        {
            // 악성 ZIP 직접 생성 — ".." 시퀀스 + manifest 동봉.
            using (var fs = new FileStream(_backupZip, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var manifest = zip.CreateEntry("backup_manifest.json");
                using (var w = new StreamWriter(manifest.Open()))
                    w.Write("{\"schemaVersion\":1,\"createdAtUtc\":\"2026-05-29T00:00:00Z\"}");

                var evil = zip.CreateEntry("../../escape.txt");
                using (var w = new StreamWriter(evil.Open()))
                    w.Write("pwned");

                var ok = zip.CreateEntry("system_config.json");
                using (var w = new StreamWriter(ok.Open()))
                    w.Write("{}");
            }

            var result = BackupRestoreService.RestoreBackup(_backupZip, _restoreDir);

            Assert.True(result.Success);
            // 정상 파일은 복원, 악성 entry 는 skipped.
            Assert.Equal(1, result.FilesRestored);
            Assert.Equal(1, result.FilesSkipped);
            Assert.True(File.Exists(Path.Combine(_restoreDir, "system_config.json")));

            // 절대 escape 경로에 파일이 생기지 않아야 함.
            var escapePath = Path.GetFullPath(Path.Combine(_restoreDir, "..", "..", "escape.txt"));
            Assert.False(File.Exists(escapePath));
        }

        // ─── audit RestoreOption ─────────────────────────────────

        [Fact]
        public void RestoreBackup_RestoreAuditFalse_SkipsAuditEntries()
        {
            Seed("system_config.json", "{}");
            Seed("audit/2026-05-29.jsonl", "{}\n");
            BackupRestoreService.CreateBackup(_sourceDir, _backupZip,
                new BackupOptions { IncludeAudit = true });

            var result = BackupRestoreService.RestoreBackup(_backupZip, _restoreDir,
                new RestoreOptions { RestoreAudit = false });

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(_restoreDir, "system_config.json")));
            Assert.False(File.Exists(Path.Combine(_restoreDir, "audit", "2026-05-29.jsonl")));
        }

        // ─── Manifest 메타데이터 ──────────────────────────────────

        [Fact]
        public void Manifest_RecordsIncludesUsersDb()
        {
            Seed("BodaVision.db", "fake");
            BackupRestoreService.CreateBackup(_sourceDir, _backupZip);

            var result = BackupRestoreService.RestoreBackup(_backupZip, _restoreDir);
            Assert.True(result.Manifest!.IncludesUsersDb);
        }

        [Fact]
        public void Manifest_RecordsZeroFiles_WhenSourceEmpty()
        {
            BackupRestoreService.CreateBackup(_sourceDir, _backupZip);
            var result = BackupRestoreService.RestoreBackup(_backupZip, _restoreDir);

            Assert.Equal(0, result.Manifest!.FileCount);
            Assert.False(result.Manifest.IncludesUsersDb);
        }
    }
}
