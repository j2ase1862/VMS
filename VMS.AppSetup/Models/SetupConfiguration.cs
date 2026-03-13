using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

        // Camera Type
        [ObservableProperty]
        private CameraType _cameraType = CameraType.AreaScan2D;

        // Area Scan parameters
        [ObservableProperty]
        private double _exposure = 5000;

        [ObservableProperty]
        private double _gain = 1.0;

        // Line Scan parameters
        [ObservableProperty]
        private TriggerSource _triggerSource = TriggerSource.Internal;

        [ObservableProperty]
        private double _lineRate = 10000;

        [ObservableProperty]
        private double _encoderResolution = 10.0;

        [ObservableProperty]
        private int _scanLength = 4096;

        // 3D Camera parameters
        [ObservableProperty]
        private CaptureMode3D _captureMode = CaptureMode3D.Both;

        [ObservableProperty]
        private int _filterStrength = 3;

        [ObservableProperty]
        private double _zRangeMin = 0;

        [ObservableProperty]
        private double _zRangeMax = 1000;

        // Dynamic UI visibility properties
        [JsonIgnore]
        public bool IsAreaScan => CameraType == CameraType.AreaScan2D || CameraType == CameraType.AreaScan3D;

        [JsonIgnore]
        public bool IsLineScan => CameraType == CameraType.LineScan2D || CameraType == CameraType.LineScan3D;

        [JsonIgnore]
        public bool Is3DCamera => CameraType == CameraType.AreaScan3D || CameraType == CameraType.LineScan3D;

        [JsonIgnore]
        public bool ShowEncoderResolution => IsLineScan && TriggerSource == TriggerSource.Encoder;

        [JsonIgnore]
        public string DisplayInfo => $"{Manufacturer} - {IpAddress}";

        partial void OnCameraTypeChanged(CameraType value)
        {
            OnPropertyChanged(nameof(IsAreaScan));
            OnPropertyChanged(nameof(IsLineScan));
            OnPropertyChanged(nameof(Is3DCamera));
            OnPropertyChanged(nameof(ShowEncoderResolution));
        }

        partial void OnTriggerSourceChanged(TriggerSource value)
        {
            OnPropertyChanged(nameof(ShowEncoderResolution));
        }
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
