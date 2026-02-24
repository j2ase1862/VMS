using VMS.VisionSetup.Models;


namespace VMS.VisionSetup.Interfaces
{
    public interface IDialogService
    {
        void ShowInformation(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);
        bool ShowConfirmation(string message, string title);
        string? ShowOpenFileDialog(string title, string filter);
        string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null);
        string? ShowRenameDialog(string currentName);
        void ShowCameraManagerDialog();
        Recipe? ShowRecipeManagerDialog();
    }
}
