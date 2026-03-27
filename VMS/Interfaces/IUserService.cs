using VMS.Models;

namespace VMS.Interfaces
{
    public interface IUserService
    {
        User? CurrentUser { get; }
        bool IsLoggedIn { get; }

        bool Authenticate(string username, string password);
        void Logout();
        bool HasPermission(UserPermission permission);

        bool CreateUser(string username, string password, string displayName, UserGrade grade);
        bool UpdateUser(int userId, string displayName, UserGrade grade);
        bool ChangePassword(int userId, string newPassword);
        bool DeleteUser(int userId);
        List<User> GetAllUsers();
    }
}
