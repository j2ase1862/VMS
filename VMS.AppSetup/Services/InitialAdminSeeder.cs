using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace VMS.AppSetup.Services
{
    /// <summary>
    /// AppSetup wizard 가 운영자 비밀번호로 admin / local-admin 시드 (Option C, 2026-06-04).
    /// VMS UserService.SeedInitialAdmin / SetLocalFallbackPassword 와 동일 동작:
    /// - admin 행 없음 → 입력 비밀번호로 시드 (Grade=Admin=2)
    /// - 이미 admin 있음 → skip (덮어쓰기 안 함)
    /// - local-admin → 비밀번호 변경 (UPDATE PasswordHash)
    /// 비밀번호는 BCrypt 해시로만 저장. 평문은 호출 후 즉시 폐기.
    /// </summary>
    public static class InitialAdminSeeder
    {
        private const string DbFileName = "BodaVision.db";
        private const string LocalFallbackUsername = "local-admin";

        /// <summary>VMS UserService 가 사용하는 DB 경로 (인스턴스별 AppData).</summary>
        public static string GetDefaultDbPath() =>
            VMS.Camera.Configuration.AppDataPaths.GetPath(DbFileName);

        /// <summary>
        /// admin 시드 — 빈 비밀번호면 skip, 길이 8 미만이면 false.
        /// 이미 admin 존재시 false 반환 (덮어쓰기 안 함).
        /// </summary>
        public static bool SeedAdminIfMissing(string password, string dbPath = "")
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8) return false;
            if (string.IsNullOrEmpty(dbPath)) dbPath = GetDefaultDbPath();
            if (!File.Exists(dbPath)) return false;  // VMS 가 아직 안 떴음 — DB 없음

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = 'admin' COLLATE NOCASE";
            if ((long)check.ExecuteScalar()! > 0) return false;

            using var ins = conn.CreateCommand();
            ins.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, DisplayName, Grade, CreatedAt)
                VALUES ('admin', @hash, 'Administrator', 2, @created)";
            ins.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
            ins.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
            return ins.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// local-admin 비밀번호 변경 — 빈 비밀번호면 skip, 8 미만이면 false.
        /// local-admin 행 없으면 false (VMS InitializeDatabase 가 시드 안 했음).
        /// </summary>
        public static bool SetLocalFallbackPassword(string newPassword, string dbPath = "")
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8) return false;
            if (string.IsNullOrEmpty(dbPath)) dbPath = GetDefaultDbPath();
            if (!File.Exists(dbPath)) return false;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var upd = conn.CreateCommand();
            upd.CommandText = @"
                UPDATE Users SET PasswordHash = @hash
                WHERE Username = @username COLLATE NOCASE";
            upd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
            upd.Parameters.AddWithValue("@username", LocalFallbackUsername);
            return upd.ExecuteNonQuery() > 0;
        }
    }
}
