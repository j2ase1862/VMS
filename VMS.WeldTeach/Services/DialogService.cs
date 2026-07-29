using System.Windows;
using Microsoft.Win32;
using VMS.WeldTeach.Interfaces;

namespace VMS.WeldTeach.Services;

public class DialogService : IDialogService
{
    public string? ShowOpenStepDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "STEP 파일 열기",
            Filter = "STEP 파일 (*.step;*.stp)|*.step;*.stp|모든 파일 (*.*)|*.*",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? ShowOpenCloudDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "점군 파일 열기",
            Filter = "점군 파일 (*.ply;*.xyz;*.txt;*.csv)|*.ply;*.xyz;*.txt;*.csv|모든 파일 (*.*)|*.*",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? ShowSaveJsonDialog(string defaultName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "로봇 경로 내보내기",
            FileName = defaultName,
            Filter = "JSON 파일 (*.json)|*.json",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public void ShowMessage(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
