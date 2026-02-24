using VMS.AppSetup.Models;

namespace VMS.AppSetup.Interfaces
{
    public interface IConfigurationService
    {
        string ConfigFilePath { get; }
        bool SaveConfiguration(SetupConfiguration config);
        SetupConfiguration? LoadConfiguration();
        bool ConfigurationExists();
        bool ExportConfiguration(SetupConfiguration config, string exportPath);
    }
}
