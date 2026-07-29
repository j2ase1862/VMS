namespace VMS.WeldTeach.Interfaces;

public interface IDialogService
{
    string? ShowOpenStepDialog();
    string? ShowOpenCloudDialog();
    string? ShowSaveJsonDialog(string defaultName);
    void ShowMessage(string message, string title);
}
