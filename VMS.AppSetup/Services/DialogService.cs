using System.Windows;
using VMS.AppSetup.Interfaces;

namespace VMS.AppSetup.Services
{
    public class DialogService : IDialogService
    {
        // 모든 메시지는 공용 다크 MessageDialog 로 통일 (WPF 기본 흰 MessageBox 대체, 2026-08-25)

        public void ShowInformation(string message, string title)
        {
            VMS.VisionSetup.Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                VMS.VisionSetup.Views.Common.MessageDialogKind.Info);
        }

        public void ShowError(string message, string title)
        {
            VMS.VisionSetup.Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                VMS.VisionSetup.Views.Common.MessageDialogKind.Error);
        }

        public bool ShowConfirmation(string message, string title)
        {
            return VMS.VisionSetup.Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                VMS.VisionSetup.Views.Common.MessageDialogKind.Question, isConfirmation: true);
        }

        public string? ShowOpenFileDialog(string title, string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
