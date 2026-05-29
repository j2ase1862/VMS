using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using VMS.Core.Security;
using VMS.Interfaces;
using VMS.Models;

namespace VMS.Services
{
    public class UserService : IUserService
    {
        private static readonly Lazy<UserService> _instance = new(() => new UserService());
        public static UserService Instance => _instance.Value;

        private readonly string _connectionString;
        private readonly string _dbPath;

        public User? CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;

        private UserService()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BODA VISION AI"))
        {
        }

        /// <summary>
        /// 임의 디렉토리(DB 파일 위치)로 별도 인스턴스를 만들 수 있는 테스트 친화 ctor.
        /// internal — VMS.Tests 통합 테스트에서만 호출. 운영 코드는
        /// 반드시 <see cref="Instance"/> 싱글톤 사용.
        /// </summary>
        internal UserService(string dbFolder)
        {
            Directory.CreateDirectory(dbFolder);

            _dbPath = Path.Combine(dbFolder, "BodaVision.db");
            _connectionString = $"Data Source={_dbPath}";

            InitializeDatabase();
            ApplyDatabaseFileAcl();
        }

        /// <summary>
        /// BodaVision.db 파일에 현재 사용자만 접근 가능한 ACL 적용.
        /// 다른 로컬 사용자 / 그룹의 권한 제거 + 상속 차단. BCrypt 해시가 있긴 하지만
        /// 파일 노출 자체를 차단해 오프라인 brute-force 시도까지 추가 방어선.
        /// 실패 시(권한 부족 / 비 Windows) 디버그 로그만 — 앱 동작은 계속.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private void ApplyDatabaseFileAcl()
        {
            try
            {
                if (!File.Exists(_dbPath)) return;

                var fileInfo = new FileInfo(_dbPath);
                var security = fileInfo.GetAccessControl();

                // 상속 차단 + 기존 명시 권한 제거 (보호 모드 활성).
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    security.SetOwner(currentUser);
                    security.SetAccessRule(new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));
                }
                fileInfo.SetAccessControl(security);
                Debug.WriteLine($"[UserService] DB ACL applied: owner-only on {_dbPath}");
            }
            catch (Exception ex)
            {
                // 권한 부족 등 — 운영 중 ACL 변경 실패는 치명적이지 않음 (앱 격리는 OS profile 폴더로 이미 1차 방어).
                Debug.WriteLine($"[UserService] ApplyDatabaseFileAcl 실패: {ex.Message}");
            }
        }

        private void InitializeDatabase()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    PasswordHash TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Grade INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    LastLoginAt TEXT
                )";
            cmd.ExecuteNonQuery();

            // Seed default admin account if no users exist
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = (long)cmd.ExecuteScalar()!;
            if (count == 0)
            {
                cmd.CommandText = @"
                    INSERT INTO Users (Username, PasswordHash, DisplayName, Grade, CreatedAt)
                    VALUES (@username, @hash, @display, @grade, @created)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@username", "admin");
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword("admin123"));
                cmd.Parameters.AddWithValue("@display", "Administrator");
                cmd.Parameters.AddWithValue("@grade", (int)UserGrade.Admin);
                cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        public bool Authenticate(string username, string password)
        {
            // DoS / control-char 주입 방어 — 잘못된 입력은 DB 호출 없이 즉시 거부.
            try
            {
                CredentialGuard.ValidateIdentifier(username, nameof(username));
                CredentialGuard.ValidateSecret(password, nameof(password));
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"[UserService] Invalid auth input: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "UserLogin", AuditOutcome.Denied,
                    userName: username, source: nameof(UserService),
                    details: $"Invalid input: {ex.Message}");
                return false;
            }

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Users WHERE Username = @username";
            cmd.Parameters.AddWithValue("@username", username);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "UserLogin", AuditOutcome.Failure,
                    userName: username, source: nameof(UserService),
                    details: "User not found");
                return false;
            }

            var hash = reader.GetString(reader.GetOrdinal("PasswordHash"));
            if (!BCrypt.Net.BCrypt.Verify(password, hash))
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "UserLogin", AuditOutcome.Denied,
                    userName: username, source: nameof(UserService),
                    details: "Password mismatch");
                return false;
            }

            CurrentUser = ReadUser(reader);

            // Update last login time
            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE Users SET LastLoginAt = @now WHERE UserId = @id";
            updateCmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            updateCmd.Parameters.AddWithValue("@id", CurrentUser.UserId);
            updateCmd.ExecuteNonQuery();

            CurrentUser.LastLoginAt = DateTime.UtcNow;
            AuditLogger.Instance.Log(
                AuditCategory.Authentication, "UserLogin", AuditOutcome.Success,
                userName: username, source: nameof(UserService),
                details: $"Grade={CurrentUser.Grade}");
            return true;
        }

        public void Logout()
        {
            var loggedOutUser = CurrentUser?.Username;
            CurrentUser = null;
            if (!string.IsNullOrEmpty(loggedOutUser))
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "UserLogout", AuditOutcome.Success,
                    userName: loggedOutUser, source: nameof(UserService));
            }
        }

        public bool HasPermission(UserPermission permission)
        {
            if (CurrentUser == null)
            {
                // 비로그인 상태 — 모든 권한 거부. 빈도 높아 audit log 폭증 방지 차원에서 미기록.
                return false;
            }

            bool granted = CurrentUser.Grade switch
            {
                UserGrade.Admin => true,
                UserGrade.Engineer => permission switch
                {
                    UserPermission.ManageUsers => false,
                    _ => true
                },
                UserGrade.Operator => permission switch
                {
                    UserPermission.StartStop => true,
                    UserPermission.ViewStatistics => true,
                    _ => false
                },
                _ => false
            };

            // 권한 거부만 기록 — 거부는 드물고 보안 의의가 크지만 허용은 빈도 높아 폭증 우려.
            if (!granted)
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Authorization, "PermissionDenied", AuditOutcome.Denied,
                    userName: CurrentUser.Username, source: nameof(UserService),
                    details: $"Permission={permission}, Grade={CurrentUser.Grade}");
            }
            return granted;
        }

        public bool CreateUser(string username, string password, string displayName, UserGrade grade)
        {
            // 신규 사용자 생성 시점에 강한 입력 검증 — 최소 길이 4 (산업 운영자 기준, 정책 변경 시 조정).
            try
            {
                CredentialGuard.ValidateIdentifier(username, nameof(username));
                CredentialGuard.ValidateSecret(password, nameof(password), minLength: 4);
                CredentialGuard.ValidateIdentifier(displayName, nameof(displayName));
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"[UserService] CreateUser invalid input: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.UserManagement, "CreateUser", AuditOutcome.Denied,
                    userName: CurrentUser?.Username, source: nameof(UserService),
                    details: $"Target='{username}', Invalid input: {ex.Message}");
                return false;
            }

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Users (Username, PasswordHash, DisplayName, Grade, CreatedAt)
                    VALUES (@username, @hash, @display, @grade, @created)";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
                cmd.Parameters.AddWithValue("@display", displayName);
                cmd.Parameters.AddWithValue("@grade", (int)grade);
                cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
                AuditLogger.Instance.Log(
                    AuditCategory.UserManagement, "CreateUser", AuditOutcome.Success,
                    userName: CurrentUser?.Username, source: nameof(UserService),
                    details: $"Created='{username}', Grade={grade}");
                return true;
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Log(
                    AuditCategory.UserManagement, "CreateUser", AuditOutcome.Failure,
                    userName: CurrentUser?.Username, source: nameof(UserService),
                    details: $"Target='{username}', {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public bool UpdateUser(int userId, string displayName, UserGrade grade)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Users SET DisplayName = @display, Grade = @grade
                    WHERE UserId = @id";
                cmd.Parameters.AddWithValue("@display", displayName);
                cmd.Parameters.AddWithValue("@grade", (int)grade);
                cmd.Parameters.AddWithValue("@id", userId);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch
            {
                return false;
            }
        }

        public bool ChangePassword(int userId, string newPassword)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE Users SET PasswordHash = @hash WHERE UserId = @id";
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
                cmd.Parameters.AddWithValue("@id", userId);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteUser(int userId)
        {
            // Prevent deleting the currently logged-in user
            if (CurrentUser?.UserId == userId) return false;

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Users WHERE UserId = @id";
                cmd.Parameters.AddWithValue("@id", userId);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch
            {
                return false;
            }
        }

        public List<User> GetAllUsers()
        {
            var users = new List<User>();

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Users ORDER BY Grade DESC, Username";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                users.Add(ReadUser(reader));
            }

            return users;
        }

        private static User ReadUser(SqliteDataReader reader)
        {
            var lastLoginStr = reader.IsDBNull(reader.GetOrdinal("LastLoginAt"))
                ? null
                : reader.GetString(reader.GetOrdinal("LastLoginAt"));

            return new User
            {
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
                DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
                Grade = (UserGrade)reader.GetInt32(reader.GetOrdinal("Grade")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                LastLoginAt = lastLoginStr != null ? DateTime.Parse(lastLoginStr) : null
            };
        }
    }
}
