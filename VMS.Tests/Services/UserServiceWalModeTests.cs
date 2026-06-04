using System;
using System.IO;
using Microsoft.Data.Sqlite;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// UserService.InitializeDatabase 가 SQLite 의 journal_mode 를 WAL 로 명시 설정하는지 검증 (GS P2).
    /// BodaVision.db 는 VMS 데스크탑 + BODA.VMS.Web 이 동시 접근하므로 default "delete"
    /// 모드면 동시 쓰기 SQLITE_BUSY 충돌 위험. WAL 은 persistent 라 한 번 설정하면 유지됨.
    /// </summary>
    public class UserServiceWalModeTests : IDisposable
    {
        private readonly string _tempDir;

        public UserServiceWalModeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"users_wal_{Guid.NewGuid():N}");
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void InitializeDatabase_sets_journal_mode_to_wal()
        {
            // ctor 가 InitializeDatabase 호출 → WAL 모드 set
            var svc = new UserService(_tempDir);
            _ = svc.GetAllUsers(); // 추가 connection 으로 WAL persistence 확인

            var dbPath = Path.Combine(_tempDir, "BodaVision.db");
            Assert.True(File.Exists(dbPath), "DB 파일 생성됨");

            // 새 connection 으로 직접 PRAGMA 조회 — WAL 은 file-level persistent 라
            // 별도 process 가 열어도 동일 모드 반환.
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            var mode = (string)cmd.ExecuteScalar()!;

            Assert.Equal("wal", mode, ignoreCase: true);
        }

        [Fact]
        public void Wal_mode_persists_across_multiple_user_service_instances()
        {
            // 첫 인스턴스가 WAL 설정 → 두 번째 인스턴스가 떠도 그대로 유지
            var first = new UserService(_tempDir);
            _ = first.GetAllUsers();

            var second = new UserService(_tempDir);
            _ = second.GetAllUsers();

            var dbPath = Path.Combine(_tempDir, "BodaVision.db");
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            var mode = (string)cmd.ExecuteScalar()!;

            Assert.Equal("wal", mode, ignoreCase: true);
        }
    }
}
