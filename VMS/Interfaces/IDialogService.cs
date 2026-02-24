namespace VMS.Interfaces
{
    public interface IDialogService
    {
        void ShowInformation(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);
        bool ShowConfirmation(string message, string title);
        string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null);
        string? ShowOpenFileDialog(string filter, string defaultExt);
    }
}
