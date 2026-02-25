using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Models;
using VMS.Camera.Models;
using VMS.PLC.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace VMS.AppSetup.ViewModels
{
    public partial class SetupViewModel : ObservableObject
    {
        private const int TotalPages = 4;

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

        // Dynamic visibility
        public bool IsSerialMode => SelectedCommunicationType == PlcCommunicationType.Serial;
        public bool IsEthernetMode => SelectedCommunicationType != PlcCommunicationType.Serial;
        public bool IsModbusVendor => SelectedPlcVendor == PlcVendor.Modbus;

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
        public Array PlcVendors => Enum.GetValues(typeof(PlcVendor));
        public Array CommunicationTypes => Enum.GetValues(typeof(PlcCommunicationType));
        public Array CameraModes => Enum.GetValues(typeof(CameraMode));
        public Array SerialParities => Enum.GetValues(typeof(PlcSerialParity));
        public Array SerialStopBitsValues => Enum.GetValues(typeof(PlcSerialStopBits));
        public Array WriteModes => Enum.GetValues(typeof(PlcWriteMode));
        public Array EndianModes => Enum.GetValues(typeof(PlcEndianMode));
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
            if (value == CameraMode.Live)
            {
                // In Live mode, scan for connected cameras
                ScanForCameras();
            }
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
                        "• PLC communication interface\n\n" +
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
            // In a real implementation, this would scan the network for cameras
            // For now, we'll simulate finding some cameras
            Cameras.Clear();

            // Simulated camera discovery
            _dialogService.ShowInformation(
                "카메라 스캔 기능은 실제 카메라 SDK 연동 후 구현됩니다.\n\n" +
                "현재는 가상 모드를 사용하여 카메라를 수동으로 설정하세요.",
                "Camera Scan");

            // Switch to virtual mode for now
            CameraMode = CameraMode.Virtual;
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
                EndianMode = EndianMode
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
