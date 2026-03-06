namespace VMS.PLC.Models
{
    /// <summary>
    /// PLC connection configuration DTO
    /// </summary>
    public class PlcConnectionConfig
    {
        public PlcVendor Vendor { get; set; } = PlcVendor.None;
        public PlcCommunicationType CommunicationType { get; set; } = PlcCommunicationType.Ethernet;
        public string IpAddress { get; set; } = "192.168.0.100";
        public int Port { get; set; }
        public int ConnectTimeoutMs { get; set; } = 3000;
        public int ReadTimeoutMs { get; set; } = 1000;
        public int WriteTimeoutMs { get; set; } = 1000;

        // Mitsubishi MC Protocol specific
        public byte NetworkNumber { get; set; } = 0x00;
        public byte StationNumber { get; set; } = 0xFF;

        // Siemens S7 specific
        public int Rack { get; set; } = 0;
        public int Slot { get; set; } = 1;

        // LS XGT specific
        public ushort InvokeId { get; set; } = 0x0000;

        // Omron FINS specific
        public byte SourceNode { get; set; } = 0x00;
        public byte DestNode { get; set; } = 0x00;

        // Modbus specific
        public byte UnitId { get; set; } = 255;

        // Serial communication (active when CommunicationType == Serial)
        public string SerialPortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 115200;
        public int DataBits { get; set; } = 8;
        public PlcSerialParity Parity { get; set; } = PlcSerialParity.None;
        public PlcSerialStopBits StopBits { get; set; } = PlcSerialStopBits.One;

        // Performance & stability
        public int PollingIntervalMs { get; set; } = 20;
        public bool UseHeartbeat { get; set; }
        public string HeartbeatAddress { get; set; } = string.Empty;
        public bool AutoReconnect { get; set; } = true;

        // Data synchronization
        public PlcWriteMode WriteMode { get; set; } = PlcWriteMode.Handshake;
        public PlcEndianMode EndianMode { get; set; } = PlcEndianMode.LittleEndian;
    }
}
