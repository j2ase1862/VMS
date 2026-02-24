using VMS.Models;

namespace VMS.Interfaces
{
    public interface IConfigurationService
    {
        string ConfigDirectory { get; }
        SystemConfiguration LoadSystemConfiguration();
        LayoutConfiguration LoadLayoutConfiguration();
        bool SaveLayoutConfiguration(LayoutConfiguration config);
        bool SaveSystemConfiguration(SystemConfiguration config);
    }
}
