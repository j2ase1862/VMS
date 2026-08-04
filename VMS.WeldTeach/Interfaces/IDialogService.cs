namespace VMS.WeldTeach.Interfaces;

public interface IDialogService
{
    string? ShowOpenStepDialog();
    string? ShowOpenCloudDialog();
    string? ShowSaveCloudDialog(string defaultName);
    string? ShowSaveJsonDialog(string defaultName);
    /// <summary>핸드-아이 행렬(T_cam2base) 파일 열기 — 4×4 행 우선 JSON/텍스트.</summary>
    string? ShowOpenMatrixDialog();
    void ShowMessage(string message, string title);
}
