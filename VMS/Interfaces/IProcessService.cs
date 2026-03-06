namespace VMS.Interfaces
{
    public interface IProcessService
    {
        void LaunchProcess(string fileName, string? arguments = null);
    }
}
