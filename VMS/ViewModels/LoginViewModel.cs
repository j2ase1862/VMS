using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VMS.Interfaces;

namespace VMS.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IUserService _userService;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isAuthenticated;

        public string Password { get; set; } = string.Empty;

        public LoginViewModel(IUserService userService)
        {
            _userService = userService;
        }

        [RelayCommand]
        private void Login()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = "Username을 입력하세요.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Password를 입력하세요.";
                return;
            }

            if (_userService.Authenticate(Username, Password))
            {
                IsAuthenticated = true;
            }
            else
            {
                ErrorMessage = "사용자명 또는 비밀번호가 올바르지 않습니다.";
            }
        }
    }
}
