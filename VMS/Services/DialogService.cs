using System.Windows;
using Microsoft.Win32;
using VMS.Interfaces;

namespace VMS.Services
{
    public class DialogService : IDialogService
    {
        public void ShowInformation(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool ShowConfirmation(string message, string title)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
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
    }
}
