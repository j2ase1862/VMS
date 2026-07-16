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
    /// - local-admin → 비밀번호 변경 (행 없으면 시드 — 신규 install 에서 VMS 부팅 전 실행 대응)
    /// DB 파일이 없으면 스키마를 직접 생성 (VMS UserService.InitializeDatabase 와 동일 스키마 +
    /// journal_mode=WAL) — MSI 설치 직후 VMS 를 한 번도 띄우지 않은 신규 install 에서도
    /// wizard 첫 실행에서 비밀번호 시드가 완료되도록. VMS 측 CREATE TABLE IF NOT EXISTS /
    /// local-admin 조건부 시드는 idempotent 라 이후 부팅과 충돌 없음.
    /// 비밀번호는 BCrypt 해시로만 저장. 평문은 호출 후 즉시 폐기.
    /// </summary>
    public static class InitialAdminSeeder
    {
        private const string DbFileName = "BodaVision.db";
        private const string LocalFallbackUsername = "local-admin";
        private const int AdminGrade = 2;   // VMS.Models.UserGrade.Admin

        /// <summary>VMS UserService 가 사용하는 DB 경로 (인스턴스별 AppData).</summary>
        public static string GetDefaultDbPath() =>
            VMS.Camera.Configuration.AppDataPaths.GetPath(DbFileName);

        /// <summary>
        /// DB 파일/스키마 보장 — VMS UserService.InitializeDatabase 의 Users 테이블과 동일.
        /// WAL 도 동일하게 설정해 "어느 쪽이 먼저 떠도 같은 저널 모드" 보장을 유지.
        /// </summary>
        private static SqliteConnection OpenWithSchema(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();

            using var create = conn.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    PasswordHash TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Grade INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    LastLoginAt TEXT
                )";
            create.ExecuteNonQuery();
            return conn;
        }

        /// <summary>
        /// admin 행 존재 여부 — wizard 재실행(업데이트 후 등)에서 "이미 존재하여 입력한
        /// 비밀번호가 적용되지 않음" 안내를 8자 미만 실패와 구분하기 위한 조회.
        /// DB 파일이 없으면 false (파일을 생성하지 않음).
        /// </summary>
        public static bool AdminExists(string dbPath = "")
        {
            if (string.IsNullOrEmpty(dbPath)) dbPath = GetDefaultDbPath();
            if (!File.Exists(dbPath)) return false;

            using var conn = OpenWithSchema(dbPath);
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = 'admin' COLLATE NOCASE";
            return (long)check.ExecuteScalar()! > 0;
        }

        /// <summary>
        /// admin 시드 — 빈 비밀번호면 skip, 길이 8 미만이면 false.
        /// 이미 admin 존재시 false 반환 (덮어쓰기 안 함).
        /// DB 파일이 없으면 생성 후 시드 (신규 install 첫 wizard 실행 지원).
        /// </summary>
        public static bool SeedAdminIfMissing(string password, string dbPath = "")
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8) return false;
            if (string.IsNullOrEmpty(dbPath)) dbPath = GetDefaultDbPath();

            using var conn = OpenWithSchema(dbPath);

            using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = 'admin' COLLATE NOCASE";
            if ((long)check.ExecuteScalar()! > 0) return false;

            using var ins = conn.CreateCommand();
            ins.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, DisplayName, Grade, CreatedAt)
                VALUES ('admin', @hash, 'Administrator', @grade, @created)";
            ins.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
            ins.Parameters.AddWithValue("@grade", AdminGrade);
            ins.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
            return ins.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// local-admin 비밀번호 변경 — 빈 비밀번호면 skip, 8 미만이면 false.
        /// 행이 없으면 시드 (신규 install 에서 VMS 가 아직 안 떠 local-admin 미생성인 경우).
        /// VMS InitializeDatabase 의 local-admin 조건부 시드는 행이 있으면 건드리지 않으므로
        /// 여기서 넣은 비밀번호가 이후 VMS 부팅에서 디폴트로 덮이지 않음.
        /// </summary>
        public static bool SetLocalFallbackPassword(string newPassword, string dbPath = "")
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8) return false;
            if (string.IsNullOrEmpty(dbPath)) dbPath = GetDefaultDbPath();

            using var conn = OpenWithSchema(dbPath);
            var hash = BCrypt.Net.BCrypt.HashPassword(newPassword);

            using var upd = conn.CreateCommand();
            upd.CommandText = @"
                UPDATE Users SET PasswordHash = @hash
                WHERE Username = @username COLLATE NOCASE";
            upd.Parameters.AddWithValue("@hash", hash);
            upd.Parameters.AddWithValue("@username", LocalFallbackUsername);
            if (upd.ExecuteNonQuery() > 0) return true;

            using var ins = conn.CreateCommand();
            ins.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, DisplayName, Grade, CreatedAt)
                VALUES (@username, @hash, 'Local Fallback Administrator', @grade, @created)";
            ins.Parameters.AddWithValue("@username", LocalFallbackUsername);
            ins.Parameters.AddWithValue("@hash", hash);
            ins.Parameters.AddWithValue("@grade", AdminGrade);
            ins.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
            return ins.ExecuteNonQuery() > 0;
        }
    }
}
