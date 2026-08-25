using System.Windows;
using Microsoft.Win32;
using VMS.Interfaces;
using VMS.ViewModels;
using VMS.Views;
using VMS.VisionSetup.Views.Common;

namespace VMS.Services
{
    public class DialogService : IDialogService
    {
        // 모든 메시지 다이얼로그는 자체 다크 MessageDialog 로 통일 (WPF 기본 흰 MessageBox 대체).
        // Owner 는 Application.Current.MainWindow — 메인 윈도우 미생성 시점(앱 시작 직후)에는 null 허용.

        public void ShowInformation(string message, string title)
        {
            MessageDialog.Show(Application.Current.MainWindow, message, title, MessageDialogKind.Info);
        }

        public void ShowWarning(string message, string title)
        {
            MessageDialog.Show(Application.Current.MainWindow, message, title, MessageDialogKind.Warning);
        }

        public void ShowError(string message, string title)
        {
            MessageDialog.Show(Application.Current.MainWindow, message, title, MessageDialogKind.Error);
        }

        public bool ShowConfirmation(string message, string title)
        {
            return MessageDialog.Show(
                Application.Current.MainWindow, message, title,
                MessageDialogKind.Question, isConfirmation: true);
        }

        public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt
            };

            if (fileName != null)
                dialog.FileName = fileName;

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? ShowOpenFileDialog(string filter, string defaultExt)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public bool ShowLoginDialog(IUserService userService)
        {
            var vm = new LoginViewModel(userService);
            var window = new LoginWindow
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            return window.ShowDialog() == true;
        }
    }
}
