using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using System;
using System.Windows;
using System.Windows.Controls;

namespace VMS.VisionSetup.Views.Camera
{
    /// <summary>
    /// CameraManagerView.xaml 코드 비하인드
    /// </summary>
    public partial class CameraManagerView : UserControl
    {
        private CameraInfo? _selectedCamera;
        private bool _isNewCamera;

        public event EventHandler<CameraInfo>? CameraSelected;

        public CameraManagerView()
        {
            InitializeComponent();
            RefreshCameraList();
        }

        /// <summary>
        /// 카메라 목록 새로고침
        /// </summary>
        public void RefreshCameraList()
        {
            CameraList.ItemsSource = null;
            CameraList.ItemsSource = CameraService.Instance.GetAllCameras();
        }

        private void CameraList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedCamera = CameraList.SelectedItem as CameraInfo;

            bool hasSelection = _selectedCamera != null;
            RemoveButton.IsEnabled = hasSelection;

            if (_selectedCamera != null)
            {
                _isNewCamera = false;
                LoadCameraToForm(_selectedCamera);
                PropertiesPanel.Visibility = Visibility.Visible;
                CameraSelected?.Invoke(this, _selectedCamera);
            }
            else
            {
                PropertiesPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadCameraToForm(CameraInfo camera)
        {
            NameBox.Text = camera.Name;
            SetComboBoxValue(ManufacturerBox, camera.Manufacturer);
            ModelBox.Text = camera.Model;
            SerialNumberBox.Text = camera.SerialNumber;
            WidthBox.Text = camera.Width.ToString();
            HeightBox.Text = camera.Height.ToString();
            ConnectionStringBox.Text = camera.ConnectionString;
            EnabledCheckBox.IsChecked = camera.IsEnabled;
        }

        private void SetComboBoxValue(ComboBox comboBox, string value)
        {
            comboBox.Text = value;
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content?.ToString() == value)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private void AddCamera_Click(object sender, RoutedEventArgs e)
        {
            _isNewCamera = true;
            _selectedCamera = new CameraInfo
            {
                Name = "New Camera",
                Manufacturer = "Basler",
                Width = 1920,
                Height = 1080,
                IsEnabled = true
            };

            CameraList.SelectedItem = null;
            LoadCameraToForm(_selectedCamera);
            PropertiesPanel.Visibility = Visibility.Visible;
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void RemoveCamera_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCamera == null) return;

            var result = MessageBox.Show(
                $"'{_selectedCamera.Name}' 카메라를 삭제하시겠습니까?",
                "Delete Camera",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                CameraService.Instance.RemoveCamera(_selectedCamera.Id);
                RefreshCameraList();
                PropertiesPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SaveCamera_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCamera == null) return;

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("카메라 이름을 입력하세요.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(WidthBox.Text, out int width) || width <= 0)
            {
                MessageBox.Show("유효한 너비 값을 입력하세요.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(HeightBox.Text, out int height) || height <= 0)
            {
                MessageBox.Show("유효한 높이 값을 입력하세요.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _selectedCamera.Name = NameBox.Text.Trim();
            _selectedCamera.Manufacturer = ManufacturerBox.Text?.Trim() ?? string.Empty;
            _selectedCamera.Model = ModelBox.Text?.Trim() ?? string.Empty;
            _selectedCamera.SerialNumber = SerialNumberBox.Text?.Trim() ?? string.Empty;
            _selectedCamera.Width = width;
            _selectedCamera.Height = height;
            _selectedCamera.ConnectionString = ConnectionStringBox.Text?.Trim() ?? string.Empty;
            _selectedCamera.IsEnabled = EnabledCheckBox.IsChecked ?? true;

            if (_isNewCamera)
            {
                CameraService.Instance.AddCamera(_selectedCamera);
                _isNewCamera = false;
            }
            else
            {
                CameraService.Instance.UpdateCamera(_selectedCamera);
            }

            RefreshCameraList();

            // 저장된 카메라를 다시 선택
            foreach (CameraInfo cam in CameraList.Items)
            {
                if (cam.Id == _selectedCamera.Id)
                {
                    CameraList.SelectedItem = cam;
                    break;
                }
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_isNewCamera)
            {
                _selectedCamera = null;
                PropertiesPanel.Visibility = Visibility.Collapsed;
            }
            else if (_selectedCamera != null)
            {
                // 원래 값으로 되돌리기
                var originalCamera = CameraService.Instance.GetCamera(_selectedCamera.Id);
                if (originalCamera != null)
                {
                    LoadCameraToForm(originalCamera);
                }
            }
        }
    }
}
