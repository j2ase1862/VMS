namespace VMS.Core.Interfaces
{
    /// <summary>
    /// 라벨링 앱용 다이얼로그 서비스 인터페이스.
    /// VMS.VisionSetup의 IDialogService에서 라벨링에 필요한 부분만 분리.
    /// </summary>
    public interface ILabelingDialogService
    {
        void ShowInformation(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);
        bool ShowConfirmation(string message, string title);
        string? ShowOpenFileDialog(string title, string filter);
        string[]? ShowOpenFilesDialog(string title, string filter);
        string? ShowSaveFileDialog(string filter, string defaultExt, string fileName = "");
        string? ShowFolderDialog(string title);
    }
}
