using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;

namespace VMS.VisionSetup.ViewModels
{
    public partial class CameraManagerViewModel : ObservableObject
    {
        private readonly ICameraService _cameraService;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private ObservableCollection<CameraInfo> _cameras = new();

        [ObservableProperty]
        private CameraInfo? _selectedCamera;

        [ObservableProperty]
        private bool _isNewCamera;

        [ObservableProperty]
        private bool _isPropertiesPanelVisible;

        [ObservableProperty]
        private bool _hasSelection;

        // Form fields
        [ObservableProperty]
        private string _cameraName = string.Empty;

        [ObservableProperty]
        private string _manufacturer = string.Empty;

        [ObservableProperty]
        private string _model = string.Empty;

        [ObservableProperty]
        private string _serialNumber = string.Empty;

        [ObservableProperty]
        private string _widthText = string.Empty;

        [ObservableProperty]
        private string _heightText = string.Empty;

        [ObservableProperty]
        private string _connectionString = string.Empty;

        [ObservableProperty]
        private bool _isEnabled = true;

        public CameraManagerViewModel(ICameraService cameraService, IDialogService dialogService)
        {
            _cameraService = cameraService;
            _dialogService = dialogService;
            RefreshCameraList();
        }

        public void RefreshCameraList()
        {
            Cameras = new ObservableCollection<CameraInfo>(_cameraService.GetAllCameras());
        }

        partial void OnSelectedCameraChanged(CameraInfo? value)
        {
            HasSelection = value != null;

            if (value != null)
            {
                IsNewCamera = false;
                LoadCameraToForm(value);
                IsPropertiesPanelVisible = true;
            }
            else
            {
                IsPropertiesPanelVisible = false;
            }
        }

        private void LoadCameraToForm(CameraInfo camera)
        {
            CameraName = camera.Name;
            Manufacturer = camera.Manufacturer;
            Model = camera.Model;
            SerialNumber = camera.SerialNumber;
            WidthText = camera.Width.ToString();
            HeightText = camera.Height.ToString();
            ConnectionString = camera.ConnectionString;
            IsEnabled = camera.IsEnabled;
        }

        [RelayCommand]
        private void AddCamera()
        {
            IsNewCamera = true;
            SelectedCamera = null;

            var newCamera = new CameraInfo
            {
                Name = "New Camera",
                Manufacturer = "Basler",
                Width = 1920,
                Height = 1080,
                IsEnabled = true
            };

            LoadCameraToForm(newCamera);
            IsPropertiesPanelVisible = true;
            // Store the new camera reference for saving
            _pendingNewCamera = newCamera;
        }

        private CameraInfo? _pendingNewCamera;

        [RelayCommand]
        private void RemoveCamera()
        {
            if (SelectedCamera == null) return;

            if (_dialogService.ShowConfirmation(
                $"'{SelectedCamera.Name}' 카메라를 삭제하시겠습니까?",
                "Delete Camera"))
            {
                _cameraService.RemoveCamera(SelectedCamera.Id);
                RefreshCameraList();
                IsPropertiesPanelVisible = false;
                SelectedCamera = null;
            }
        }

        [RelayCommand]
        private void SaveCamera()
        {
            if (string.IsNullOrWhiteSpace(CameraName))
            {
                _dialogService.ShowWarning("카메라 이름을 입력하세요.", "Validation Error");
                return;
            }

            if (!int.TryParse(WidthText, out int width) || width <= 0)
            {
                _dialogService.ShowWarning("유효한 너비 값을 입력하세요.", "Validation Error");
                return;
            }

            if (!int.TryParse(HeightText, out int height) || height <= 0)
            {
                _dialogService.ShowWarning("유효한 높이 값을 입력하세요.", "Validation Error");
                return;
            }

            CameraInfo camera;
            if (IsNewCamera && _pendingNewCamera != null)
            {
                camera = _pendingNewCamera;
            }
            else if (SelectedCamera != null)
            {
                camera = SelectedCamera;
            }
            else
            {
                return;
            }

            camera.Name = CameraName.Trim();
            camera.Manufacturer = Manufacturer?.Trim() ?? string.Empty;
            camera.Model = Model?.Trim() ?? string.Empty;
            camera.SerialNumber = SerialNumber?.Trim() ?? string.Empty;
            camera.Width = width;
            camera.Height = height;
            camera.ConnectionString = ConnectionString?.Trim() ?? string.Empty;
            camera.IsEnabled = IsEnabled;

            if (IsNewCamera)
            {
                _cameraService.AddCamera(camera);
                IsNewCamera = false;
                _pendingNewCamera = null;
            }
            else
            {
                _cameraService.UpdateCamera(camera);
            }

            RefreshCameraList();

            // Re-select the saved camera
            SelectedCamera = Cameras.FirstOrDefault(c => c.Id == camera.Id);
        }

        [RelayCommand]
        private void CancelEdit()
        {
            if (IsNewCamera)
            {
                _pendingNewCamera = null;
                IsNewCamera = false;
                SelectedCamera = null;
                IsPropertiesPanelVisible = false;
            }
            else if (SelectedCamera != null)
            {
                var originalCamera = _cameraService.GetCamera(SelectedCamera.Id);
                if (originalCamera != null)
                {
                    LoadCameraToForm(originalCamera);
                }
            }
        }
    }
}
