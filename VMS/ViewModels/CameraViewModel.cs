using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.Interfaces;
using VMS.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using PointCloudData = VMS.Camera.Models.PointCloudData;

namespace VMS.ViewModels
{
    /// <summary>
    /// ViewModel for individual camera display
    /// </summary>
    public partial class CameraViewModel : ObservableObject
    {
        private readonly IDialogService? _dialogService;
        private readonly IConfigurationService? _configService;
        private readonly IInspectionService? _inspectionService;
        private ICameraAcquisition? _acquisition;
        private Models.Recipe? _currentRecipe;
        private BitmapSource? _originalImage;  // 검사용 원본 이미지 (오버레이 전)

        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        private CameraManufacturer _manufacturer;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private bool _isPassed = false;  // Bypass checkbox: checked = always OK without inspection

        [ObservableProperty]
        private bool _isInspected = false;  // True after trigger + inspection is performed

        [ObservableProperty]
        private bool _inspectionOk = true;  // Actual inspection result (true=OK, false=NG)

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private BitmapSource? _currentImage;

        [ObservableProperty]
        private PointCloudData? _currentPointCloud;

        [ObservableProperty]
        private int _selectedViewTab;  // 0=2D, 1=3D

        // Layout properties
        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        [ObservableProperty]
        private double _width = 400;

        [ObservableProperty]
        private double _height = 300;

        // Step support
        [ObservableProperty]
        private ObservableCollection<StepViewModel> _steps = new();

        [ObservableProperty]
        private StepViewModel? _selectedStep;

        [ObservableProperty]
        private int _currentStepIndex;

        // Inline control box
        [ObservableProperty]
        private bool _isControlBoxOpen;

        // Current step's camera settings (bound to selected step)
        public double Exposure
        {
            get => SelectedStep?.Exposure ?? 5000;
            set
            {
                if (SelectedStep != null)
                {
                    SelectedStep.Exposure = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Gain
        {
            get => SelectedStep?.Gain ?? 1.0;
            set
            {
                if (SelectedStep != null)
                {
                    SelectedStep.Gain = value;
                    OnPropertyChanged();
                }
            }
        }

        partial void OnSelectedStepChanged(StepViewModel? value)
        {
            OnPropertyChanged(nameof(Exposure));
            OnPropertyChanged(nameof(Gain));
        }

        // Inspection results
        [ObservableProperty]
        private string _resultMessage = string.Empty;

        [ObservableProperty]
        private int _inspectionCount;

        [ObservableProperty]
        private int _passCount;

        [ObservableProperty]
        private int _failCount;

        [ObservableProperty]
        private double _lastExecutionTimeMs;

        public ObservableCollection<ToolResultItem> ToolRunResults { get; } = new();

        /// <summary>
        /// 마지막 검사의 개별 도구 결과 (AutoProcessService PLC 전송용)
        /// </summary>
        public IReadOnlyList<Interfaces.ToolInspectionResult>? LastToolResults { get; private set; }

        // Status logic:
        // IsPassed checked   -> always "OK" (green), bypass inspection
        // IsPassed unchecked -> "WAIT" (orange) until trigger received
        //   after trigger    -> "OK" (green) or "NG" (red) based on InspectionOk
        public string StatusText => IsPassed ? "OK" : (!IsInspected ? "WAIT" : (InspectionOk ? "OK" : "NG"));

        partial void OnIsPassedChanged(bool value)
        {
            if (value)
            {
                IsInspected = false;
            }
            NotifyStatusChanged();
        }

        partial void OnIsInspectedChanged(bool value) => NotifyStatusChanged();

        partial void OnInspectionOkChanged(bool value) => NotifyStatusChanged();

        private void NotifyStatusChanged() => OnPropertyChanged(nameof(StatusText));

        public CameraViewModel()
        {
        }

        public CameraViewModel(IDialogService dialogService, IConfigurationService configService,
            IInspectionService? inspectionService = null)
        {
            _dialogService = dialogService;
            _configService = configService;
            _inspectionService = inspectionService;
        }

        public void SetRecipe(Models.Recipe? recipe)
        {
            _currentRecipe = recipe;
            _inspectionService?.ClearCache();
        }

        /// <summary>
        /// Called when a trigger is received and inspection is performed.
        /// Sets the inspection result (OK or NG).
        /// </summary>
        public void SetInspectionResult(bool ok)
        {
            InspectionOk = ok;
            IsInspected = true;
            InspectionCount++;
            if (ok) PassCount++;
            else FailCount++;
        }

        /// <summary>
        /// Reset inspection state to waiting for next cycle
        /// </summary>
        public void ResetInspection()
        {
            IsInspected = false;
            InspectionOk = true;
        }

        [RelayCommand]
        private void ToggleControlBox()
        {
            IsControlBoxOpen = !IsControlBoxOpen;
        }

        [RelayCommand]
        private void CloseControlBox()
        {
            IsControlBoxOpen = false;
        }

        [RelayCommand]
        private async Task GrabAsync()
        {
            try
            {
                _acquisition ??= CameraAcquisitionFactory.Create(ToCameraInfo());

                if (!_acquisition.IsConnected)
                {
                    var connected = await _acquisition.ConnectAsync(ToCameraInfo());
                    if (!connected)
                    {
                        ResultMessage = "Camera connection failed";
                        return;
                    }
                    IsConnected = true;
                }

                var result = await _acquisition.AcquireAsync();
                if (result.Success)
                {
                    if (result.Image2D != null)
                    {
                        var bmp = MatToBitmapSource(result.Image2D);
                        _originalImage = bmp;
                        CurrentImage = bmp;
                        SelectedViewTab = 0;
                    }

                    if (result.PointCloud != null)
                    {
                        CurrentPointCloud = result.PointCloud;
                        SelectedViewTab = 1;
                    }

                    ResultMessage = "Acquisition OK";
                }
                else
                {
                    ResultMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                ResultMessage = $"Grab error: {ex.Message}";
            }
        }

        [RelayCommand]
        private void OpenImage()
        {
            if (_dialogService == null) return;

            var filePath = _dialogService.ShowOpenFileDialog(
                "Image Files (*.bmp;*.jpg;*.png;*.tif)|*.bmp;*.jpg;*.png;*.tif|All Files (*.*)|*.*",
                ".bmp");

            if (filePath != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(filePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _originalImage = bitmap;
                    CurrentImage = bitmap;
                    SelectedViewTab = 0;
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError($"Image load failed: {ex.Message}", "Error");
                }
            }
        }

        [RelayCommand]
        private async Task ManualInspectAsync()
        {
            if (_inspectionService == null || (_originalImage ?? CurrentImage) == null)
            {
                SetInspectionResult(true);
                return;
            }

            // Find matching inspection step for this camera + current step index
            var step = FindCurrentStep();
            if (step == null || step.Tools.Count == 0)
            {
                SetInspectionResult(true);
                return;
            }

            // 항상 원본 이미지로 검사 (오버레이가 그려진 이미지 사용 방지)
            var sourceImage = _originalImage ?? CurrentImage!;
            var mat = BitmapSourceToMat(sourceImage);
            if (mat == null)
            {
                SetInspectionResult(false);
                ResultMessage = "Image conversion failed";
                return;
            }

            try
            {
                var result = await _inspectionService.ExecuteStepAsync(step, mat);

                // 원본 이미지로 복원 후 오버레이 표시
                if (result.OverlayImage != null && !result.OverlayImage.Empty())
                {
                    CurrentImage = MatToBitmapSource(result.OverlayImage);
                    result.OverlayImage.Dispose();
                }
                else
                {
                    // 오버레이 없으면 원본으로 복원
                    CurrentImage = _originalImage;
                }

                SetInspectionResult(result.Success);
                ResultMessage = result.Success ? "OK" : "NG";
                LastExecutionTimeMs = result.ExecutionTimeMs;
                UpdateToolRunResults(result);
            }
            catch (Exception ex)
            {
                SetInspectionResult(false);
                ResultMessage = $"Inspection error: {ex.Message}";
            }
            finally
            {
                mat.Dispose();
            }
        }

        private void UpdateToolRunResults(Interfaces.StepInspectionResult result)
        {
            LastToolResults = result.ToolResults;
            ToolRunResults.Clear();

            foreach (var toolResult in result.ToolResults)
            {
                var resultValue = string.Empty;

                if (toolResult.Data != null && toolResult.Data.Count > 0)
                {
                    var entries = toolResult.Data.Select(kv =>
                    {
                        var formatted = kv.Value switch
                        {
                            double d => d.ToString("F3"),
                            float f => f.ToString("F3"),
                            decimal m => m.ToString("F3"),
                            _ => kv.Value?.ToString() ?? ""
                        };
                        return $"{kv.Key}={formatted}";
                    });
                    resultValue = string.Join(", ", entries);
                }
                else
                {
                    resultValue = toolResult.Message;
                }

                ToolRunResults.Add(new Models.ToolResultItem
                {
                    ToolName = toolResult.ToolName,
                    Result = toolResult.Success,
                    ResultValue = resultValue
                });
            }
        }

        private Models.InspectionStep? FindCurrentStep()
        {
            if (_currentRecipe == null) return null;

            // Find steps matching this camera
            var cameraSteps = _currentRecipe.Steps
                .Where(s => s.CameraId == Id)
                .OrderBy(s => s.Sequence)
                .ToList();

            if (cameraSteps.Count == 0) return null;

            // Return step matching current step index
            if (CurrentStepIndex >= 0 && CurrentStepIndex < cameraSteps.Count)
                return cameraSteps[CurrentStepIndex];

            return cameraSteps[0];
        }

        private static Mat? BitmapSourceToMat(BitmapSource source)
        {
            try
            {
                // Convert to Bgr24 format
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = (width * 3 + 3) & ~3;
                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                var mat = new Mat(height, width, MatType.CV_8UC3);
                Marshal.Copy(pixels, 0, mat.Data, Math.Min(pixels.Length, (int)(mat.Step() * height)));
                return mat;
            }
            catch
            {
                return null;
            }
        }

        [RelayCommand]
        private void SaveSettings()
        {
            if (_configService == null) return;

            var config = _configService.LoadSystemConfiguration();
            var camConfig = config.Cameras.FirstOrDefault(c => c.Id == Id);
            if (camConfig != null)
            {
                camConfig.Steps.Clear();
                foreach (var step in Steps)
                {
                    camConfig.Steps.Add(new StepConfiguration
                    {
                        StepNumber = step.StepNumber,
                        Name = step.Name,
                        Exposure = step.Exposure,
                        Gain = step.Gain
                    });
                }
                _configService.SaveSystemConfiguration(config);
            }
        }

        private CameraInfo ToCameraInfo()
        {
            return new CameraInfo
            {
                Id = Id,
                Name = Name,
                Manufacturer = Manufacturer.ToString().Replace("_", " "),
                ConnectionString = IpAddress
            };
        }

        private static BitmapSource? MatToBitmapSource(Mat mat)
        {
            if (mat.Empty()) return null;

            var format = mat.Channels() switch
            {
                1 => PixelFormats.Gray8,
                3 => PixelFormats.Bgr24,
                4 => PixelFormats.Bgra32,
                _ => PixelFormats.Bgr24
            };

            int stride = (int)mat.Step();
            byte[] data = new byte[stride * mat.Height];
            Marshal.Copy(mat.Data, data, 0, data.Length);

            var bitmapSource = BitmapSource.Create(
                mat.Width, mat.Height, 96, 96, format, null, data, stride);
            bitmapSource.Freeze();
            return bitmapSource;
        }

        public static CameraViewModel FromConfiguration(
            CameraConfiguration config,
            IDialogService dialogService,
            IConfigurationService configService,
            IInspectionService? inspectionService = null)
        {
            var vm = new CameraViewModel(dialogService, configService, inspectionService)
            {
                Id = config.Id,
                Name = config.Name,
                IpAddress = config.IpAddress,
                Manufacturer = config.Manufacturer,
                IsEnabled = config.IsEnabled
            };

            // Create steps from configuration or default
            if (config.Steps != null && config.Steps.Count > 0)
            {
                foreach (var step in config.Steps)
                {
                    vm.Steps.Add(new StepViewModel
                    {
                        StepNumber = step.StepNumber,
                        Name = step.Name,
                        Exposure = step.Exposure,
                        Gain = step.Gain
                    });
                }
            }
            else
            {
                // Create default steps based on StepCount
                var stepCount = Math.Max(1, config.StepCount);
                for (int i = 1; i <= stepCount; i++)
                {
                    vm.Steps.Add(new StepViewModel
                    {
                        StepNumber = i,
                        Name = $"Step {i}",
                        Exposure = 5000,
                        Gain = 1.0
                    });
                }
            }

            // Select first step by default
            if (vm.Steps.Count > 0)
            {
                vm.SelectedStep = vm.Steps[0];
            }

            return vm;
        }

        public void ApplyLayout(CameraWindowLayout layout)
        {
            X = layout.X;
            Y = layout.Y;
            Width = layout.Width;
            Height = layout.Height;
        }

        public CameraWindowLayout ToLayout()
        {
            return new CameraWindowLayout
            {
                CameraId = Id,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height
            };
        }
    }

    /// <summary>
    /// ViewModel for camera step (robot position with exposure/gain settings)
    /// </summary>
    public partial class StepViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _stepNumber = 1;

        [ObservableProperty]
        private string _name = "Step 1";

        [ObservableProperty]
        private double _exposure = 5000;

        [ObservableProperty]
        private double _gain = 1.0;

        public override string ToString() => Name;
    }
}
