using System;
using System.IO;
using Microsoft.Data.Sqlite;
using VMS.AppSetup.Services;
using Xunit;

namespace VMS.AppSetup.Tests
{
    /// <summary>
    /// InitialAdminSeeder DB 부트스트랩 검증 — MSI 설치 직후 VMS 를 한 번도 띄우지 않아
    /// BodaVision.db 가 없는 신규 install 에서도 wizard 첫 실행의 비밀번호 시드가
    /// 완료되어야 한다 (이전에는 DB 부재 시 조용히 skip → admin 미생성).
    /// </summary>
    public class InitialAdminSeederTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), $"vms_seeder_test_{Guid.NewGuid():N}");
        private readonly string _dbPath;

        public InitialAdminSeederTests()
        {
            _dbPath = Path.Combine(_dir, "BodaVision.db");
        }

        public void Dispose()
        {
            // WAL 보조 파일(-wal/-shm)까지 포함해 폴더째 정리
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private (string hash, long grade)? QueryUser(string username)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT PasswordHash, Grade FROM Users WHERE Username = @u COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@u", username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return (reader.GetString(0), reader.GetInt64(1));
        }

        [Fact]
        public void SeedAdminIfMissing_NoDbFile_CreatesDbAndSeedsAdmin()
        {
            Assert.False(File.Exists(_dbPath));

            Assert.True(InitialAdminSeeder.SeedAdminIfMissing("StrongPass12!", _dbPath));

            Assert.True(File.Exists(_dbPath));
            var admin = QueryUser("admin");
            Assert.NotNull(admin);
            Assert.Equal(2, admin!.Value.grade);   // UserGrade.Admin
            Assert.True(BCrypt.Net.BCrypt.Verify("StrongPass12!", admin.Value.hash));
        }

        [Fact]
        public void SeedAdminIfMissing_AdminAlreadyExists_DoesNotOverwrite()
        {
            Assert.True(InitialAdminSeeder.SeedAdminIfMissing("FirstPass12!", _dbPath));
            var firstHash = QueryUser("admin")!.Value.hash;

            Assert.False(InitialAdminSeeder.SeedAdminIfMissing("SecondPass12!", _dbPath));
            Assert.Equal(firstHash, QueryUser("admin")!.Value.hash);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("short7!")]
        public void SeedAdminIfMissing_WeakOrEmptyPassword_ReturnsFalseWithoutDb(string password)
        {
            Assert.False(InitialAdminSeeder.SeedAdminIfMissing(password, _dbPath));
            Assert.False(File.Exists(_dbPath));   // 유효성 실패는 DB 생성 전에 걸러짐
        }

        [Fact]
        public void SetLocalFallbackPassword_NoDbFile_CreatesDbAndSeedsLocalAdmin()
        {
            Assert.False(File.Exists(_dbPath));

            Assert.True(InitialAdminSeeder.SetLocalFallbackPassword("FallbackPass12!", _dbPath));

            var fallback = QueryUser("local-admin");
            Assert.NotNull(fallback);
            Assert.Equal(2, fallback!.Value.grade);
            Assert.True(BCrypt.Net.BCrypt.Verify("FallbackPass12!", fallback.Value.hash));
        }

        [Fact]
        public void SetLocalFallbackPassword_ExistingRow_UpdatesHash()
        {
            Assert.True(InitialAdminSeeder.SetLocalFallbackPassword("FirstPass12!", _dbPath));
            Assert.True(InitialAdminSeeder.SetLocalFallbackPassword("SecondPass12!", _dbPath));

            var fallback = QueryUser("local-admin")!;
            Assert.True(BCrypt.Net.BCrypt.Verify("SecondPass12!", fallback.Value.hash));
            Assert.False(BCrypt.Net.BCrypt.Verify("FirstPass12!", fallback.Value.hash));
        }

        [Fact]
        public void BootstrappedDb_UsesWalJournalMode()
        {
            // VMS UserService 와 동일한 journal_mode 보장 (동시 접근 SQLITE_BUSY 방지)
            Assert.True(InitialAdminSeeder.SeedAdminIfMissing("StrongPass12!", _dbPath));

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", (string)cmd.ExecuteScalar()!, ignoreCase: true);
        }

        [Fact]
        public void BootstrappedSchema_MatchesVmsUserServiceSchema()
        {
            // VMS UserService.InitializeDatabase 의 CREATE TABLE IF NOT EXISTS 가
            // 이 스키마 위에서 그대로 no-op 이 되도록 컬럼 구성이 일치해야 한다.
            Assert.True(InitialAdminSeeder.SeedAdminIfMissing("StrongPass12!", _dbPath));

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(Users);";
            using var reader = cmd.ExecuteReader();
            var cols = new System.Collections.Generic.List<string>();
            while (reader.Read()) cols.Add(reader.GetString(1));

            Assert.Equal(
                new[] { "UserId", "Username", "PasswordHash", "DisplayName", "Grade", "CreatedAt", "LastLoginAt" },
                cols);
        }
    }
}
