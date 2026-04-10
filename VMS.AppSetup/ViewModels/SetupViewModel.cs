using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Models;
using VMS.Camera.Models;
using VMS.PLC.Models;
using EulerConvention = VMS.Camera.Models.EulerConvention;
using RobotProtocolMode = VMS.Camera.Models.RobotProtocolMode;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace VMS.AppSetup.ViewModels
{
    public partial class SetupViewModel : ObservableObject
    {
        private const int TotalPages = 5;

        private readonly IConfigurationService _configService;
        private readonly IDialogService _dialogService;
        private readonly Action _shutdownAction;

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private string _pageTitle = "Welcome";

        [ObservableProperty]
        private string _pageDescription = string.Empty;

        // Page 2: Application Settings
        [ObservableProperty]
        private string _applicationName = "BODA Vision System";

        [ObservableProperty]
        private string _systemIpAddress = "192.168.0.1";

        // Page 2: Web Server Integration
        [ObservableProperty]
        private int _clientIndex = 1;

        [ObservableProperty]
        private string _webServerUrl = "http://localhost:5292";

        [ObservableProperty]
        private string _visionServerUrl = "http://localhost:5000";

        // Page 3: Camera Settings
        [ObservableProperty]
        private CameraMode _cameraMode = CameraMode.Virtual;

        [ObservableProperty]
        private ObservableCollection<CameraConfiguration> _cameras = new();

        [ObservableProperty]
        private int _virtualCameraCount = 1;

        // Page 4: PLC Settings — Vendor & Communication
        [ObservableProperty]
        private PlcVendor _selectedPlcVendor = PlcVendor.None;

        [ObservableProperty]
        private PlcCommunicationType _selectedCommunicationType = PlcCommunicationType.Ethernet;

        [ObservableProperty]
        private string _plcIpAddress = "192.168.0.100";

        [ObservableProperty]
        private int _plcPort = 502;

        // Page 4: Modbus
        [ObservableProperty]
        private byte _modbusUnitId = 255;

        // Page 4: Serial
        [ObservableProperty]
        private string _serialPortName = "COM1";

        [ObservableProperty]
        private int _baudRate = 115200;

        [ObservableProperty]
        private int _dataBits = 8;

        [ObservableProperty]
        private PlcSerialParity _parity = PlcSerialParity.None;

        [ObservableProperty]
        private PlcSerialStopBits _stopBits = PlcSerialStopBits.One;

        // Page 4: Performance & Stability
        [ObservableProperty]
        private int _pollingIntervalMs = 20;

        [ObservableProperty]
        private bool _useHeartbeat;

        [ObservableProperty]
        private string _heartbeatAddress = string.Empty;

        [ObservableProperty]
        private bool _autoReconnect = true;

        // Page 4: Data Synchronization
        [ObservableProperty]
        private PlcWriteMode _writeMode = PlcWriteMode.Handshake;

        [ObservableProperty]
        private PlcEndianMode _endianMode = PlcEndianMode.LittleEndian;

        // Page 5: Robot Settings
        [ObservableProperty]
        private bool _isRobotEnabled;

        [ObservableProperty]
        private RobotVendor _selectedRobotVendor = RobotVendor.None;

        [ObservableProperty]
        private string _robotIpAddress = "192.168.0.200";

        [ObservableProperty]
        private int _robotPort = 30003;

        [ObservableProperty]
        private EulerConvention _selectedEulerConvention = EulerConvention.UR_RotationVector;

        [ObservableProperty]
        private RobotProtocolMode _selectedRobotProtocolMode = RobotProtocolMode.VendorNative;

        [ObservableProperty]
        private byte _robotModbusUnitId = 1;

        [ObservableProperty]
        private ushort _robotModbusPoseRegister = 270;

        // Dynamic visibility
        public bool IsSerialMode => SelectedCommunicationType == PlcCommunicationType.Serial;
        public bool IsEthernetMode => SelectedCommunicationType != PlcCommunicationType.Serial;
        public bool IsModbusVendor => SelectedPlcVendor == PlcVendor.Modbus;
        public bool IsRobotVendorSelected => SelectedRobotVendor != RobotVendor.None;
        public bool IsRobotModbusMode => SelectedRobotProtocolMode == RobotProtocolMode.ModbusTcp;
        public bool ShowRobotModbusOption => SelectedRobotVendor == RobotVendor.Doosan;

        // Navigation
        [ObservableProperty]
        private bool _canGoBack;

        [ObservableProperty]
        private bool _canGoNext = true;

        [ObservableProperty]
        private bool _isLastPage;

        [ObservableProperty]
        private string _nextButtonText = "Next";

        // Enum values for binding
        public Array CameraManufacturers => Enum.GetValues(typeof(CameraManufacturer));
        public Array CameraTypes => Enum.GetValues(typeof(CameraType));
        public Array TriggerSources => Enum.GetValues(typeof(TriggerSource));
        public Array CaptureModes3D => Enum.GetValues(typeof(CaptureMode3D));
        public Array PlcVendors => Enum.GetValues(typeof(PlcVendor));
        public Array CommunicationTypes => Enum.GetValues(typeof(PlcCommunicationType));
        public Array CameraModes => Enum.GetValues(typeof(CameraMode));
        public Array SerialParities => Enum.GetValues(typeof(PlcSerialParity));
        public Array SerialStopBitsValues => Enum.GetValues(typeof(PlcSerialStopBits));
        public Array WriteModes => Enum.GetValues(typeof(PlcWriteMode));
        public Array EndianModes => Enum.GetValues(typeof(PlcEndianMode));
        public Array RobotVendors => Enum.GetValues(typeof(RobotVendor));
        public Array EulerConventions => Enum.GetValues(typeof(EulerConvention));
        public Array RobotProtocolModes => Enum.GetValues(typeof(RobotProtocolMode));
        public int[] BaudRateOptions => [9600, 19200, 38400, 57600, 115200];
        public int[] DataBitsOptions => [7, 8];

        public SetupViewModel(IConfigurationService configService, IDialogService dialogService, Action shutdownAction)
        {
            _configService = configService;
            _dialogService = dialogService;
            _shutdownAction = shutdownAction;

            UpdatePageInfo();
            LoadExistingConfiguration();
        }

        private void LoadExistingConfiguration()
        {
            var config = _configService.LoadConfiguration();
            if (config != null)
            {
                ApplicationName = config.ApplicationName;
                SystemIpAddress = config.SystemIpAddress;
                ClientIndex = config.ClientIndex;
                WebServerUrl = config.WebServerUrl;
                VisionServerUrl = config.VisionServerUrl;
                CameraMode = config.CameraMode;

                // PLC Vendor & Communication
                SelectedPlcVendor = config.PlcVendor;
                SelectedCommunicationType = config.CommunicationType;
                PlcIpAddress = config.PlcIpAddress;
                PlcPort = config.PlcPort;

                // Modbus
                ModbusUnitId = config.ModbusUnitId;

                // Serial
                SerialPortName = config.SerialPortName;
                BaudRate = config.BaudRate;
                DataBits = config.DataBits;
                Parity = config.Parity;
                StopBits = config.StopBits;

                // Performance & Stability
                PollingIntervalMs = config.PollingIntervalMs;
                UseHeartbeat = config.UseHeartbeat;
                HeartbeatAddress = config.HeartbeatAddress;
                AutoReconnect = config.AutoReconnect;

                // Data Synchronization
                WriteMode = config.WriteMode;
                EndianMode = config.EndianMode;

                // Robot
                IsRobotEnabled = config.IsRobotEnabled;
                SelectedRobotVendor = config.RobotVendor;
                RobotIpAddress = config.RobotIpAddress;
                RobotPort = config.RobotPort;
                SelectedEulerConvention = config.EulerConvention;
                SelectedRobotProtocolMode = config.RobotProtocolMode;
                RobotModbusUnitId = config.RobotModbusUnitId;
                RobotModbusPoseRegister = config.RobotModbusPoseRegister;

                foreach (var cam in config.Cameras)
                {
                    Cameras.Add(cam);
                }
            }
        }

        partial void OnCurrentPageChanged(int value)
        {
            UpdatePageInfo();
        }

        partial void OnCameraModeChanged(CameraMode value)
        {
        }

        partial void OnVirtualCameraCountChanged(int value)
        {
            if (CameraMode == CameraMode.Virtual)
            {
                UpdateVirtualCameras();
            }
        }

        partial void OnSelectedCommunicationTypeChanged(PlcCommunicationType value)
        {
            OnPropertyChanged(nameof(IsSerialMode));
            OnPropertyChanged(nameof(IsEthernetMode));
        }

        partial void OnSelectedPlcVendorChanged(PlcVendor value)
        {
            OnPropertyChanged(nameof(IsModbusVendor));
        }

        partial void OnSelectedRobotVendorChanged(RobotVendor value)
        {
            OnPropertyChanged(nameof(IsRobotVendorSelected));

            // 제조사별 기본 EulerConvention 및 포트 자동 매핑
            (SelectedEulerConvention, RobotPort) = value switch
            {
                RobotVendor.UR => (EulerConvention.UR_RotationVector, 30003),
                RobotVendor.Doosan => (EulerConvention.Doosan_ZYX, 12345),
                RobotVendor.Jaka => (EulerConvention.Jaka_XYZ, 10001),
                RobotVendor.ABB => (EulerConvention.ABB_Quaternion, 6511),
                RobotVendor.Fanuc => (EulerConvention.Fanuc_WPR, 18735),
                _ => (SelectedEulerConvention, RobotPort)
            };

            // 프로토콜 모드 기본값 리셋 + 설명/옵션 갱신
            SelectedRobotProtocolMode = RobotProtocolMode.VendorNative;
            OnPropertyChanged(nameof(ShowRobotModbusOption));
            OnPropertyChanged(nameof(RobotProtocolDescription));
        }

        partial void OnSelectedRobotProtocolModeChanged(RobotProtocolMode value)
        {
            OnPropertyChanged(nameof(RobotProtocolDescription));
            OnPropertyChanged(nameof(IsRobotModbusMode));

            // Modbus-TCP 선택 시 포트 자동 변경
            if (value == RobotProtocolMode.ModbusTcp)
            {
                RobotPort = 502;
            }
            else if (value == RobotProtocolMode.VendorNative)
            {
                // 네이티브 모드로 돌아가면 제조사 기본 포트로 복원
                RobotPort = SelectedRobotVendor switch
                {
                    RobotVendor.UR => 30003,
                    RobotVendor.Doosan => 12345,
                    RobotVendor.Jaka => 10001,
                    RobotVendor.ABB => 6511,
                    RobotVendor.Fanuc => 18735,
                    _ => RobotPort
                };
            }
        }

        /// <summary>
        /// 현재 선택된 Vendor + Protocol 조합에 대한 설명 텍스트
        /// </summary>
        public string RobotProtocolDescription => (SelectedRobotVendor, SelectedRobotProtocolMode) switch
        {
            (RobotVendor.UR, RobotProtocolMode.VendorNative) =>
                "UR Real-Time Interface (포트 30003): 125Hz 바이너리 패킷에서 TCP 포즈를 자동으로 읽습니다.",
            (RobotVendor.Doosan, RobotProtocolMode.VendorNative) =>
                "Doosan DRL JSON Protocol: {\"cmd\":\"get_current_posx\"} 형식으로 JSON 포즈 데이터를 수신합니다.",
            (RobotVendor.Doosan, RobotProtocolMode.ModbusTcp) =>
                "Doosan Modbus-TCP (포트 502): DRL 프로그램 없이 Holding Register에서 실시간 TCP 포즈를 읽습니다. " +
                "레지스터 6개(X/Y/Z/Rx/Ry/Rz)를 32-bit float으로 파싱합니다.",
            (RobotVendor.Jaka, RobotProtocolMode.VendorNative) =>
                "Jaka SDK JSON Protocol: {\"cmdName\":\"get_tcp_pos\"} 형식으로 JSON 포즈 데이터를 수신합니다.",
            (RobotVendor.ABB, RobotProtocolMode.VendorNative) =>
                "ABB RAPID Socket: [x,y,z],[q1,q2,q3,q4] robtarget 형식으로 쿼터니언 포즈를 수신합니다.",
            (RobotVendor.Fanuc, RobotProtocolMode.VendorNative) =>
                "Fanuc KAREL/TP Socket: CURPOS X:.. Y:.. Z:.. W:.. P:.. R:.. 형식으로 WPR 포즈를 수신합니다.",
            (_, RobotProtocolMode.CustomSocket) =>
                "Custom Socket Server: 사용자 로봇 프로그램의 소켓 서버에 텍스트 명령을 전송하고 CSV 응답(x,y,z,rx,ry,rz)을 파싱합니다.",
            _ => "로봇 제조사를 선택하세요."
        };

        partial void OnIsRobotEnabledChanged(bool value)
        {
            if (value && SelectedRobotVendor == RobotVendor.None)
            {
                SelectedRobotVendor = RobotVendor.UR;
            }
        }

        private void UpdatePageInfo()
        {
            CanGoBack = CurrentPage > 1;
            IsLastPage = CurrentPage == TotalPages;
            NextButtonText = IsLastPage ? "Finish" : "Next";

            switch (CurrentPage)
            {
                case 1:
                    PageTitle = "Welcome to BODA Vision Setup";
                    PageDescription = "This wizard will help you configure the machine vision system.\n\n" +
                        "You will set up:\n" +
                        "• Application settings and network configuration\n" +
                        "• Camera connections and manufacturers\n" +
                        "• PLC communication interface\n" +
                        "• Robot integration (optional)\n\n" +
                        "Click 'Next' to begin the setup process.";
                    break;
                case 2:
                    PageTitle = "Application Settings";
                    PageDescription = "Configure the basic application settings and network configuration.";
                    break;
                case 3:
                    PageTitle = "Camera Configuration";
                    PageDescription = "Configure the cameras for the vision system.\n" +
                        "Choose Live mode to detect connected cameras or Virtual mode to manually configure.";
                    break;
                case 4:
                    PageTitle = "PLC Communication";
                    PageDescription = "Configure the PLC vendor and communication interface.";
                    break;
                case 5:
                    PageTitle = "Robot Configuration";
                    PageDescription = "Configure robot integration for multi-view 3D scanning.\n" +
                        "Enable this if your system uses a robot for camera positioning.";
                    break;
            }
        }

        [RelayCommand]
        private void GoBack()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
            }
        }

        [RelayCommand]
        private void GoNext()
        {
            if (IsLastPage)
            {
                SaveConfiguration();
            }
            else if (CurrentPage < TotalPages)
            {
                CurrentPage++;
            }
        }

        [RelayCommand]
        private void AddCamera()
        {
            var index = Cameras.Count + 1;
            Cameras.Add(new CameraConfiguration
            {
                Name = $"Camera {index}",
                IpAddress = $"192.168.0.{100 + index}",
                Manufacturer = CameraManufacturer.HIK,
                IsEnabled = true
            });
        }

        [RelayCommand]
        private void RemoveCamera(CameraConfiguration camera)
        {
            if (camera != null)
            {
                Cameras.Remove(camera);
            }
        }

        [RelayCommand]
        private void ScanForCameras()
        {
            // TODO: Implement actual network camera discovery
            _dialogService.ShowInformation(
                "카메라 스캔 기능은 아직 구현되지 않았습니다.\n\n" +
                "카메라를 수동으로 추가하려면 '+ Add Camera' 버튼을 사용하세요.",
                "Camera Scan");
        }

        private void UpdateVirtualCameras()
        {
            // Adjust camera count
            while (Cameras.Count < VirtualCameraCount)
            {
                var index = Cameras.Count + 1;
                Cameras.Add(new CameraConfiguration
                {
                    Name = $"Camera {index}",
                    IpAddress = $"192.168.0.{100 + index}",
                    Manufacturer = CameraManufacturer.HIK,
                    IsEnabled = true
                });
            }

            while (Cameras.Count > VirtualCameraCount)
            {
                Cameras.RemoveAt(Cameras.Count - 1);
            }
        }

        private void SaveConfiguration()
        {
            var config = new SetupConfiguration
            {
                ApplicationName = ApplicationName,
                SystemIpAddress = SystemIpAddress,
                ClientIndex = ClientIndex,
                WebServerUrl = WebServerUrl,
                VisionServerUrl = VisionServerUrl,
                CameraMode = CameraMode,
                Cameras = Cameras.ToList(),

                // PLC Vendor & Communication
                PlcVendor = SelectedPlcVendor,
                CommunicationType = SelectedCommunicationType,
                PlcIpAddress = PlcIpAddress,
                PlcPort = PlcPort,

                // Modbus
                ModbusUnitId = ModbusUnitId,

                // Serial
                SerialPortName = SerialPortName,
                BaudRate = BaudRate,
                DataBits = DataBits,
                Parity = Parity,
                StopBits = StopBits,

                // Performance & Stability
                PollingIntervalMs = PollingIntervalMs,
                UseHeartbeat = UseHeartbeat,
                HeartbeatAddress = HeartbeatAddress,
                AutoReconnect = AutoReconnect,

                // Data Synchronization
                WriteMode = WriteMode,
                EndianMode = EndianMode,

                // Robot
                IsRobotEnabled = IsRobotEnabled,
                RobotVendor = SelectedRobotVendor,
                RobotIpAddress = RobotIpAddress,
                RobotPort = RobotPort,
                EulerConvention = SelectedEulerConvention,
                RobotProtocolMode = SelectedRobotProtocolMode,
                RobotModbusUnitId = RobotModbusUnitId,
                RobotModbusPoseRegister = RobotModbusPoseRegister
            };

            if (_configService.SaveConfiguration(config))
            {
                _dialogService.ShowInformation(
                    $"설정이 저장되었습니다.\n\n저장 위치: {_configService.ConfigFilePath}",
                    "Setup Complete");

                // Close the application
                _shutdownAction();
            }
            else
            {
                _dialogService.ShowError(
                    "설정 저장에 실패했습니다.",
                    "Error");
            }
        }
    }
}
