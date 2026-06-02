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

        // ─── IO 보드 (Phase 1) — PLC 와 동시 사용 가능한 디지털 IO 디바이스 목록 ───
        // ADLink PCI-743x / Advantech PCI-17xx 등. SequenceNodeConfig.DeviceId 가
        // 이 리스트의 DeviceId 와 매칭. 빈 리스트면 기본 PLC 만 사용 (후방호환).
        public List<IoDeviceConfig> IoBoards { get; set; } = new();

        // Web Parameter Sync
        public string WebServerUrl { get; set; } = "http://localhost:5292";
        public string VisionServerUrl { get; set; } = "http://localhost:5000";
        public int ClientIndex { get; set; } = 1;

        /// <summary>
        /// BODA.VMS.Web 머신 endpoint(heartbeat/register/disconnect/검사결과/센서) 호출시
        /// X-API-Key 헤더로 송신할 비밀. 빈 문자열이면 헤더 미송신 (서버가 호환 모드일 때만 통과).
        /// 운영 환경에서는 Web 서버의 ClientApiKey:Value 와 동일하게 설정.
        /// </summary>
        public string ClientApiKey { get; set; } = string.Empty;

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
        public CameraType CameraType { get; set; } = CameraType.AreaScan2D;

        // Area Scan parameters
        public double Exposure { get; set; } = 5000;
        public double Gain { get; set; } = 1.0;

        // Line Scan parameters
        public TriggerSource TriggerSource { get; set; } = TriggerSource.Internal;
        public double LineRate { get; set; } = 10000;
        public double EncoderResolution { get; set; } = 10.0;
        public int ScanLength { get; set; } = 4096;

        // 3D Camera parameters
        public CaptureMode3D CaptureMode { get; set; } = CaptureMode3D.Both;
        public int FilterStrength { get; set; } = 3;
        public double ZRangeMin { get; set; } = 0;
        public double ZRangeMax { get; set; } = 1000;

        // Frame Grabber (Matrox/Dalsa) parameters
        public string BoardType { get; set; } = "SOLIOS";
        public int BoardNumber { get; set; }
        public int DigitizerNumber { get; set; }
        public string DcfFilePath { get; set; } = string.Empty;

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
