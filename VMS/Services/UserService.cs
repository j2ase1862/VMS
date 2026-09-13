using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VMS.Core.Security;
using VMS.Core.Services;
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
            : this(VMS.Camera.Configuration.AppDataPaths.Root)
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

            // GS 결함 허용성 / 가혹 테스트 안전 (검토사항 P2): journal_mode=WAL 명시.
            // BodaVision.db 는 VMS 데스크탑 + BODA.VMS.Web 이 동시 접근하므로 default
            // "delete" mode 면 동시 쓰기 SQLITE_BUSY 충돌 위험. WAL 은 한 번 설정하면
            // 파일에 persistent 라 양측이 어느 쪽이 먼저 떠도 동일 모드 보장.
            // Web 측 Program.cs 도 동일 PRAGMA 호출 — idempotent 라 중복 OK.
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                pragma.ExecuteNonQuery();
            }

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

            // Option C (사용자 결정 2026-06-04): admin/admin123 디폴트 시드 제거.
            // 약한 디폴트 비밀번호가 GS 보안성 위반 + 양쪽 시스템 admin 비밀번호 불일치 운영 부담.
            // 신규 install: AppSetup wizard 의 SeedInitialAdmin 호출로 운영자가 비밀번호 입력.
            // 기존 install: DB 에 이미 있는 admin/admin123 그대로 보존 (마이그레이션은 운영 가이드 §11).

            // 비상 local-admin 시드 (SSO Migration Plan §2.3 / SSO PR3).
            // 항상 존재 보장 — SSO 활성 시 Web 도달 불가 상황의 유일한 폴백 진입점.
            // 기본 비밀번호는 PR4 AppSetup wizard 에서 운영자가 변경 권장 (인스톨러 가이드 §9 갱신 예정).
            cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = @username COLLATE NOCASE";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@username", LocalFallbackUsername);
            if ((long)cmd.ExecuteScalar()! == 0)
            {
                cmd.CommandText = @"
                    INSERT INTO Users (Username, PasswordHash, DisplayName, Grade, CreatedAt)
                    VALUES (@username, @hash, @display, @grade, @created)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@username", LocalFallbackUsername);
                cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(LocalFallbackDefaultPassword));
                cmd.Parameters.AddWithValue("@display", "Local Fallback Administrator");
                // Admin grade 로 저장 — 실제 권한 게이트는 HasPermission 이 IsLocalFallback 으로 제한
                cmd.Parameters.AddWithValue("@grade", (int)UserGrade.Admin);
                cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>비상 폴백 계정 username (대소문자 무시). 변경시 운영 가이드/매뉴얼 동기 필요.</summary>
        public const string LocalFallbackUsername = "local-admin";

        /// <summary>초기 시드 비밀번호 — AppSetup wizard 에서 운영 첫 가동시 변경 권장.</summary>
        public const string LocalFallbackDefaultPassword = "vasim1234";

        /// <summary>
        /// 비상 local-admin 폴백 세션에서 허용되는 권한 집합 (SSO Migration Plan §2.3).
        /// 운영 데이터 변경 (ManageUsers, EditRecipe, SystemConfiguration 등) 은 모두 거부.
        /// 키오스크 운영 지속 + 시스템 진단 + Web Service 재시작만 허용.
        /// </summary>
        internal static readonly System.Collections.Generic.HashSet<UserPermission> LocalFallbackAllowed = new()
        {
            UserPermission.StartStop,
            UserPermission.ViewStatistics,
            UserPermission.RestartWebService
        };

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

            // SSO Migration §2.3: 비상 폴백 계정 식별. HasPermission 이 제한 권한 적용.
            // username 대소문자 무관 비교 — DB 컬럼이 COLLATE NOCASE.
            CurrentUser.IsLocalFallback = string.Equals(
                CurrentUser.Username, LocalFallbackUsername, StringComparison.OrdinalIgnoreCase);

            // Update last login time
            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE Users SET LastLoginAt = @now WHERE UserId = @id";
            updateCmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            updateCmd.Parameters.AddWithValue("@id", CurrentUser.UserId);
            updateCmd.ExecuteNonQuery();

            CurrentUser.LastLoginAt = DateTime.UtcNow;

            // 비상 폴백 세션은 별도 details 로 표시 — 사후 분석시 식별 용이
            var loginDetails = CurrentUser.IsLocalFallback
                ? $"Grade={CurrentUser.Grade}, IsLocalFallback=true (restricted permissions apply)"
                : $"Grade={CurrentUser.Grade}";
            AuditLogger.Instance.Log(
                AuditCategory.Authentication, "UserLogin", AuditOutcome.Success,
                userName: username, source: nameof(UserService),
                details: loginDetails);
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

        // ─── SSO (SSO Migration Plan §2.2) ───────────────────────────

        public async Task<bool> AuthenticateViaWebAsync(
            string username, string password, WebAuthClient client, CancellationToken ct = default)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));

            // DoS / control-char 방어 — Web 호출 전 즉시 거부
            try
            {
                CredentialGuard.ValidateIdentifier(username, nameof(username));
                CredentialGuard.ValidateSecret(password, nameof(password));
            }
            catch (ArgumentException ex)
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "UserLogin", AuditOutcome.Denied,
                    userName: username, source: nameof(UserService),
                    details: $"Invalid input: {ex.Message}");
                return false;
            }

            var result = await client.LoginAsync(username, password, ct).ConfigureAwait(false);

            switch (result.Kind)
            {
                case WebAuthResultKind.Success:
                    CurrentUser = new User
                    {
                        UserId = -1,  // SSO 사용자 — DB 영속 없음, in-memory 만
                        Username = result.Username ?? username,
                        DisplayName = result.DisplayName ?? username,
                        Grade = MapWebRoleToGrade(result.Role),
                        PasswordHash = string.Empty,  // SSO — VMS 가 비밀번호 보관 안 함
                        CreatedAt = DateTime.UtcNow,
                        LastLoginAt = DateTime.UtcNow,
                        IsLocalFallback = false  // Web 인증 — 명시적으로 비폴백 (전체 권한 매핑된 Grade 기준)
                    };
                    AuditLogger.Instance.Log(
                        AuditCategory.Authentication, "UserLogin", AuditOutcome.Success,
                        userName: CurrentUser.Username, source: nameof(UserService),
                        details: $"Web SSO ok, Role={result.Role}, Grade={CurrentUser.Grade}");
                    return true;

                case WebAuthResultKind.InvalidCredentials:
                    AuditLogger.Instance.Log(
                        AuditCategory.Authentication, "UserLogin", AuditOutcome.Denied,
                        userName: username, source: nameof(UserService),
                        details: $"Web SSO rejected: {result.ErrorDetail}");
                    return false;

                case WebAuthResultKind.WebUnreachable:
                    AuditLogger.Instance.Log(
                        AuditCategory.Authentication, "UserLogin", AuditOutcome.Failure,
                        userName: username, source: nameof(UserService),
                        details: $"Web unreachable: {result.ErrorDetail} — caller may attempt local fallback");
                    return false;

                case WebAuthResultKind.ServerError:
                default:
                    AuditLogger.Instance.Log(
                        AuditCategory.Authentication, "UserLogin", AuditOutcome.Failure,
                        userName: username, source: nameof(UserService),
                        details: $"Web server error: {result.ErrorDetail}");
                    return false;
            }
        }

        /// <summary>
        /// Web Role 문자열 → VMS UserGrade 매핑.
        /// - "Admin"   → Admin (전체 권한)
        /// - "Manager" → Engineer (관리자급, 사용자 관리 제외)
        /// - "User"    → Engineer (일반 운영자, 키오스크보다 높음)
        /// - 미지원    → Operator (안전 디폴트, 키오스크 권한만)
        /// 운영 정책 변경 시 본 매퍼만 수정.
        ///
        /// ⚠ MLOps 동기 제약 (2026-09-11): BODA.VMS.MLOps 는 같은 Web JWT 로 로그인하며
        /// Web Role 을 자체 역할로 옮기는 규칙(Core/Auth/WebRoleMapping.cs)을 이 매퍼에
        /// 맞춰 두었다 — Admin→Admin, User→Engineer, Guest→Viewer.
        /// 여기서 Web User 의 등급을 바꾸면(예: Engineer → Operator) 같은 사람이 두 제품에서
        /// 다른 권한 수준을 갖게 되므로 MLOps 쪽 매핑도 반드시 함께 수정할 것.
        /// </summary>
        internal static UserGrade MapWebRoleToGrade(string? webRole) => webRole switch
        {
            "Admin"   => UserGrade.Admin,
            "Manager" => UserGrade.Engineer,
            "User"    => UserGrade.Engineer,
            _         => UserGrade.Operator
        };

        public bool HasPermission(UserPermission permission)
        {
            if (CurrentUser == null)
            {
                // 비로그인 상태 — 모든 권한 거부. 빈도 높아 audit log 폭증 방지 차원에서 미기록.
                return false;
            }

            // SSO Migration §2.3: 비상 폴백 세션은 Grade 무관 제한 권한만 허용.
            // 운영 데이터 변경 (ManageUsers / EditRecipe / SystemConfiguration 등) 거부.
            if (CurrentUser.IsLocalFallback)
            {
                bool fallbackGranted = LocalFallbackAllowed.Contains(permission);
                if (!fallbackGranted)
                {
                    AuditLogger.Instance.Log(
                        AuditCategory.Authorization, "PermissionDenied", AuditOutcome.Denied,
                        userName: CurrentUser.Username, source: nameof(UserService),
                        details: $"Permission={permission}, IsLocalFallback=true — " +
                                 "운영 데이터 변경은 Web SSO 로그인 후 수행하세요");
                }
                return fallbackGranted;
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

        /// <summary>
        /// 초기 admin 계정 시드 — Option C (사용자 결정 2026-06-04): InitializeDatabase 의 디폴트
        /// admin/admin123 시드 제거 후, AppSetup wizard 또는 첫 가동 흐름에서 운영자가 비밀번호 입력해
        /// 명시적으로 시드해야 함.
        ///
        /// 동작:
        /// - admin 이 이미 존재 → false (보존). 기존 install 호환.
        /// - admin 없음 → 입력 비밀번호로 BCrypt 해시 후 시드 + AuditLog 기록. true.
        /// - 비밀번호 최소 길이 8.
        ///
        /// 호출자는 평문 비밀번호 인자를 즉시 폐기 권장 (메모리 transit 최소화).
        /// </summary>
        public bool SeedInitialAdmin(string password, string displayName = "Administrator")
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            try
            {
                CredentialGuard.ValidateSecret(password, nameof(password), minLength: 8);
            }
            catch (ArgumentException ex)
            {
                AuditLogger.Instance.Log(
                    AuditCategory.UserManagement, "SeedInitialAdmin", AuditOutcome.Denied,
                    userName: "admin", source: nameof(UserService),
                    details: $"Invalid input: {ex.Message}");
                return false;
            }

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // 이미 존재하면 보존 — 기존 운영 환경 무영향
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = 'admin' COLLATE NOCASE";
            if ((long)checkCmd.ExecuteScalar()! > 0)
            {
                return false;
            }

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, DisplayName, Grade, CreatedAt)
                VALUES ('admin', @hash, @display, @grade, @created)";
            insertCmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(password));
            insertCmd.Parameters.AddWithValue("@display",
                string.IsNullOrWhiteSpace(displayName) ? "Administrator" : displayName.Trim());
            insertCmd.Parameters.AddWithValue("@grade", (int)UserGrade.Admin);
            insertCmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
            insertCmd.ExecuteNonQuery();

            AuditLogger.Instance.Log(
                AuditCategory.UserManagement, "SeedInitialAdmin", AuditOutcome.Success,
                userName: "admin", source: nameof(UserService),
                details: "Initial admin account seeded via AppSetup or first-launch flow");
            return true;
        }

        /// <summary>
        /// 비상 local-admin 의 비밀번호를 변경 (SSO Migration §3 — AppSetup wizard 사용).
        /// 디폴트 시드 비밀번호 (LocalFallbackDefaultPassword) 를 운영 환경에서 변경하는 유일한 경로.
        /// AppSetup wizard 가 SaveConfiguration 시점에 호출 — 빈 값 전달시 무동작.
        ///
        /// 보안:
        /// - newPassword 는 BCrypt 해시로만 DB 저장 (평문 보관 X)
        /// - 최소 길이 8 (LocalFallbackDefaultPassword 보다 강한 정책)
        /// - 호출자는 평문을 메모리에서 즉시 폐기 권장
        /// </summary>
        public bool SetLocalFallbackPassword(string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword)) return false;
            try
            {
                CredentialGuard.ValidateSecret(newPassword, nameof(newPassword), minLength: 8);
            }
            catch (ArgumentException ex)
            {
                AuditLogger.Instance.Log(
                    AuditCategory.UserManagement, "SetLocalFallbackPassword", AuditOutcome.Denied,
                    userName: LocalFallbackUsername, source: nameof(UserService),
                    details: $"Invalid input: {ex.Message}");
                return false;
            }

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Users SET PasswordHash = @hash
                WHERE Username = @username COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@hash", BCrypt.Net.BCrypt.HashPassword(newPassword));
            cmd.Parameters.AddWithValue("@username", LocalFallbackUsername);
            var affected = cmd.ExecuteNonQuery();

            if (affected == 0)
            {
                AuditLogger.Instance.Log(
                    AuditCategory.UserManagement, "SetLocalFallbackPassword", AuditOutcome.Failure,
                    userName: LocalFallbackUsername, source: nameof(UserService),
                    details: "local-admin row not found — InitializeDatabase 시드가 실패했을 가능성");
                return false;
            }

            AuditLogger.Instance.Log(
                AuditCategory.UserManagement, "SetLocalFallbackPassword", AuditOutcome.Success,
                userName: LocalFallbackUsername, source: nameof(UserService),
                details: "local-admin password updated via AppSetup wizard");
            return true;
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
