using System.Windows;
using Microsoft.Win32;
using VMS.Core.Interfaces;

namespace VMS.Labeling.Services
{
    public class LabelingDialogService : ILabelingDialogService
    {
        public void ShowInformation(string message, string title)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowWarning(string message, string title)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public void ShowError(string message, string title)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public bool ShowConfirmation(string message, string title)
            => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

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

        public string? ShowFolderDialog(string title)
        {
            var dialog = new OpenFolderDialog { Title = title };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
