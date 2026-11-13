using System;
using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using VMS.Core.Backup;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 실행 중 백업 · 복원 회귀 (2026-11-13 영상 녹화 중 발견) — VMS 가 SQLite 연결을 열어 둔 채로
    /// [Run Backup...] · 자동 백업을 하면 BodaVision.db · inspection_history.db 가 "사용 중" 이라
    /// 항상 실패했다. 열린 연결이 있는 실제 SQLite DB 로 백업 → 복원을 확인한다.
    /// </summary>
    [Collection(BackupDatabaseCopierCollection.Name)]
    public class SqliteDatabaseCopierTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _appDataDir;
        private readonly string _zipPath;
        private readonly Action<string, string>? _previousCopier;

        public SqliteDatabaseCopierTests()
        {
            _tempBase = Path.Combine(Path.GetTempPath(), $"sqlite_backup_{Guid.NewGuid():N}");
            _appDataDir = Path.Combine(_tempBase, "appdata");
            _zipPath = Path.Combine(_tempBase, "backup.zip");
            Directory.CreateDirectory(_appDataDir);
            _previousCopier = BackupRestoreService.DatabaseCopier;
            BackupRestoreService.DatabaseCopier = SqliteDatabaseCopier.Copy;
        }

        public void Dispose()
        {
            BackupRestoreService.DatabaseCopier = _previousCopier;
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(_tempBase)) Directory.Delete(_tempBase, recursive: true); }
            catch { /* SQLite 핸들 잔존 — 다른 테스트 영향 없음 */ }
        }

        private string DbPath(string name) => Path.Combine(_appDataDir, name);

        private static SqliteConnection OpenPooled(string path)
        {
            // 운영 코드(UserService · LocalInspectionHistoryStore)처럼 기본 풀링 연결
            var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            return conn;
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static long Count(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            return (long)cmd.ExecuteScalar()!;
        }

        [Fact]
        public void Backup_WhileConnectionsOpen_SucceedsAndContainsRows()
        {
            using var users = OpenPooled(DbPath("BodaVision.db"));
            Exec(users, "CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Users (Name) VALUES ('admin'), ('line1');");
            using var history = OpenPooled(DbPath("inspection_history.db"));
            Exec(history, "CREATE TABLE History (Id INTEGER PRIMARY KEY); INSERT INTO History DEFAULT VALUES;");
            File.WriteAllText(DbPath("system_config.json"), "{}");

            var result = BackupRestoreService.CreateBackup(_appDataDir, _zipPath);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(3, result.FileCount);

            // ZIP 안의 사용자 DB 가 열리는 SQLite 이고 행이 그대로인지
            var extracted = Path.Combine(_tempBase, "check.db");
            using (var zip = ZipFile.OpenRead(_zipPath))
                zip.GetEntry("BodaVision.db")!.ExtractToFile(extracted);
            using var check = new SqliteConnection($"Data Source={extracted};Pooling=False");
            check.Open();
            Assert.Equal(2, Count(check, "Users"));
        }

        [Fact]
        public void Restore_WhileConnectionOpen_ReplacesContent()
        {
            using var users = OpenPooled(DbPath("BodaVision.db"));
            Exec(users, "CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Users (Name) VALUES ('admin');");
            Assert.True(BackupRestoreService.CreateBackup(_appDataDir, _zipPath).Success);

            // 백업 뒤에 사용자를 더 만든 상태에서 복원 → 백업 시점(1명)으로 돌아가야 함
            Exec(users, "INSERT INTO Users (Name) VALUES ('later1'), ('later2');");
            Assert.Equal(3, Count(users, "Users"));

            var restore = BackupRestoreService.RestoreBackup(_zipPath, _appDataDir);

            Assert.True(restore.Success, restore.ErrorMessage);
            Assert.Equal(1, Count(users, "Users"));  // 열려 있던 연결도 복원된 내용을 본다
        }
    }

    /// <summary>
    /// <see cref="BackupRestoreService.DatabaseCopier"/> 는 정적 설정이라 이 테스트들은 한 줄로 실행한다.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class BackupDatabaseCopierCollection
    {
        public const string Name = "BackupDatabaseCopier";
    }
}
