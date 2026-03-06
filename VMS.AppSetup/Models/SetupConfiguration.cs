using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using VMS.Camera.Models;
using VMS.PLC.Models;

namespace VMS.AppSetup.Models
{
    /// <summary>
    /// 머신 비전 시스템 설정 구성
    /// </summary>
    public class SetupConfiguration
    {
        // Page 2: Application Settings
        public string ApplicationName { get; set; } = "BODA Vision System";
        public string SystemIpAddress { get; set; } = "192.168.0.1";

        // Page 3: Camera Mode
        public CameraMode CameraMode { get; set; } = CameraMode.Virtual;
        public List<CameraConfiguration> Cameras { get; set; } = new();

        // Page 4: PLC Settings — Vendor & Communication
        public PlcVendor PlcVendor { get; set; } = PlcVendor.None;
        public PlcCommunicationType CommunicationType { get; set; } = PlcCommunicationType.Ethernet;
        public string PlcIpAddress { get; set; } = "192.168.0.100";
        public int PlcPort { get; set; } = 502;

        // Page 4: Modbus
        public byte ModbusUnitId { get; set; } = 255;

        // Page 4: Serial
        public string SerialPortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 115200;
        public int DataBits { get; set; } = 8;
        public PlcSerialParity Parity { get; set; } = PlcSerialParity.None;
        public PlcSerialStopBits StopBits { get; set; } = PlcSerialStopBits.One;

        // Page 4: Performance & Stability
        public int PollingIntervalMs { get; set; } = 20;
        public bool UseHeartbeat { get; set; }
        public string HeartbeatAddress { get; set; } = string.Empty;
        public bool AutoReconnect { get; set; } = true;

        // Page 4: Data Synchronization
        public PlcWriteMode WriteMode { get; set; } = PlcWriteMode.Handshake;
        public PlcEndianMode EndianMode { get; set; } = PlcEndianMode.LittleEndian;

        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "1.0.0";
    }

    /// <summary>
    /// 카메라 설정
    /// </summary>
    public partial class CameraConfiguration : ObservableObject
    {
        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        private CameraManufacturer _manufacturer = CameraManufacturer.Other;

        [JsonIgnore]
        public string DisplayInfo => $"{Manufacturer} - {IpAddress}";
    }

    /// <summary>
    /// 카메라 모드
    /// </summary>
    public enum CameraMode
    {
        Live,   // 실제 연결된 카메라 표시
        Virtual // 가상 카메라 설정
    }

}
