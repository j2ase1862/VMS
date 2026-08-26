using System.Windows;
using Microsoft.Win32;
using VMS.VisionSetup.Views.Common;

namespace Boda.LicGen.App.Services
{
    public sealed class DialogService : IDialogService
    {
        private static Window? Owner => Application.Current?.MainWindow;

        public void ShowInformation(string message, string title = "알림") =>
            MessageDialog.Show(Owner, message, title, MessageDialogKind.Info);

        public void ShowWarning(string message, string title = "경고") =>
            MessageDialog.Show(Owner, message, title, MessageDialogKind.Warning);

        public void ShowError(string message, string title = "오류") =>
            MessageDialog.Show(Owner, message, title, MessageDialogKind.Error);

        public bool ShowConfirmation(string message, string title = "확인") =>
            MessageDialog.Show(Owner, message, title, MessageDialogKind.Question, isConfirmation: true);

        public string? ShowSaveLicenseDialog(string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Title = "라이선스 파일 저장",
                FileName = defaultFileName,
                Filter = "라이선스 파일 (*.lic)|*.lic|모든 파일 (*.*)|*.*",
                DefaultExt = ".lic"
            };
            return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
        }

        public string? ShowSelectBackupFolderDialog()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "백업 대상 선택 (USB 드라이브 루트 권장)"
            };
            return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
        }
    }
}
