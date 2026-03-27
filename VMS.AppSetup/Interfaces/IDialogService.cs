namespace VMS.AppSetup.Interfaces
{
    public interface IDialogService
    {
        void ShowInformation(string message, string title);
        void ShowError(string message, string title);
        bool ShowConfirmation(string message, string title);
    }
}
