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

        /// <summary>
        /// 학습 결과를 MLOps 레지스트리에 올리는 창을 띄운다.
        ///
        /// <para>
        /// 뷰모델은 이미 만들어져 들어온다 — 화면 계층이 하는 일은 창을 붙여 주는 것뿐이고,
        /// 로그인·업로드는 전부 <see cref="ViewModels.ModelUploadViewModel"/> 안에서 일어난다.
        /// 창을 띄울 수 없는 환경(테스트 등)에서는 아무 일도 하지 않으면 된다.
        /// </para>
        /// </summary>
        void ShowModelUpload(ViewModels.ModelUploadViewModel viewModel);
    }
}
