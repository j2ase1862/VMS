using System;
using System.Collections.Generic;
using VMS.Camera.Models;
using VMS.PLC.Models;

namespace VMS.Models
{
    /// <summary>
    /// Configuration loaded from BODA.Setup project
    /// </summary>
    public class SystemConfiguration
    {
        public string ApplicationName { get; set; } = "BODA Vision System";
        public string SystemIpAddress { get; set; } = "192.168.0.1";
        public CameraMode CameraMode { get; set; } = CameraMode.Virtual;
        public List<CameraConfiguration> Cameras { get; set; } = new();
        public PlcVendor PlcVendor { get; set; } = PlcVendor.None;
        public PlcCommunicationType CommunicationType { get; set; } = PlcCommunicationType.Ethernet;
        public string PlcIpAddress { get; set; } = "192.168.0.100";
        public int PlcPort { get; set; } = 502;

        // Modbus
        public byte ModbusUnitId { get; set; } = 255;

        // Serial
        public string SerialPortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 115200;
        public int DataBits { get; set; } = 8;
        public PlcSerialParity Parity { get; set; } = PlcSerialParity.None;
        public PlcSerialStopBits StopBits { get; set; } = PlcSerialStopBits.One;

        // Performance & Stability
        public int PollingIntervalMs { get; set; } = 20;
        public bool UseHeartbeat { get; set; }
        public string HeartbeatAddress { get; set; } = string.Empty;
        public bool AutoReconnect { get; set; } = true;

        // Data Synchronization
        public PlcWriteMode WriteMode { get; set; } = PlcWriteMode.Handshake;
        public PlcEndianMode EndianMode { get; set; } = PlcEndianMode.LittleEndian;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "1.0.0";
    }

    public class CameraConfiguration
    {
        public bool IsEnabled { get; set; } = true;
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public CameraManufacturer Manufacturer { get; set; } = CameraManufacturer.Other;
        public int StepCount { get; set; } = 1;
        public List<StepConfiguration> Steps { get; set; } = new();
    }

    /// <summary>
    /// Step configuration - each step represents a robot position with camera settings
    /// </summary>
    public class StepConfiguration
    {
        public int StepNumber { get; set; } = 1;
        public string Name { get; set; } = "Step 1";
        public double Exposure { get; set; } = 5000;  // microseconds
        public double Gain { get; set; } = 1.0;
    }

    public enum CameraMode
    {
        Live,
        Virtual
    }

}
