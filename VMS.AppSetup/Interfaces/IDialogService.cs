namespace VMS.AppSetup.Interfaces
{
    public interface IDialogService
    {
        void ShowInformation(string message, string title);
        void ShowError(string message, string title);
        bool ShowConfirmation(string message, string title);

        /// <summary>파일 열기 다이얼로그 — 선택 취소 시 null.</summary>
        string? ShowOpenFileDialog(string title, string filter);
    }
}
