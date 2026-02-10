using VMS.AppSetup.Models;
using VMS.AppSetup.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace VMS.AppSetup.ViewModels
{
    public partial class SetupViewModel : ObservableObject
    {
        private const int TotalPages = 4;

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

        // Page 4: PLC Settings
        [ObservableProperty]
        private PlcVendor _selectedPlcVendor = PlcVendor.None;

        [ObservableProperty]
        private PlcCommunicationType _selectedCommunicationType = PlcCommunicationType.Ethernet;

        [ObservableProperty]
        private string _plcIpAddress = "192.168.0.100";

        [ObservableProperty]
        private int _plcPort = 502;

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

        public SetupViewModel()
        {
            UpdatePageInfo();
            LoadExistingConfiguration();
        }

        private void LoadExistingConfiguration()
        {
            var config = ConfigurationService.Instance.LoadConfiguration();
            if (config != null)
            {
                ApplicationName = config.ApplicationName;
                SystemIpAddress = config.SystemIpAddress;
                CameraMode = config.CameraMode;
                SelectedPlcVendor = config.PlcVendor;
                SelectedCommunicationType = config.CommunicationType;
                PlcIpAddress = config.PlcIpAddress;
                PlcPort = config.PlcPort;

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
            MessageBox.Show(
                "카메라 스캔 기능은 실제 카메라 SDK 연동 후 구현됩니다.\n\n" +
                "현재는 가상 모드를 사용하여 카메라를 수동으로 설정하세요.",
                "Camera Scan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

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
                PlcVendor = SelectedPlcVendor,
                CommunicationType = SelectedCommunicationType,
                PlcIpAddress = PlcIpAddress,
                PlcPort = PlcPort
            };

            if (ConfigurationService.Instance.SaveConfiguration(config))
            {
                MessageBox.Show(
                    $"설정이 저장되었습니다.\n\n저장 위치: {ConfigurationService.Instance.ConfigFilePath}",
                    "Setup Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Close the application
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(
                    "설정 저장에 실패했습니다.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
