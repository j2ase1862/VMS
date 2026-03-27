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
        /// When resilient is true, wraps the connection with ResilientPlcConnection
        /// for automatic reconnection, keep-alive, and structured logging.
        /// </summary>
        public static IPlcConnection Create(PlcConnectionConfig config, bool resilient = true)
        {
            IPlcConnection inner = config.Vendor switch
            {
                PlcVendor.Mitsubishi => new MitsubishiMcConnection(),
                PlcVendor.Siemens => new SiemensS7Connection(),
                PlcVendor.LS => new LsXgtConnection(),
                PlcVendor.Omron => new OmronFinsConnection(),
                // Modbus TCP: use SimulatedPlcConnection until ModbusTcpConnection is implemented
                PlcVendor.Modbus => new SimulatedPlcConnection(),
                _ => new SimulatedPlcConnection()
            };

            if (!resilient) return inner;

            return new ResilientPlcConnection(inner);
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
            PlcVendor.Modbus => 502,
            _ => 0
        };
    }
}
