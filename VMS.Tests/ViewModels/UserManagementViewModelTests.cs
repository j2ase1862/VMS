using System;
using System.IO;
using System.Linq;
using VMS.Interfaces;
using VMS.Models;
using VMS.Services;
using VMS.ViewModels;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// VM-25 — [Save Changes] 뒤에도 고른 사용자가 그대로 골라져 있어야
    /// 이어서 [Change Password] · [Delete User] 를 누를 수 있다.
    /// </summary>
    public class UserManagementViewModelTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"usermgmt_vm_{Guid.NewGuid():N}");
        private readonly UserService _service;

        public UserManagementViewModelTests()
        {
            _service = new UserService(_tempDir);
            _service.SeedInitialAdmin("admin123", "Administrator");
            _service.CreateUser("op1", "pw123456", "Operator One", UserGrade.Operator);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* SQLite 핸들이 남아 있을 수 있다 — 다른 테스트 영향 없음 */ }
        }

        [Fact]
        public void SaveUser_KeepsSelection()
        {
            var vm = new UserManagementViewModel(_service, new NoopDialogService());
            vm.SelectedUser = vm.Users.Single(u => u.Username == "op1");
            vm.SelectedUser.DisplayName = "Operator Renamed";

            vm.SaveUserCommand.Execute(null);

            Assert.NotNull(vm.SelectedUser);
            Assert.Equal("op1", vm.SelectedUser!.Username);
            Assert.Equal("Operator Renamed", vm.SelectedUser.DisplayName);
            Assert.Contains(vm.SelectedUser, vm.Users);
        }

        private sealed class NoopDialogService : IDialogService
        {
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null) => null;
            public string? ShowOpenFileDialog(string filter, string defaultExt) => null;
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public bool ShowLoginDialog(IUserService userService) => false;
        }
    }
}
