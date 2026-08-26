namespace Boda.LicGen.App.Services
{
    /// <summary>다이얼로그 추상화 — MessageBox/파일 대화상자는 구현체에만 존재 (솔루션 관례).</summary>
    public interface IDialogService
    {
        void ShowInformation(string message, string title = "알림");
        void ShowWarning(string message, string title = "경고");
        void ShowError(string message, string title = "오류");
        bool ShowConfirmation(string message, string title = "확인");

        /// <summary>라이선스 파일 저장 위치 선택 — 취소 시 null.</summary>
        string? ShowSaveLicenseDialog(string defaultFileName);

        /// <summary>백업 대상 폴더(USB 루트) 선택 — 취소 시 null.</summary>
        string? ShowSelectBackupFolderDialog();
    }
}
