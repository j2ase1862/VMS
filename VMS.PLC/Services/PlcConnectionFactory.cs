using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// Factory for creating vendor-specific PLC connection instances.
    /// </summary>
    public static class PlcConnectionFactory
    {
        /// <summary>
        /// Create an IPlcConnection instance based on vendor configuration.
        /// </summary>
        public static IPlcConnection Create(PlcConnectionConfig config)
        {
            return config.Vendor switch
            {
                PlcVendor.Mitsubishi => new MitsubishiMcConnection(),
                PlcVendor.Siemens => new SiemensS7Connection(),
                PlcVendor.LS => new LsXgtConnection(),
                PlcVendor.Omron => new OmronFinsConnection(),
                _ => new SimulatedPlcConnection()
            };
        }

        /// <summary>
        /// Get the default TCP port for a given PLC vendor.
        /// </summary>
        public static int GetDefaultPort(PlcVendor vendor) => vendor switch
        {
            PlcVendor.Mitsubishi => 5000,
            PlcVendor.Siemens => 102,
            PlcVendor.LS => 2004,
            PlcVendor.Omron => 9600,
            _ => 0
        };
    }
}
