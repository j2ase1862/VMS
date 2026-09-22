using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.Interfaces;
using VMS.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
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
        private readonly IRecipeService? _recipeService;
        private ICameraAcquisition? _acquisition;
        private Recipe? _currentRecipe;
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

        // ── 스텝 ──
        // 레시피가 로드돼 있으면 이 카메라의 레시피 스텝이 그대로 목록이 된다.
        // 종전에는 system_config.json 의 카메라별 steps/stepCount 로만 만들어서,
        // 스텝이 2개인 레시피를 열어도 화면에는 늘 "Step 1" 하나만 보였다
        // (그 필드가 비어 있으면 기본 1개를 만든다 — 2026-09-22 현장).
        [ObservableProperty]
        private ObservableCollection<InspectionStep> _steps = new();

        [ObservableProperty]
        private InspectionStep? _selectedStep;

        /// <summary>
        /// 실행 대상 스텝의 위치. PLC 시퀀스의 StepChange 신호가 이 값을 바꾸고,
        /// 화면 콤보는 SelectedStep 을 바꾼다 — 둘은 서로를 따라간다.
        /// 종전에는 콤보를 바꿔도 이 값이 그대로여서 늘 첫 스텝만 검사했다.
        /// </summary>
        [ObservableProperty]
        private int _currentStepIndex;

        /// <summary>SelectedStep ↔ CurrentStepIndex 상호 갱신의 재진입 방지.</summary>
        private bool _syncingStepSelection;

        /// <summary>
        /// 레시피가 없을 때 쓰는 스텝 (system_config.json 의 카메라 설정에서 생성).
        /// 레시피를 닫으면 이 목록으로 돌아간다.
        /// </summary>
        private readonly List<InspectionStep> _configSteps = new();

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

        partial void OnSelectedStepChanged(InspectionStep? value)
        {
            OnPropertyChanged(nameof(Exposure));
            OnPropertyChanged(nameof(ExposureMs));
            OnPropertyChanged(nameof(Gain));
            OnPropertyChanged(nameof(Use2DCameraDefault));
            OnPropertyChanged(nameof(IsManualExposureEnabled));

            if (_syncingStepSelection) return;
            var index = value == null ? -1 : Steps.IndexOf(value);
            if (index < 0) return;

            _syncingStepSelection = true;
            try { CurrentStepIndex = index; }
            finally { _syncingStepSelection = false; }
        }

        partial void OnCurrentStepIndexChanged(int value)
        {
            if (_syncingStepSelection) return;
            if (value < 0 || value >= Steps.Count) return;

            _syncingStepSelection = true;
            try { SelectedStep = Steps[value]; }
            finally { _syncingStepSelection = false; }
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
        /// Provides (cameraName, ok, displayImage, originalImage, stepNumber, correlationKey).
        /// displayImage 는 결과 그래픽(오버레이)이 그려진 화면용, originalImage 는 검사에 넣은 오버레이 전 원본.
        /// 학습 데이터(MLOps 수집)는 반드시 originalImage 를 써야 한다 — 오버레이가 찍힌 사진으로 학습하면 모델이
        /// 그림을 배운다 (2026-09-10 실증: 라인에서 올라온 사진에 결과 그래픽이 들어 있었다).
        /// stepNumber is the inspected step's Sequence (0 if none); correlationKey ties the Web results upload
        /// to the image upload (null if none).
        /// </summary>
        public event Action<string, bool, BitmapSource?, BitmapSource?, int, string?>? InspectionCompleted;

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
            IInspectionService? inspectionService = null, IRecipeService? recipeService = null)
        {
            _dialogService = dialogService;
            _configService = configService;
            _inspectionService = inspectionService;
            _recipeService = recipeService;
        }

        public void SetRecipe(Recipe? recipe)
        {
            _currentRecipe = recipe;
            // 레시피 캘리브레이션은 스텝 밖에 있으므로 실행 엔진에 따로 알려 줘야 한다
            _inspectionService?.SetRecipeContext(recipe);
            _inspectionService?.ClearCache();
            RebuildSteps();
        }

        /// <summary>
        /// 스텝 목록을 현재 레시피 기준으로 다시 만든다 — 이 카메라의 스텝만, 순번대로.
        /// 레시피가 없거나 이 카메라를 쓰는 스텝이 없으면 카메라 설정(system_config)에서
        /// 만든 스텝으로 돌아간다 (노출/게인 조작은 레시피 없이도 가능해야 한다).
        /// 선택은 스텝 Id 로 유지 — 레시피를 저장만 하고 다시 로드한 경우 선택이 튀지 않는다.
        ///
        /// 주의: 여기서 StepNaming.RecomputeNames 를 부르지 말 것. 그 함수는 Sequence 를
        /// 1..n 으로 재부여하는데, 검사 이미지 파일명의 Step 토큰이 Sequence 라서 VMS 가
        /// 이름을 다시 매기면 저장된 이력과 어긋난다. 이름 편집 권한은 VisionSetup 에 있다.
        /// </summary>
        private void RebuildSteps()
        {
            var previousId = SelectedStep?.Id;

            var recipeSteps = _currentRecipe?.Steps
                .Where(s => s.CameraId == Id)
                .OrderBy(s => s.Sequence)
                .ToList();

            var next = recipeSteps is { Count: > 0 } ? recipeSteps : _configSteps;

            Steps.Clear();
            foreach (var step in next)
                Steps.Add(step);

            _syncingStepSelection = true;
            try
            {
                SelectedStep = Steps.FirstOrDefault(s => s.Id == previousId) ?? Steps.FirstOrDefault();
                CurrentStepIndex = SelectedStep == null ? 0 : Steps.IndexOf(SelectedStep);
            }
            finally
            {
                _syncingStepSelection = false;
            }
        }

        /// <summary>
        /// 카메라 설정(system_config)에서 온 폴백 스텝을 등록. FromConfiguration 전용.
        /// </summary>
        internal void SetConfigSteps(IEnumerable<InspectionStep> steps)
        {
            _configSteps.Clear();
            _configSteps.AddRange(steps);
            RebuildSteps();
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
            // CurrentImage 는 검사 직후 OverlayImage 로 바뀌어 있다. 원본은 _originalImage (없으면 화면 이미지가 곧 원본).
            InspectionCompleted?.Invoke(Name, ok, CurrentImage, _originalImage ?? CurrentImage, stepNumber, correlationKey);
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
            creation.Acquisition.ConnectionLost += OnAcquisitionConnectionLost;
            return creation.Acquisition;
        }

        /// <summary>
        /// 런타임 연결 끊김(케이블 분리 등) — IsConnected 를 내려 UI 배지를 갱신하고,
        /// 다음 Grab/Live 의 lazy 재연결 분기가 실제로 동작하게 한다 (2026-08-19 현장).
        /// </summary>
        private void OnAcquisitionConnectionLost(object? sender, string reason)
        {
            void Apply()
            {
                IsConnected = false;
                ResultMessage = $"카메라 연결 끊김: {reason}";
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Apply();
            else
                dispatcher.InvokeAsync(Apply);
        }

        private string ConnectedLabel()
            => _isSimulationFallback
                ? $"Connected (SIMULATION — SDK 미탑재): {Name}"
                : $"Connected: {Name}";

        /// <summary>테스트 전용 — 취득 구현체 주입 (InternalsVisibleTo).</summary>
        internal void SetAcquisitionForTest(Camera.Interfaces.ICameraAcquisition acquisition)
        {
            _acquisition = acquisition;
            acquisition.ConnectionLost += OnAcquisitionConnectionLost;
        }

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

                        // BitmapSource.Create 가 픽셀을 복사하므로 Mat 은 여기서 소유 종료.
                        // Dispose 없이 두면 네이티브 15MB(5MP BGR)가 grab 마다 누적되는데
                        // GC 는 이 메모리를 보지 못해 회수가 영영 안 된다 — AUTO RUN 이
                        // 사이클당 1프레임씩 새서 수 GB 까지 자랐다 (실증 PC 2026-08-28).
                        result.Image2D.Dispose();
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
                    // 연속 실패 한계 — 실패 분기 없이 돌면 끊긴 카메라로 hot-spin 하며
                    // 조용히 멈춰 있게 된다 (2026-08-19 현장). 끊김이 확인되거나
                    // 실패가 누적되면 라이브를 스스로 중단한다.
                    const int maxConsecutiveFailures = 5;
                    var consecutiveFailures = 0;

                    while (!ct.IsCancellationRequested)
                    {
                        // 매 프레임 선택 스텝의 노출/게인 반영 — 라이브 중 슬라이더
                        // 조작이 실시간 적용됨 (VisionSetup Camera Live 와 동일, #199).
                        // "카메라 설정 유지"(기본) 스텝은 push 하지 않는다 — 카메라 튜닝값 보존.
                        var liveStep = SelectedStep;
                        if (liveStep != null && !liveStep.Use2DCameraDefault)
                            await _acquisition.ApplySettingsAsync(liveStep.Exposure, liveStep.Gain);

                        var result = await _acquisition.AcquireAsync();
                        if (!result.Success)
                        {
                            consecutiveFailures++;
                            if (!_acquisition.IsConnected || consecutiveFailures >= maxConsecutiveFailures)
                            {
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    ResultMessage = $"라이브 중단: {result.Message}";
                                    IsLiveGrabbing = false;
                                });
                                return;
                            }
                            await Task.Delay(200, CancellationToken.None);
                            continue;
                        }

                        consecutiveFailures = 0;
                        if (result.Success)
                        {
                            FrameAcquired?.Invoke(result);

                            // Roller inspection: 원본 Mat을 Dispose 전에 전달
                            if (result.Image2D != null)
                                LiveFrameReady?.Invoke(result.Image2D);

                            var liveMat = !SuppressLiveDisplay ? result.Image2D : null;
                            var pointCloud = !SuppressLiveDisplay ? result.PointCloud : null;

                            if (liveMat != null || pointCloud != null)
                            {
                                // 프레임마다 새 BitmapSource 를 만들면 대형 할당이 GC 회수를
                                // 앞질러 몇 분 뒤 앱이 느려지다 멈춘다 (세연공장 2026-08-27,
                                // VisionSetup #371 과 동일 병리) — UI 스레드에서 표시 비트맵을
                                // 재사용해 픽셀만 덮어쓴다. Mat 소유권은 디스패처 작업이 인수.
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (liveMat != null)
                                    {
                                        UpdateLiveDisplayBitmap(liveMat);
                                        liveMat.Dispose();
                                    }
                                    if (pointCloud != null)
                                    {
                                        var old = CurrentPointCloud;
                                        CurrentPointCloud = pointCloud;
                                        old?.Dispose();
                                    }
                                });
                            }
                            else
                            {
                                result.Image2D?.Dispose();
                                result.PointCloud?.Dispose();
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

            // 검사할 스텝 — 화면에 선택된 레시피 스텝
            var step = FindCurrentStep();
            if (step == null || step.Tools.Count == 0)
            {
                // 검사할 것이 없으면 종전대로 통과시키되, 왜 통과했는지는 남긴다.
                // 조용한 양품은 "검사가 도는 줄 알았는데 안 돌고 있었다"로 이어진다.
                LastExecutionTimeMs = 0;
                SetInspectionResult(true);
                ResultMessage = SelectedStep == null
                    ? "OK (검사할 스텝 없음 — 레시피를 불러오세요)"
                    : $"OK (검사할 도구 없음 — {SelectedStep.DisplayName})";
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

        /// <summary>
        /// 검사할 스텝 — 화면에 선택된 그 스텝이다 (Steps 가 레시피 스텝 자체이므로
        /// 별도 조회가 필요 없다). 레시피 밖의 폴백 스텝이거나 비활성 스텝이면 null.
        /// </summary>
        private InspectionStep? FindCurrentStep()
        {
            var step = SelectedStep;
            if (step == null || !step.IsEnabled) return null;

            // 폴백(카메라 설정) 스텝은 검사 대상이 아니다 — 도구가 없다.
            return _currentRecipe?.Steps.Contains(step) == true ? step : null;
        }

        /// <summary>internal — 행 보폭 처리를 직접 검증하기 위한 테스트 시임.</summary>
        internal static Mat? BitmapSourceToMat(BitmapSource source)
        {
            try
            {
                // Convert to Bgr24 format
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;

                // WPF 의 행 보폭은 4바이트 정렬(stride), Mat 의 행 보폭은 width*3 이라
                // 폭이 4의 배수가 아니면 두 값이 다르다. 종전에는 정렬된 버퍼를 Mat 에
                // 통째로 평탄 복사해서, 그런 폭에서는 행이 한 칸씩 밀려 영상이 사선으로
                // 찌그러진 채 검사에 들어갔다. 행 단위로 옮긴다.
                int srcStride = (width * 3 + 3) & ~3;
                byte[] pixels = new byte[srcStride * height];
                converted.CopyPixels(pixels, srcStride, 0);

                var mat = new Mat(height, width, MatType.CV_8UC3);
                int dstStride = (int)mat.Step();
                int rowBytes = width * 3;
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * srcStride, mat.Data + y * dstStride, rowBytes);
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
            // 레시피 스텝을 편집 중이면 저장 대상은 레시피다 — 노출/게인이 레시피에
            // 들어 있는데 system_config 에 쓰면 다음 로드 때 레시피 값에 덮여 사라진다.
            if (_currentRecipe != null && SelectedStep != null && _currentRecipe.Steps.Contains(SelectedStep))
            {
                if (_recipeService == null)
                {
                    ResultMessage = "Save failed: recipe service unavailable";
                    return;
                }

                ResultMessage = _recipeService.SaveRecipe(_currentRecipe)
                    ? $"Recipe step saved: {SelectedStep.DisplayName}"
                    : "Recipe step save failed";
                return;
            }

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
                    StepNumber = step.Sequence,
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

        // 라이브 표시 비트맵 재사용 — 같은 크기/형식이면 WritePixels 로 덮어쓴다.
        // WriteableBitmap 은 UI 스레드 소유이므로 반드시 디스패처에서 호출.
        private WriteableBitmap? _liveDisplayBitmap;
        private MatType _liveDisplayMatType;

        private void UpdateLiveDisplayBitmap(Mat mat)
        {
            if (_liveDisplayBitmap == null
                || _liveDisplayBitmap.PixelWidth != mat.Width
                || _liveDisplayBitmap.PixelHeight != mat.Height
                || _liveDisplayMatType != mat.Type())
            {
                _liveDisplayBitmap = mat.ToWriteableBitmap();
                _liveDisplayMatType = mat.Type();
                CurrentImage = _liveDisplayBitmap;
            }
            else
            {
                // 같은 인스턴스에 WritePixels — 바인딩 교체 없이 화면 자동 무효화
                OpenCvSharp.WpfExtensions.WriteableBitmapConverter.ToWriteableBitmap(mat, _liveDisplayBitmap);
                if (!ReferenceEquals(CurrentImage, _liveDisplayBitmap))
                    CurrentImage = _liveDisplayBitmap;
            }
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
            IInspectionService? inspectionService = null,
            IRecipeService? recipeService = null)
        {
            var vm = new CameraViewModel(dialogService, configService, inspectionService, recipeService)
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

            // 카메라 설정의 스텝 — 레시피가 없을 때만 쓰는 폴백이다.
            // 레시피를 열면 SetRecipe → RebuildSteps 가 레시피 스텝으로 교체한다.
            vm.SetConfigSteps(BuildConfigSteps(config));

            return vm;
        }

        /// <summary>
        /// system_config 의 카메라 스텝 → 폴백 스텝. 설정에 스텝이 없으면
        /// StepCount(최소 1)만큼 기본 스텝을 만든다.
        /// </summary>
        private static List<InspectionStep> BuildConfigSteps(CameraConfiguration config)
        {
            var steps = new List<InspectionStep>();

            if (config.Steps != null && config.Steps.Count > 0)
            {
                foreach (var step in config.Steps)
                {
                    steps.Add(new InspectionStep
                    {
                        Sequence = step.StepNumber,
                        Name = step.Name,
                        CameraId = config.Id,
                        Use2DCameraDefault = step.Use2DCameraDefault,
                        Exposure = step.Exposure,
                        Gain = step.Gain
                    });
                }
                return steps;
            }

            var stepCount = Math.Max(1, config.StepCount);
            for (int i = 1; i <= stepCount; i++)
            {
                steps.Add(new InspectionStep
                {
                    Sequence = i,
                    Name = $"Step {i}",
                    CameraId = config.Id,
                    Exposure = 5000,
                    Gain = 1.0
                });
            }
            return steps;
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

}
