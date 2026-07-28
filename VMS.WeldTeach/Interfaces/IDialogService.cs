namespace VMS.WeldTeach.Interfaces;

public interface IDialogService
{
    string? ShowOpenStepDialog();
    string? ShowSaveJsonDialog(string defaultName);
    void ShowMessage(string message, string title);
}
