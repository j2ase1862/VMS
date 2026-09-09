using System.Windows;
using Microsoft.Win32;
using VMS.Core.Interfaces;

namespace VMS.DeepLearning.Services
{
    public class LabelingDialogService : ILabelingDialogService
    {
        // 모든 메시지는 공용 다크 MessageDialog 로 통일 (WPF 기본 흰 MessageBox 대체, Phase B 2026-08-25)

        public void ShowInformation(string message, string title)
            => VMS.VisionSetup.Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                VMS.VisionSetup.Views.Common.MessageDialogKind.Info);

        public void ShowWarning(string message, string title)
            => VMS.VisionSetup.Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                VMS.VisionSetup.Views.Common.MessageDialogKind.Warning);

        public void ShowError(string message, string title)
            => VMS.VisionSetup.Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                VMS.VisionSetup.Views.Common.MessageDialogKind.Error);

        public bool ShowConfirmation(string message, string title)
            => VMS.VisionSetup.Views.Common.MessageDialog.Show(Application.Current.MainWindow, message, title,
                VMS.VisionSetup.Views.Common.MessageDialogKind.Question, isConfirmation: true);

        public string? ShowOpenFileDialog(string title, string filter)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string[]? ShowOpenFilesDialog(string title, string filter)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
            return dialog.ShowDialog() == true ? dialog.FileNames : null;
        }

        public string? ShowSaveFileDialog(string filter, string defaultExt, string fileName = "")
        {
            var dialog = new SaveFileDialog { Filter = filter, DefaultExt = defaultExt, FileName = fileName };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public void ShowModelUpload(VMS.Core.ViewModels.ModelUploadViewModel viewModel)
        {
            var owner = Application.Current?.MainWindow;
            var window = new Views.ModelUploadWindow(viewModel);
            if (owner is not null && owner.IsLoaded) window.Owner = owner;
            window.ShowDialog();
        }

        public void ShowDatasetDownload(VMS.Core.ViewModels.DatasetDownloadViewModel viewModel)
        {
            var owner = Application.Current?.MainWindow;
            var window = new Views.DatasetDownloadWindow(viewModel);
            if (owner is not null && owner.IsLoaded) window.Owner = owner;
            window.ShowDialog();
        }

        public string? ShowFolderDialog(string title)
        {
            var dialog = new OpenFolderDialog { Title = title };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
