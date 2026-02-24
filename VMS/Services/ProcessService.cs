using System.Diagnostics;
using VMS.Interfaces;

namespace VMS.Services
{
    public class ProcessService : IProcessService
    {
        public void LaunchProcess(string fileName, string? arguments = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true
            };

            if (!string.IsNullOrEmpty(arguments))
            {
                startInfo.Arguments = arguments;
            }

            Process.Start(startInfo);
        }
    }
}
