using VMS.Models;
using VMS.PLC.Models;

namespace VMS.Interfaces
{
    public interface IConfigurationService
    {
        string ConfigDirectory { get; }
        SystemConfiguration LoadSystemConfiguration();
        LayoutConfiguration LoadLayoutConfiguration();
        bool SaveLayoutConfiguration(LayoutConfiguration config);
        bool SaveSystemConfiguration(SystemConfiguration config);
        PlcSignalConfiguration LoadPlcSignalConfiguration();
        bool SavePlcSignalConfiguration(PlcSignalConfiguration config);
    }
}
