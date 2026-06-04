using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using VMS.Models;

namespace VMS.Interfaces
{
    public interface IUserService
    {
        User? CurrentUser { get; }
        bool IsLoggedIn { get; }

        bool Authenticate(string username, string password);

        /// <summary>
        /// BODA.VMS.Web 의 /api/auth/login 으로 SSO 위임. Web 응답이 Success 면 CurrentUser 를
        /// in-memory User 로 set (DB 영속 없음). 실패 분기는 AuditLog 기록 + false 반환 — 호출자가
        /// 비상 로컬 폴백 여부 판단 (SSO Migration Plan §2.2). (SSO PR2)
        /// </summary>
        Task<bool> AuthenticateViaWebAsync(string username, string password,
            WebAuthClient client, CancellationToken ct = default);

        void Logout();
        bool HasPermission(UserPermission permission);

        /// <summary>
        /// 비상 local-admin 비밀번호 변경 — AppSetup wizard 가 호출 (SSO PR4).
        /// 빈 값이면 무동작. BCrypt 해시로만 저장.
        /// </summary>
        bool SetLocalFallbackPassword(string newPassword);

        bool CreateUser(string username, string password, string displayName, UserGrade grade);
        bool UpdateUser(int userId, string displayName, UserGrade grade);
        bool ChangePassword(int userId, string newPassword);
        bool DeleteUser(int userId);
        List<User> GetAllUsers();
    }
}
