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
using System.Threading;
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
        private CancellationTokenSource? _liveGrabCts;
        private Task? _liveGrabTask;

        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        private CameraManufacturer _manufacturer;

        [ObservableProperty]
        private CameraType _cameraType = CameraType.AreaScan2D;

        [ObservableProperty]
        private bool _isEnabled = true;

        // Frame Grabber (Matrox/Dalsa) settings
        [ObservableProperty]
        private string _boardType = string.Empty;

        [ObservableProperty]
        private int _boardNumber;

        [ObservableProperty]
        private int _digitizerNumber;

        [ObservableProperty]
        private string _dcfFilePath = string.Empty;

        // Line Scan parameters
        [ObservableProperty]
        private int _scanLength = 4096;

        [ObservableProperty]
        private double _lineRate = 10000;

        [ObservableProperty]
        private string _triggerSource = "Internal";

        [ObservableProperty]
        private bool _isPassed = false;  // Bypass checkbox: checked = always OK without inspection

        [ObservableProperty]
        private bool _isInspected = false;  // True after trigger + inspection is performed

        [ObservableProperty]
        private bool _inspectionOk = true;  // Actual inspection result (true=OK, false=NG)

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isLiveGrabbing;

        /// <summary>
        /// True일 때 Live 프레임의 화면 표시를 억제합니다.
        /// 롤러 검사 모드에서 용지 캡처 완료 시에만 이미지를 표시하기 위해 사용됩니다.
        /// </summary>
        public bool SuppressLiveDisplay { get; set; }


        [ObservableProperty]
        private BitmapSource? _currentImage;

        [ObservableProperty]
        private PointCloudData? _currentPointCloud;

        [ObservableProperty]
        private int _selectedViewTab;  // 0=2D, 1=Depth Map, 2=Point Cloud

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
        // 카메라 설정 유지 (기본): 촬영 시 카메라의 현재 노출/게인을 건드리지 않음
        public bool Use2DCameraDefault
        {
            get => SelectedStep?.Use2DCameraDefault ?? true;
            set
            {
                if (SelectedStep != null)
                {
                    SelectedStep.Use2DCameraDefault = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsManualExposureEnabled));
                }
            }
        }

        // 노출/게인 슬라이더 활성 조건 — 스텝 선택 + 유지 모드 해제
        public bool IsManualExposureEnabled => SelectedStep != null && !Use2DCameraDefault;

        // 카메라가 실제로 쓰는 현재 노출/게인 (read-back, 지원 카메라만) — 설정 창 열 때 갱신
        [ObservableProperty]
        private string? _cameraCurrentSettingsText;

        public double Exposure
        {
            get => SelectedStep?.Exposure ?? 5000;
            set
            {
                if (SelectedStep != null)
                {
                    SelectedStep.Exposure = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ExposureMs));
                }
            }
        }

        /// <summary>
        /// 노출 UI 입력용 ms 단위 — Mech-Eye Viewer 와 동일 단위 (200 입력 = 200ms).
        /// 저장/적용 경로는 µs 유지 (레시피·system_config 호환, ApplySettingsAsync 가 µs).
        /// </summary>
        public double ExposureMs
        {
            get => Exposure / 1000.0;
            set => Exposure = value * 1000.0;
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
            OnPropertyChanged(nameof(ExposureMs));
            OnPropertyChanged(nameof(Gain));
            OnPropertyChanged(nameof(Use2DCameraDefault));
            OnPropertyChanged(nameof(IsManualExposureEnabled));
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
        /// Raised after a frame is acquired (Grab or Live).
        /// Used by SharedFrameWriter to share frames with VisionSetup.
        /// </summary>
        public event Action<AcquisitionResult>? FrameAcquired;

        /// <summary>
        /// Raised with the raw Mat frame during Live grab (before disposal).
        /// Used by RollerInspectionService for real-time frame analysis.
        /// </summary>
        public event Action<OpenCvSharp.Mat>? LiveFrameReady;

        /// <summary>
        /// Raised after an inspection result is recorded (ok/ng).
        /// Provides (cameraName, ok, currentImage, stepNumber, correlationKey) for dashboard
        /// tracking and image saving/upload. stepNumber is the inspected step's Sequence (0 if none);
        /// correlationKey ties the Web results upload to the image upload (null if none).
        /// </summary>
        public event Action<string, bool, BitmapSource?, int, string?>? InspectionCompleted;

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
        public void SetInspectionResult(bool ok, string? correlationKey = null)
        {
            InspectionOk = ok;
            IsInspected = true;
            InspectionCount++;
            if (ok) PassCount++;
            else FailCount++;

            // 방금 검사한 스텝 번호(Sequence) — 이미지 파일명 규칙의 Step 토큰용. 없으면 0.
            int stepNumber = FindCurrentStep()?.Sequence ?? 0;
            InspectionCompleted?.Invoke(Name, ok, CurrentImage, stepNumber, correlationKey);
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
        private async Task ToggleControlBox()
        {
            IsControlBoxOpen = !IsControlBoxOpen;
            if (IsControlBoxOpen)
                await RefreshCameraCurrentSettingsAsync();
        }

        /// <summary>연결된 카메라에서 현재 2D 노출/게인을 읽어 설정 창에 표시 (미지원/미연결이면 숨김)</summary>
        private async Task RefreshCameraCurrentSettingsAsync()
        {
            CameraCurrentSettingsText = null;
            var acquisition = _acquisition;
            if (acquisition == null || !acquisition.IsConnected) return;

            var current = await acquisition.ReadSettingsAsync();
            if (current != null)
                CameraCurrentSettingsText =
                    $"카메라 현재값: 노출 {current.ExposureUs / 1000.0:N1} ms · 게인 {current.Gain:N1} dB";
        }

        [RelayCommand]
        private void CloseControlBox()
        {
            IsControlBoxOpen = false;
        }

        /// <summary>SDK 미탑재로 시뮬레이션 폴백된 연결인지 (UI 경고 표시용).</summary>
        private bool _isSimulationFallback;

        /// <summary>
        /// 취득 구현체 생성 + SDK 미탑재 폴백 시 결과 메시지에 경고 표기.
        /// 조용한 폴백은 현장에서 시뮬레이션 영상을 실카메라로 오인하게 한다 (2026-08 Basler 사태).
        /// </summary>
        private Camera.Interfaces.ICameraAcquisition CreateAcquisition()
        {
            var creation = CameraAcquisitionFactory.CreateWithInfo(ToCameraInfo());
            _isSimulationFallback = creation.IsSimulationFallback;
            if (creation.IsSimulationFallback)
                ResultMessage = $"⚠ {creation.FallbackReason}";
            return creation.Acquisition;
        }

        private string ConnectedLabel()
            => _isSimulationFallback
                ? $"Connected (SIMULATION — SDK 미탑재): {Name}"
                : $"Connected: {Name}";

        /// <summary>테스트 전용 — 취득 구현체 주입 (InternalsVisibleTo).</summary>
        internal void SetAcquisitionForTest(Camera.Interfaces.ICameraAcquisition acquisition)
            => _acquisition = acquisition;

        /// <summary>
        /// 카메라 연결 초기화. 앱 시작 시 호출.
        /// </summary>
        public async Task InitializeConnectionAsync()
        {
            try
            {
                _acquisition = CreateAcquisition();
                var connected = await _acquisition.ConnectAsync(ToCameraInfo());
                IsConnected = connected;
                ResultMessage = connected
                    ? ConnectedLabel()
                    : $"Connection failed: {Name}";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ResultMessage = $"Connection error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task GrabAsync()
        {
            await GrabOnceAsync();
        }

        /// <summary>
        /// 단발 grab 을 수행하고 실제 획득 성공 여부를 반환한다.
        /// Auto Run 의 grabFunc 가 이 반환값으로 grab 실패를 판정한다 —
        /// 실패가 은폐되면 SequenceEngine 이 직전 프레임(_originalImage)으로
        /// 검사를 계속 진행한다 (2026-08-19 현장: 케이블 뽑힘 상태에서 검사 지속).
        /// </summary>
        public async Task<bool> GrabOnceAsync()
        {
            try
            {
                _acquisition ??= CreateAcquisition();

                if (!_acquisition.IsConnected)
                {
                    var connected = await _acquisition.ConnectAsync(ToCameraInfo());
                    if (!connected)
                    {
                        ResultMessage = "Camera connection failed";
                        return false;
                    }
                    IsConnected = true;
                }

                // 선택 스텝의 노출/게인을 카메라에 반영 후 촬영.
                // 미구현 제조사는 ICameraAcquisition 기본 no-op (#188).
                // "카메라 설정 유지"(기본) 스텝은 push 하지 않는다 — 카메라 튜닝값 보존.
                var step = SelectedStep;
                if (step != null && !step.Use2DCameraDefault)
                    await _acquisition.ApplySettingsAsync(step.Exposure, step.Gain);

                var result = await _acquisition.AcquireAsync();
                if (result.Success)
                {
                    FrameAcquired?.Invoke(result);

                    // Grab 은 보고 있던 탭을 바꾸지 않는다 — 2D+점군이 함께 오는 3D 카메라에서
                    // 매번 Depth Map 탭으로 튕기던 문제(현장 보고). 데이터만 갱신하면
                    // 현재 탭(2D/Depth Map/Point Cloud)이 알아서 새 내용을 표시한다.
                    if (result.Image2D != null)
                    {
                        var bmp = MatToBitmapSource(result.Image2D);
                        _originalImage = bmp;
                        CurrentImage = bmp;
                    }

                    if (result.PointCloud != null)
                    {
                        var old = CurrentPointCloud;
                        CurrentPointCloud = result.PointCloud;
                        old?.Dispose();
                    }

                    ResultMessage = "Acquisition OK";
                    return true;
                }

                ResultMessage = result.Message;
                return false;
            }
            catch (Exception ex)
            {
                ResultMessage = $"Grab error: {ex.Message}";
                return false;
            }
        }

        public async Task StartLiveGrabAsync()
        {
            if (IsLiveGrabbing) return;

            try
            {
                _acquisition ??= CreateAcquisition();

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

                _liveGrabCts = new CancellationTokenSource();
                var ct = _liveGrabCts.Token;
                IsLiveGrabbing = true;

                // Live 모드: stride=2 로 다운샘플링 (포인트 1/4)
                _acquisition.DownsampleStride = 2;

                _liveGrabTask = Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // 매 프레임 선택 스텝의 노출/게인 반영 — 라이브 중 슬라이더
                        // 조작이 실시간 적용됨 (VisionSetup Camera Live 와 동일, #199).
                        // "카메라 설정 유지"(기본) 스텝은 push 하지 않는다 — 카메라 튜닝값 보존.
                        var liveStep = SelectedStep;
                        if (liveStep != null && !liveStep.Use2DCameraDefault)
                            await _acquisition.ApplySettingsAsync(liveStep.Exposure, liveStep.Gain);

                        var result = await _acquisition.AcquireAsync();
                        if (result.Success)
                        {
                            FrameAcquired?.Invoke(result);

                            BitmapSource? bmp = null;
                            if (result.Image2D != null)
                            {
                                // Roller inspection: 원본 Mat을 Dispose 전에 전달
                                LiveFrameReady?.Invoke(result.Image2D);

                                if (!SuppressLiveDisplay)
                                    bmp = MatToBitmapSource(result.Image2D);

                                result.Image2D.Dispose();
                            }

                            var pointCloud = !SuppressLiveDisplay ? result.PointCloud : null;

                            if (bmp != null || pointCloud != null)
                            {
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (bmp != null)
                                        CurrentImage = bmp;
                                    if (pointCloud != null)
                                    {
                                        var old = CurrentPointCloud;
                                        CurrentPointCloud = pointCloud;
                                        old?.Dispose();
                                    }
                                });
                            }
                        }
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                ResultMessage = $"Live grab error: {ex.Message}";
                IsLiveGrabbing = false;
            }
        }

        public async Task StopLiveGrabAsync()
        {
            if (!IsLiveGrabbing) return;

            _liveGrabCts?.Cancel();
            if (_liveGrabTask != null)
            {
                try
                {
                    await _liveGrabTask;
                }
                catch (OperationCanceledException) { }
            }

            _liveGrabCts?.Dispose();
            _liveGrabCts = null;
            _liveGrabTask = null;
            IsLiveGrabbing = false;

            // 전체 해상도 복원
            if (_acquisition != null)
                _acquisition.DownsampleStride = 1;
        }

        private bool CanSavePointCloud() => CurrentPointCloud != null && !IsLiveGrabbing;

        /// <summary>
        /// Grab 으로 획득한 3D 데이터를 .vpc 파일로 저장.
        /// VisionSetup 의 LoadPointCloud 와 동일 포맷(VMS.Camera.Models.PointCloudData)이라
        /// 저장 파일을 VisionSetup 에서 열어 레시피/툴 설정에 사용할 수 있다.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSavePointCloud))]
        private void SavePointCloud()
        {
            if (_dialogService == null || CurrentPointCloud == null) return;

            var safeName = string.Join("_", Name.Split(System.IO.Path.GetInvalidFileNameChars(),
                StringSplitOptions.RemoveEmptyEntries));
            var defaultName = $"{(safeName.Length > 0 ? safeName : "Camera")}_{DateTime.Now:yyyyMMdd_HHmmss}";

            var filePath = _dialogService.ShowSaveFileDialog(
                "VPC Point Cloud (*.vpc)|*.vpc", ".vpc", defaultName);
            if (filePath == null) return;

            try
            {
                CurrentPointCloud.SaveToFile(filePath);
                ResultMessage = $"3D data saved: {System.IO.Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                ResultMessage = $"3D save failed: {ex.Message}";
            }
        }

        partial void OnCurrentPointCloudChanged(PointCloudData? value)
            => SavePointCloudCommand.NotifyCanExecuteChanged();

        partial void OnIsLiveGrabbingChanged(bool value)
            => SavePointCloudCommand.NotifyCanExecuteChanged();

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
            // 이미지가 없으면 검사 불능 = NG. 과거에는 OK 를 반환해 카메라 미획득
            // 상태에서도 양품 판정이 나갔다 (2026-08-19 현장).
            if (_inspectionService == null || (_originalImage ?? CurrentImage) == null)
            {
                LastExecutionTimeMs = 0;
                SetInspectionResult(false);
                ResultMessage = "No image to inspect";
                return;
            }

            // Find matching inspection step for this camera + current step index
            var step = FindCurrentStep();
            if (step == null || step.Tools.Count == 0)
            {
                LastExecutionTimeMs = 0;
                SetInspectionResult(true);
                return;
            }

            // 항상 원본 이미지로 검사 (오버레이가 그려진 이미지 사용 방지)
            var sourceImage = _originalImage ?? CurrentImage!;
            var mat = BitmapSourceToMat(sourceImage);
            if (mat == null)
            {
                LastExecutionTimeMs = 0;
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

                // InspectionCompleted(→ 대시보드 처리 시간 표시)가 SetInspectionResult 안에서
                // 발생하므로 실행 시간을 먼저 반영해야 이번 검사 값이 전달된다
                LastExecutionTimeMs = result.ExecutionTimeMs;
                SetInspectionResult(result.Success, result.CorrelationKey);
                ResultMessage = result.Success ? "OK" : "NG";
                UpdateToolRunResults(result);
            }
            catch (Exception ex)
            {
                LastExecutionTimeMs = 0;
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
            if (camConfig == null)
            {
                // 구성에 없는 카메라(기본 카메라 또는 AppSetup 에서 재등록되어 id 가 바뀐
                // 카메라)는 저장 대상이 없다 — 무증상으로 버려지면 재시작 후 초기값으로
                // 보이는 현장 미스터리가 된다 (2026-08-10). 반드시 통지.
                ResultMessage = "Save failed: camera not found in system config — run AppSetup";
                return;
            }

            camConfig.Steps.Clear();
            foreach (var step in Steps)
            {
                camConfig.Steps.Add(new StepConfiguration
                {
                    StepNumber = step.StepNumber,
                    Name = step.Name,
                    Use2DCameraDefault = step.Use2DCameraDefault,
                    Exposure = step.Exposure,
                    Gain = step.Gain
                });
            }
            camConfig.StepCount = camConfig.Steps.Count;

            ResultMessage = _configService.SaveSystemConfiguration(config)
                ? "Camera settings saved"
                : "Camera settings save failed";
        }

        private CameraInfo ToCameraInfo()
        {
            // Frame Grabber: BoardType을 ConnectionString으로 사용
            var isFrameGrabber = Manufacturer == CameraManufacturer.Matrox || Manufacturer == CameraManufacturer.Dalsa;
            return new CameraInfo
            {
                Id = Id,
                Name = Name,
                Manufacturer = Manufacturer.ToString().Replace("_", " "),
                ConnectionString = isFrameGrabber && !string.IsNullOrEmpty(BoardType) ? BoardType : IpAddress,
                CameraType = CameraType,
                BoardNumber = BoardNumber,
                DigitizerNumber = DigitizerNumber,
                DcfFilePath = DcfFilePath,
                ScanLength = ScanLength,
                LineRate = LineRate,
                TriggerSource = TriggerSource
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

            // IntPtr에서 직접 BitmapSource 생성 (byte[] 중간 복사 제거)
            var bitmapSource = BitmapSource.Create(
                mat.Width, mat.Height, 96, 96, format, null, mat.Data, stride * mat.Height, stride);
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
                CameraType = config.CameraType,
                IsEnabled = config.IsEnabled,
                BoardType = config.BoardType,
                BoardNumber = config.BoardNumber,
                DigitizerNumber = config.DigitizerNumber,
                DcfFilePath = config.DcfFilePath,
                ScanLength = config.ScanLength,
                LineRate = config.LineRate,
                TriggerSource = config.TriggerSource.ToString()
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
                        Use2DCameraDefault = step.Use2DCameraDefault,
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

        // 2D 노출/게인 카메라 설정 유지 — true(기본)면 Grab/Live 때 카메라의 현재
        // 노출/게인을 건드리지 않는다 (Mech-Eye Viewer 등에서 튜닝한 값 보존)
        [ObservableProperty]
        private bool _use2DCameraDefault = true;

        [ObservableProperty]
        private double _exposure = 5000;

        [ObservableProperty]
        private double _gain = 1.0;

        public override string ToString() => Name;
    }
}
