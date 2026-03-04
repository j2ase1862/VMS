using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using VMS.Interfaces;
using VMS.Models;

namespace VMS.ViewModels
{
    public partial class UserManagementViewModel : ObservableObject
    {
        private readonly IUserService _userService;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private ObservableCollection<User> _users = new();

        [ObservableProperty]
        private User? _selectedUser;

        // New user form fields
        [ObservableProperty]
        private string _newUsername = string.Empty;

        [ObservableProperty]
        private string _newDisplayName = string.Empty;

        [ObservableProperty]
        private UserGrade _newGrade = UserGrade.Operator;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public string NewPassword { get; set; } = string.Empty;
        public string ChangePasswordValue { get; set; } = string.Empty;

        public Array GradeValues => Enum.GetValues(typeof(UserGrade));

        public UserManagementViewModel(IUserService userService, IDialogService dialogService)
        {
            _userService = userService;
            _dialogService = dialogService;
            RefreshUsers();
        }

        private void RefreshUsers()
        {
            Users.Clear();
            foreach (var user in _userService.GetAllUsers())
            {
                Users.Add(user);
            }
        }

        [RelayCommand]
        private void AddUser()
        {
            if (string.IsNullOrWhiteSpace(NewUsername))
            {
                StatusMessage = "사용자명을 입력하세요.";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                StatusMessage = "비밀번호를 입력하세요.";
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(NewDisplayName)
                ? NewUsername
                : NewDisplayName;

            if (_userService.CreateUser(NewUsername, NewPassword, displayName, NewGrade))
            {
                StatusMessage = $"사용자 '{NewUsername}'이(가) 추가되었습니다.";
                NewUsername = string.Empty;
                NewDisplayName = string.Empty;
                NewPassword = string.Empty;
                NewGrade = UserGrade.Operator;
                RefreshUsers();
            }
            else
            {
                StatusMessage = "사용자 추가에 실패했습니다. (중복된 사용자명일 수 있습니다)";
            }
        }

        [RelayCommand]
        private void SaveUser()
        {
            if (SelectedUser == null) return;

            if (_userService.UpdateUser(SelectedUser.UserId, SelectedUser.DisplayName, SelectedUser.Grade))
            {
                StatusMessage = $"사용자 '{SelectedUser.Username}' 정보가 저장되었습니다.";
                RefreshUsers();
            }
            else
            {
                StatusMessage = "사용자 정보 저장에 실패했습니다.";
            }
        }

        [RelayCommand]
        private void ChangePassword()
        {
            if (SelectedUser == null)
            {
                StatusMessage = "사용자를 선택하세요.";
                return;
            }

            if (string.IsNullOrWhiteSpace(ChangePasswordValue))
            {
                StatusMessage = "새 비밀번호를 입력하세요.";
                return;
            }

            if (_userService.ChangePassword(SelectedUser.UserId, ChangePasswordValue))
            {
                StatusMessage = $"'{SelectedUser.Username}'의 비밀번호가 변경되었습니다.";
                ChangePasswordValue = string.Empty;
            }
            else
            {
                StatusMessage = "비밀번호 변경에 실패했습니다.";
            }
        }

        [RelayCommand]
        private void DeleteUser()
        {
            if (SelectedUser == null) return;

            if (!_dialogService.ShowConfirmation(
                $"사용자 '{SelectedUser.Username}'을(를) 삭제하시겠습니까?",
                "Delete User"))
                return;

            if (_userService.DeleteUser(SelectedUser.UserId))
            {
                StatusMessage = $"사용자 '{SelectedUser.Username}'이(가) 삭제되었습니다.";
                SelectedUser = null;
                RefreshUsers();
            }
            else
            {
                StatusMessage = "사용자 삭제에 실패했습니다. (현재 로그인된 계정은 삭제할 수 없습니다)";
            }
        }
    }
}
