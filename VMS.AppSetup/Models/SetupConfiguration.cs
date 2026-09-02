using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using VMS.Camera.Models;
using VMS.PLC.Models;
using RobotProtocolMode = VMS.Camera.Models.RobotProtocolMode;

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

        // Page 2: Web Server Integration
        public int ClientIndex { get; set; } = 1;
        public string WebServerUrl { get; set; } = "http://localhost:5292";
        public string VisionServerUrl { get; set; } = "http://localhost:5000";

        // GS 인증: Web 서버 X-API-Key (BODA.VMS.Web PR #10). 빈 값이면 헤더 미송신.
        // Web 서버의 ClientApiKey:Value (user-secrets / 환경변수 ClientApiKey__Value) 와 동일하게 설정.
        public string ClientApiKey { get; set; } = string.Empty;

        // SSO Migration Plan §2.2 (SSO PR4) — Web SSO 통합 옵션.
        // JSON 으로 "webSso" : { "enabled": true, "webServerUrl": "..." } 객체로 직렬화.
        // VMS.Core.Security.WebSsoConfig.LoadFromAppData 가 본 스키마 그대로 파싱.
        public WebSsoSettings WebSso { get; set; } = new();

        // 비상 local-admin 비밀번호 변경 (선택) — 비어 있으면 기존 비밀번호 유지.
        // AppSetup wizard 에서 운영 첫 가동시 디폴트 (vasim1234) 변경 권장.
        // system_config.json 에는 절대 저장 안 함 — 저장 직후 UserService.SetLocalFallbackPassword 로
        // BCrypt 해시로만 DB 갱신 후 메모리에서 폐기 (SetupViewModel.SaveConfiguration 책임).
        [System.Text.Json.Serialization.JsonIgnore]
        public string LocalFallbackAdminPassword { get; set; } = string.Empty;

        // Page 6: IO 보드 (Phase 2b) — PLC 와 동시 사용 가능한 디지털 IO 디바이스 목록.
        // ADLink PCI-743x / Advantech PCI-17xx 등. SystemConfiguration 과 같은 필드명 →
        // ConfigurationService 가 동일 JSON 으로 read/write.
        public List<IoDeviceConfig> IoBoards { get; set; } = new();

        // Page 7: Security Mode — system_config.json 의 "securityMode" 키로 직렬화.
        // VMS.Core.Security.SecurityOptions.LoadFromAppData 가 이 키를 읽어 부팅 시 정책 결정.
        // RELEASE 빌드 VMS 는 RequireExplicit 정책이라 이 키(또는 BODA_VMS_SECURITY_MODE 환경변수)
        // 미명시 시 부팅 중단 — wizard 가 항상 기록해 현장 설치 직후 부팅 실패를 방지.
        public SecurityMode SecurityMode { get; set; } = SecurityMode.Production;

        // Page 5: Robot Settings
        public bool IsRobotEnabled { get; set; }
        public RobotVendor RobotVendor { get; set; } = RobotVendor.None;
        public string RobotIpAddress { get; set; } = "192.168.0.200";
        public int RobotPort { get; set; } = 30003;
        public EulerConvention EulerConvention { get; set; } = EulerConvention.UR_RotationVector;
        public RobotProtocolMode RobotProtocolMode { get; set; } = RobotProtocolMode.VendorNative;
        public byte RobotModbusUnitId { get; set; } = 1;
        public ushort RobotModbusPoseRegister { get; set; } = 270;

        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "1.0.0";
    }

    /// <summary>
    /// 애플리케이션 보안 모드 — VMS.Core.Security.SecurityMode 와 이름 일치 필수
    /// (JsonStringEnumConverter 로 "Development"/"Production" 문자열 직렬화 후
    /// VMS 측이 Enum.TryParse 로 파싱).
    /// </summary>
    public enum SecurityMode
    {
        /// <summary>개발 모드 — HTTP 허용, self-signed cert 우회. 로컬/사내 테스트만.</summary>
        Development,

        /// <summary>운영 모드 — HTTPS 강제, cert 엄격 검증. 현장 배포 기본값.</summary>
        Production
    }

    /// <summary>
    /// 로봇 제조사
    /// </summary>
    public enum RobotVendor
    {
        None,
        UR,
        Doosan,
        Jaka,
        ABB,
        Fanuc
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

        // Area Scan parameters — 마법사 UI 에서는 더 이상 노출하지 않는다.
        // 런타임(VMS 스텝 / VisionSetup 레시피 스텝)이 카메라 단위 값을 읽지 않으므로
        // 기존 system_config.json 호환(역직렬화)용으로만 필드를 유지한다.
        [ObservableProperty]
        private double _exposure = 5000;

        [ObservableProperty]
        private double _gain = 1.0;

        // Line Scan parameters
        [ObservableProperty]
        private TriggerSource _triggerSource = TriggerSource.Internal;

        [ObservableProperty]
        private double _lineRate = 10000;

        /// <summary>마법사 UI 에서 제거됨(런타임 미사용) — json 호환용 유지.</summary>
        [ObservableProperty]
        private double _encoderResolution = 10.0;

        [ObservableProperty]
        private int _scanLength = 4096;

        // 3D Camera parameters — 마법사 UI 에서 제거됨. 런타임 3D 옵션(점군 후처리·깊이 범위)은
        // VisionSetup 레시피 스텝이 소유하므로 카메라 단위 값은 json 호환용으로만 유지한다.
        [ObservableProperty]
        private CaptureMode3D _captureMode = CaptureMode3D.Both;

        [ObservableProperty]
        private int _filterStrength = 3;

        [ObservableProperty]
        private double _zRangeMin = 0;

        [ObservableProperty]
        private double _zRangeMax = 1000;

        // Frame Grabber (Matrox/Dalsa) parameters
        /// <summary>보드 타입 (SOLIOS, RAPIXO, RADIENT 등)</summary>
        [ObservableProperty]
        private string _boardType = "SOLIOS";

        /// <summary>보드 번호 (0 = M_DEV0, 1 = M_DEV1, ...)</summary>
        [ObservableProperty]
        private int _boardNumber;

        /// <summary>디지타이저(채널) 번호 (0 = CH0, 1 = CH1, ...)</summary>
        [ObservableProperty]
        private int _digitizerNumber;

        /// <summary>DCF 파일 경로 (Camera Link 카메라 설정)</summary>
        [ObservableProperty]
        private string _dcfFilePath = string.Empty;

        // Dynamic UI visibility properties
        [JsonIgnore]
        public bool IsAreaScan => CameraType == CameraType.AreaScan2D || CameraType == CameraType.AreaScan3D;

        [JsonIgnore]
        public bool IsLineScan => CameraType == CameraType.LineScan2D || CameraType == CameraType.LineScan3D;

        [JsonIgnore]
        public bool Is3DCamera => CameraType == CameraType.AreaScan3D || CameraType == CameraType.LineScan3D;

        [JsonIgnore]
        public bool IsFrameGrabber => Manufacturer == CameraManufacturer.Matrox || Manufacturer == CameraManufacturer.Dalsa;

        [JsonIgnore]
        public string DisplayInfo => $"{Manufacturer} - {IpAddress}";

        partial void OnCameraTypeChanged(CameraType value)
        {
            OnPropertyChanged(nameof(IsAreaScan));
            OnPropertyChanged(nameof(IsLineScan));
            OnPropertyChanged(nameof(Is3DCamera));
        }

        partial void OnManufacturerChanged(CameraManufacturer value)
        {
            OnPropertyChanged(nameof(IsFrameGrabber));
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

    /// <summary>
    /// SSO 옵션 — system_config.json:webSso 객체로 직렬화.
    /// VMS.Core.Security.WebSsoConfig 가 본 스키마 그대로 읽음.
    /// </summary>
    public sealed class WebSsoSettings
    {
        /// <summary>Web SSO 활성 여부 (기본 false — 현행 로컬 인증 유지).</summary>
        public bool Enabled { get; set; }

        /// <summary>SSO 대상 Web URL. 빈 값이면 루트 WebServerUrl 사용 (호환).</summary>
        public string WebServerUrl { get; set; } = string.Empty;
    }

}
