using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 캘리브레이션 이미지 소스 종류.
    /// </summary>
    public enum CalibrationImageSource
    {
        /// <summary>디스크에서 이미지 파일 로드</summary>
        File,
        /// <summary>VisionSetup이 카메라에 직접 연결해 한 장 캡처</summary>
        Camera,
        /// <summary>VMS 메인 앱의 SharedFrame(MMF)에서 한 장 수신</summary>
        VmsSharedFrame
    }

    /// <summary>
    /// ComboBox 바인딩용 표시 옵션 (enum + 라벨).
    /// ToString을 라벨로 오버라이드 — EnumComboBoxParameter가 DisplayMemberPath 미지원이라 ToString 사용.
    /// </summary>
    public record ImageSourceOption(CalibrationImageSource Value, string DisplayName)
    {
        public sealed override string ToString() => DisplayName;
    }

    /// <summary>
    /// 카메라 ComboBox 표시용 래퍼. CameraInfo 자체를 건드리지 않기 위해 분리.
    /// </summary>
    public record CameraComboItem(CameraInfo Camera, string DisplayName)
    {
        public sealed override string ToString() => DisplayName;
    }

    /// <summary>
    /// 캘리브레이션 모드 ComboBox 표시 옵션.
    /// </summary>
    public record CalibrationModeOption(CalibrationMode Value, string DisplayName)
    {
        public sealed override string ToString() => DisplayName;
    }

    /// <summary>
    /// [Apply to Current Recipe] 가 건드릴 스텝 한 줄.
    ///
    /// <para>캘리브레이션 결과는 레시피 한 개에 한 벌만 저장되지만, 스텝의 Resolution(mm/px)은
    /// 스텝마다 따로 있다 — 한 레시피에 카메라가 여러 대면 렌즈·작동거리가 달라 같은 값을
    /// 모든 스텝에 밀어 넣으면 틀린다. 그래서 어느 스텝을 갱신할지 누르기 전에 고르게 하고,
    /// 무엇이 어떤 값으로 바뀌는지 미리 보여준다.</para>
    /// </summary>
    public partial class StepApplyTarget : ObservableObject
    {
        public StepApplyTarget(InspectionStep step, string cameraName)
        {
            Step = step;
            CameraName = cameraName;
        }

        /// <summary>레시피가 들고 있는 그 인스턴스 그대로 — 여기에 쓰면 왼쪽 Steps 표가 바로 바뀐다.</summary>
        public InspectionStep Step { get; }

        public string StepName => Step.DisplayName;
        public string CameraName { get; }

        /// <summary>지금 스텝에 들어 있는 값 (적용 후 Refresh 로 다시 읽는다).</summary>
        public double CurrentResolution => Step.Resolution;

        [ObservableProperty] private bool _isSelected;

        /// <summary>적용하면 들어갈 값 = 이번 계산의 Pixel Size.</summary>
        [ObservableProperty] private double _newResolution;

        public string ChangeText => NewResolution > 0
            ? $"{CurrentResolution:F5} → {NewResolution:F5}"
            : $"{CurrentResolution:F5} → (계산 전)";

        partial void OnNewResolutionChanged(double value) => OnPropertyChanged(nameof(ChangeText));

        /// <summary>적용 후 "현재 값" 표시를 다시 읽는다.</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(CurrentResolution));
            OnPropertyChanged(nameof(ChangeText));
        }
    }

    /// <summary>
    /// Camera Calibration Manager 윈도우의 ViewModel.
    /// 이미지 소스 3종(파일/카메라 직접/VMS Shared Frame) → 매개변수 설정 → Run → Apply to Recipe.
    /// VMS가 카메라를 점유 중일 때 카메라 직접 소스는 자동 비활성화.
    /// </summary>
    public partial class CalibrationManagerViewModel : ObservableObject, IDisposable
    {
        private readonly CalibrationService _calibrationService = new();
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private Mat? _sourceImage;
        private SharedFrameReader? _sharedFrameReader;
        private GrabRequestChannel? _grabRequestChannel;
        private ICameraAcquisition? _cameraAcquisition;
        private CameraInfo? _connectedCameraInfo;

        public CalibrationManagerViewModel(IRecipeService recipeService, IDialogService dialogService)
        {
            _recipeService = recipeService;
            _dialogService = dialogService;

            // 카메라 레지스트리 로드 → ComboBox 표시용 래퍼 컬렉션
            var camSvc = CameraService.Instance;
            camSvc.LoadCameraRegistry();
            CameraOptions = camSvc.GetEnabledCameras()
                .Select(c => new CameraComboItem(c, c.Name))
                .ToList();
            SelectedCameraOption = CameraOptions.FirstOrDefault();

            // 첫 옵션을 기본 선택 (File, Checkerboard)
            SelectedSourceOption = AvailableSources.FirstOrDefault();
            SelectedModeOption = AvailableModes.FirstOrDefault();

            // VMS Shared Frame Writer 가용성 체크 (현재 VMS 실행 중인지)
            RefreshSourceAvailability();

            // VisionService에 이미 로드된 이미지가 있으면 출발점으로 사용 (File 소스)
            var current = VisionService.Instance.CurrentImage;
            if (current != null && !current.Empty())
            {
                _sourceImage = current.Clone();
                UpdatePreview(_sourceImage);
            }

            // 현재 Recipe에 저장된 캘리브레이션이 있으면 결과 영역에 표시
            var existing = _recipeService.CurrentRecipe?.Calibration;
            if (existing != null)
            {
                ReprojectionError = existing.ReprojectionError;
                PixelSizeMm = existing.PixelSizeMm;
                StatusMessage = $"Loaded existing calibration from recipe ({existing.CalibratedAt.ToLocalTime():yyyy-MM-dd HH:mm}).";
                _lastResultMetadata = existing;
                HasResult = true;
            }

            // 적용하면 무엇이 바뀌는지 미리 보여준다 — 창을 연 시점의 레시피 스텝 목록.
            RebuildApplyTargets();
            RefreshAppliedSummary();
        }

        // ── 이미지 소스 ──
        [ObservableProperty] private bool _isVmsWriterAlive;
        [ObservableProperty] private string _availabilityMessage = string.Empty;

        /// <summary>ComboBox 표시용 — enum + 사람이 읽을 수 있는 라벨.</summary>
        public IReadOnlyList<ImageSourceOption> AvailableSources { get; } = new[]
        {
            new ImageSourceOption(CalibrationImageSource.File, "File"),
            new ImageSourceOption(CalibrationImageSource.Camera, "Camera (direct)"),
            new ImageSourceOption(CalibrationImageSource.VmsSharedFrame, "VMS Shared Frame")
        };

        /// <summary>EnumComboBoxParameter는 SelectedItem 기반이라 옵션 객체를 그대로 받는 프로퍼티가 필요.</summary>
        private ImageSourceOption? _selectedSourceOption;
        public ImageSourceOption? SelectedSourceOption
        {
            get => _selectedSourceOption;
            set
            {
                if (SetProperty(ref _selectedSourceOption, value) && value != null)
                {
                    OnPropertyChanged(nameof(IsFileSource));
                    OnPropertyChanged(nameof(IsCameraSource));
                    OnPropertyChanged(nameof(IsVmsSharedFrameSource));
                }
            }
        }

        public CalibrationImageSource SelectedSource =>
            SelectedSourceOption?.Value ?? CalibrationImageSource.File;

        public IReadOnlyList<CameraComboItem> CameraOptions { get; }
        [ObservableProperty] private CameraComboItem? _selectedCameraOption;
        private CameraInfo? SelectedCamera => SelectedCameraOption?.Camera;

        [ObservableProperty] private bool _isCapturing;

        public bool IsFileSource => SelectedSource == CalibrationImageSource.File;
        public bool IsCameraSource => SelectedSource == CalibrationImageSource.Camera;
        public bool IsVmsSharedFrameSource => SelectedSource == CalibrationImageSource.VmsSharedFrame;

        // ── 캘리브레이션 모드 ──
        public IReadOnlyList<CalibrationModeOption> AvailableModes { get; } = new[]
        {
            new CalibrationModeOption(CalibrationMode.Checkerboard, "Checkerboard"),
            new CalibrationModeOption(CalibrationMode.CirclesGrid, "Circles Grid (dots)"),
            new CalibrationModeOption(CalibrationMode.NPointToNPoint, "N-Point (planar)"),
            new CalibrationModeOption(CalibrationMode.SinglePointScale, "Single Scale (2 points)")
        };

        private CalibrationModeOption? _selectedModeOption;
        public CalibrationModeOption? SelectedModeOption
        {
            get => _selectedModeOption;
            set
            {
                if (SetProperty(ref _selectedModeOption, value) && value != null)
                {
                    OnPropertyChanged(nameof(SelectedMode));
                    OnPropertyChanged(nameof(IsCheckerboardMode));
                    OnPropertyChanged(nameof(IsCirclesGridMode));
                    OnPropertyChanged(nameof(IsNPointMode));
                    OnPropertyChanged(nameof(IsSingleScaleMode));
                    RedrawOverlay();
                }
            }
        }

        public CalibrationMode SelectedMode =>
            SelectedModeOption?.Value ?? CalibrationMode.Checkerboard;

        public bool IsCheckerboardMode => SelectedMode == CalibrationMode.Checkerboard;
        public bool IsCirclesGridMode => SelectedMode == CalibrationMode.CirclesGrid;
        public bool IsNPointMode => SelectedMode == CalibrationMode.NPointToNPoint;
        public bool IsSingleScaleMode => SelectedMode == CalibrationMode.SinglePointScale;

        // ── 파라미터 (Circles Grid) ──
        // 행·열은 체커보드와 공유한다 (같은 "몇 개짜리 패턴인가" 질문이라, 따로 두면 모드를 오갈 때
        // 값을 두 번 맞춰야 한다). 간격과 배열 종류만 이 모드 전용이다.
        [ObservableProperty] private double _circleSpacingMm = 20.0;
        [ObservableProperty] private bool _asymmetricCircles = true;

        // ── 파라미터 (Checkerboard) ──
        [ObservableProperty] private int _patternCols = 9;
        [ObservableProperty] private int _patternRows = 6;
        [ObservableProperty] private double _squareSizeMm = 5.0;
        [ObservableProperty] private bool _accumulateMultiView;

        // ── 파라미터 (N-Point) ──
        public System.Collections.ObjectModel.ObservableCollection<CalibPoint> NPointPoints { get; } = new();

        // ── 파라미터 (Single Scale) ──
        [ObservableProperty] private double _knownLengthMm = 10.0;
        private Point2d? _scalePoint1;
        private Point2d? _scalePoint2;
        public string ScalePoint1Display => _scalePoint1 is { } p ? $"({p.X:F1}, {p.Y:F1})" : "(click on image)";
        public string ScalePoint2Display => _scalePoint2 is { } p ? $"({p.X:F1}, {p.Y:F1})" : "(click on image)";

        // ── 결과 ──
        [ObservableProperty] private double _reprojectionError;
        [ObservableProperty] private double _pixelSizeMm;
        [ObservableProperty] private string _statusMessage = "Choose an image source and load a checkerboard image.";
        [ObservableProperty] private BitmapSource? _previewImage;
        [ObservableProperty] private string _sourceImagePath = string.Empty;
        [ObservableProperty] private bool _hasResult;

        private CalibrationMetadata? _lastResultMetadata;

        public int AccumulatedViewCount => _calibrationService.AccumulatedViewCount;

        // ── 적용 (Apply) ──
        // 이 창이 [Apply to Current Recipe] 로 실제로 바꾸는 것은 두 곳이다.
        //   ① recipe.Calibration  — 측정 도구의 mm 판정 + Image Rectify 의 왜곡 보정이 쓴다
        //   ② 고른 스텝의 Resolution(mm/px) — VisionSetup 왼쪽 Steps 표에 보이는 그 값
        // ②를 안 건드리면 표의 Resolution 은 기본값(0.05) 그대로라 "적용한 게 어디에도 안 보인다"
        // 가 된다 (2026-09-15 dev PC 검증). 어느 스텝을 건드릴지는 아래 목록에서 고른다.

        /// <summary>Resolution 을 갱신할 후보 스텝들 (현재 레시피 전체).</summary>
        public System.Collections.ObjectModel.ObservableCollection<StepApplyTarget> ApplyTargets { get; } = new();

        /// <summary>대상 목록이 비어 있을 때 표시할 안내 (비어 있지 않으면 빈 문자열).</summary>
        [ObservableProperty] private string _applyTargetsHint = string.Empty;

        /// <summary>지금 레시피에 실제로 저장돼 있는 캘리브레이션 요약 — 적용 전/후 확인용.</summary>
        [ObservableProperty] private string _appliedSummary = string.Empty;

        /// <summary>
        /// 이미지를 어느 카메라에서 가져왔는지 (카메라 직접 촬영일 때만 확실). 기본 체크 대상을
        /// 그 카메라의 스텝으로 좁히는 데 쓴다 — 파일/VMS 프레임은 알 수 없어 전체를 고른다.
        /// </summary>
        private string? _sourceCameraId;

        /// <summary>현재 레시피의 스텝으로 적용 대상 목록을 다시 만든다 (기본 선택까지).</summary>
        private void RebuildApplyTargets()
        {
            ApplyTargets.Clear();

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null)
            {
                ApplyTargetsHint = "로드된 레시피가 없습니다 — 결과는 이번 세션에만 적용됩니다.";
                return;
            }

            var cameras = CameraService.Instance.GetAllCameras();
            foreach (var step in recipe.Steps.OrderBy(s => s.Sequence))
            {
                var camName = cameras.FirstOrDefault(c => c.Id == step.CameraId)?.Name ?? "(미지정)";
                ApplyTargets.Add(new StepApplyTarget(step, camName) { NewResolution = PixelSizeMm });
            }

            ApplyTargetsHint = ApplyTargets.Count == 0
                ? "이 레시피에는 스텝이 없습니다 — 캘리브레이션만 저장됩니다."
                : string.Empty;

            ApplyDefaultSelection();
        }

        /// <summary>
        /// 기본 체크 — 촬영한 카메라를 아는 경우 그 카메라의 스텝만, 모르면 전체.
        /// 사용자가 손으로 고친 뒤에는 호출하지 않는다 (촬영/레시피 교체 시점에만).
        /// </summary>
        private void ApplyDefaultSelection()
        {
            foreach (var t in ApplyTargets)
            {
                t.IsSelected = string.IsNullOrEmpty(_sourceCameraId)
                    || string.IsNullOrEmpty(t.Step.CameraId)
                    || t.Step.CameraId == _sourceCameraId;
            }
        }

        /// <summary>계산 결과가 바뀌면 "적용 후 값" 미리보기도 따라간다.</summary>
        partial void OnPixelSizeMmChanged(double value)
        {
            foreach (var t in ApplyTargets)
                t.NewResolution = value;
        }

        /// <summary>레시피에 지금 저장돼 있는 캘리브레이션을 한 줄로 요약.</summary>
        private void RefreshAppliedSummary()
        {
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null)
            {
                AppliedSummary = "레시피 없음 — 적용해도 파일에는 남지 않습니다.";
                return;
            }

            var c = recipe.Calibration;
            AppliedSummary = c == null
                ? $"'{recipe.Name}' 에 저장된 캘리브레이션: 없음"
                : $"'{recipe.Name}' 에 저장됨: {c.Mode} · {c.PixelSizeMm:F5} mm/px · "
                  + $"오차 {c.ReprojectionError:F4} · {c.CalibratedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        }

        [RelayCommand]
        private void SelectAllApplyTargets()
        {
            foreach (var t in ApplyTargets) t.IsSelected = true;
        }

        [RelayCommand]
        private void SelectNoApplyTargets()
        {
            foreach (var t in ApplyTargets) t.IsSelected = false;
        }

        partial void OnPatternColsChanged(int value) { if (value < 2) PatternCols = 2; }
        partial void OnPatternRowsChanged(int value) { if (value < 2) PatternRows = 2; }
        partial void OnSquareSizeMmChanged(double value) { if (value < 0.001) SquareSizeMm = 0.001; }

        /// <summary>
        /// VMS SharedFrame Writer 생존 여부 재검사. UI의 Refresh 버튼이 호출.
        /// Writer가 살아있으면 카메라 직접 연결은 충돌 위험이 있어 안내 메시지로 사용자에게 알림.
        /// </summary>
        [RelayCommand]
        public void RefreshSourceAvailability()
        {
            // 생존 확인은 정적 프로브로만 한다 — 예전처럼 임시 Reader 를 만들었다 버리면
            // 그 Dispose 가 공유 ReaderAlive 신호를 내려, 아직 살아 있는 메인 화면의
            // VMS 프레임 수신까지 함께 끊겼다 (창을 열거나 Refresh 를 누를 때마다).
            IsVmsWriterAlive = SharedFrameReader.IsVmsMainRunning();

            try
            {
                var ownership = WeakReferenceMessenger.Default.Send<MainCameraOwnershipRequestMessage>();
                IsMainWindowHoldingCamera = ownership.HasReceivedResponse && ownership.Response.IsConnected;
            }
            catch
            {
                IsMainWindowHoldingCamera = false;
            }

            var conflicts = new List<string>();
            if (IsVmsWriterAlive)
                conflicts.Add("VMS가 실행 중입니다 — 카메라 소유권은 VMS에 있습니다. 'VMS Shared Frame' 소스를 쓰세요.");
            if (IsMainWindowHoldingCamera)
                conflicts.Add("VisionSetup 메인 화면이 카메라에 연결되어 있습니다 — 촬영 전 메인 화면의 카메라 연결을 끊으세요.");

            AvailabilityMessage = string.Join("\n", conflicts);
            HasSourceConflict = conflicts.Count > 0;
        }

        /// <summary>
        /// VisionSetup 메인 화면이 지금 카메라를 붙들고 있는지.
        ///
        /// <para>산업용 카메라 SDK 는 장치를 배타 점유로 연다(Basler pylon 의 Camera.Open 은
        /// Control|Stream 기본). 메인 화면이 이미 열어 둔 카메라를 이 창이 두 번째로 열면
        /// 연결이 실패해 "눌러도 아무 일이 없는" 것처럼 보인다 — 2026-09-16 현장 보고.</para>
        /// </summary>
        [ObservableProperty] private bool _isMainWindowHoldingCamera;

        /// <summary>선택한 소스로 지금 촬영하면 충돌할 수 있는 상태인지 (안내 배너 표시용).</summary>
        [ObservableProperty] private bool _hasSourceConflict;

        [RelayCommand]
        private void LoadImage()
        {
            var path = _dialogService.ShowOpenFileDialog(
                "Load Calibration Image",
                "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All|*.*");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                var loaded = Cv2.ImRead(path);
                if (loaded.Empty())
                {
                    loaded.Dispose();
                    _dialogService.ShowError($"Cannot read image: {path}", "Load Failed");
                    return;
                }
                ReplaceSourceImage(loaded, path, $"Loaded {Path.GetFileName(path)} ({loaded.Width}x{loaded.Height})");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to load image: {ex.Message}", "Load Failed");
            }
        }

        [RelayCommand]
        private async Task CaptureFromCamera()
        {
            if (SelectedCamera == null)
            {
                _dialogService.ShowWarning("Select a camera first.", "No Camera");
                return;
            }

            if (SharedFrameReader.IsVmsMainRunning())
            {
                // VMS 가 떠 있으면 카메라 소유권은 VMS 에 있다 — 직접 연결은 실패한다.
                StatusMessage = "VMS가 실행 중입니다 — 이미지 소스를 'VMS Shared Frame'으로 바꾸세요.";
                _dialogService.ShowWarning(
                    "VMS 메인 앱이 실행 중이라 카메라를 직접 열 수 없습니다.\n\n" +
                    "이미지 소스를 'VMS Shared Frame' 으로 바꾸고 [Receive Frame from VMS] 를 쓰세요.",
                    "카메라 사용 중");
                RefreshSourceAvailability();
                return;
            }

            IsCapturing = true;
            try
            {
                // ① 메인 화면이 같은 카메라를 이미 쥐고 있으면 그쪽에 촬영을 부탁한다.
                //    두 번째 연결을 만들면 SDK 배타 점유에 걸려 조용히 실패한다.
                if (await TryCaptureViaMainWindowAsync())
                    return;

                // 카메라가 바뀌었거나 미연결이면 새로 연결
                if (_cameraAcquisition == null
                    || !_cameraAcquisition.IsConnected
                    || _connectedCameraInfo?.Id != SelectedCamera.Id)
                {
                    if (_cameraAcquisition != null)
                    {
                        try { await _cameraAcquisition.DisconnectAsync(); } catch { /* swallow */ }
                        _cameraAcquisition.Dispose();
                        _cameraAcquisition = null;
                    }

                    var creation = CameraAcquisitionFactory.CreateWithInfo(SelectedCamera);
                    _cameraAcquisition = creation.Acquisition;
                    if (creation.IsSimulationFallback)
                    {
                        // 캘리브레이션은 실카메라가 전제 — 시뮬레이션 폴백을 명시 (mm 환산 무의미)
                        StatusMessage = $"⚠ {creation.FallbackReason} — 캘리브레이션 결과를 신뢰할 수 없습니다";
                    }
                    var connected = await _cameraAcquisition.ConnectAsync(SelectedCamera);
                    if (!connected)
                    {
                        // 실패 사유를 짚어 준다 — 예전에는 "Connect failed" 한 줄이 우측 패널
                        // 맨 아래에만 떠서, 현장에서는 "눌러도 아무 반응이 없다"로 보였다.
                        RefreshSourceAvailability();
                        string hint =
                            IsMainWindowHoldingCamera
                                ? "VisionSetup 메인 화면이 이 카메라를 쓰고 있습니다 — 메인 화면의 카메라 연결을 끊고 다시 시도하세요."
                            : IsVmsWriterAlive
                                ? "VMS가 카메라를 쓰고 있을 수 있습니다 — 이미지 소스를 'VMS Shared Frame'으로 바꾸세요."
                                : "카메라 연결·케이블·전원을 확인하세요.";
                        StatusMessage = $"카메라 연결 실패: {SelectedCamera.Name} — {hint}";
                        _dialogService.ShowWarning(
                            $"{SelectedCamera.Name} 에 연결하지 못했습니다.\n\n{hint}",
                            "카메라 연결 실패");
                        _cameraAcquisition.Dispose();
                        _cameraAcquisition = null;
                        return;
                    }
                    _connectedCameraInfo = SelectedCamera;
                }

                var result = await _cameraAcquisition.AcquireAsync();
                if (!result.Success || result.Image2D == null || result.Image2D.Empty())
                {
                    StatusMessage = $"Capture failed: {result.Message}";
                    return;
                }

                // 이 카메라의 스텝만 기본 체크되도록 소스를 기억 (다중 카메라 레시피 오적용 방지)
                _sourceCameraId = SelectedCamera.Id;
                ApplyDefaultSelection();

                ReplaceSourceImage(result.Image2D, $"[Camera: {SelectedCamera.Name}]",
                    $"Captured from {SelectedCamera.Name} ({result.Image2D.Width}x{result.Image2D.Height})");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Camera capture failed: {ex.Message}", "Capture Failed");
            }
            finally
            {
                IsCapturing = false;
            }
        }

        /// <summary>
        /// 메인 화면이 같은 카메라를 쥐고 있으면 그쪽에 촬영을 부탁해 결과를 받는다.
        /// 처리했으면 true — 호출자는 직접 연결을 시도하지 않는다.
        /// </summary>
        private async Task<bool> TryCaptureViaMainWindowAsync()
        {
            if (SelectedCamera == null) return false;

            var ownership = WeakReferenceMessenger.Default.Send<MainCameraOwnershipRequestMessage>();
            if (!ownership.HasReceivedResponse) return false;

            var owner = ownership.Response;
            IsMainWindowHoldingCamera = owner.IsConnected;
            if (!owner.IsConnected || owner.CameraId != SelectedCamera.Id) return false;

            StatusMessage = $"메인 화면의 카메라로 촬영 중... ({owner.CameraName})";

            // AsyncRequestMessage 는 그 자체가 awaitable 이라, Send 결과를 변수로 받아야
            // "await 를 빠뜨렸다"(CS4014)는 경고가 나지 않는다.
            var sent = WeakReferenceMessenger.Default.Send(new MainCameraCaptureRequestMessage(SelectedCamera.Id));
            if (!sent.HasReceivedResponse) return false;

            var reply = await sent.Response;
            if (!reply.Success || reply.Image == null || reply.Image.Empty())
            {
                reply.Image?.Dispose();
                StatusMessage = $"촬영 실패: {reply.Message}";
                _dialogService.ShowWarning(reply.Message, "촬영 실패");
                return true;   // 메인 화면이 처리했고 실패했다 — 직접 연결을 또 시도하지 않는다
            }

            // 이 카메라의 스텝만 기본 체크되도록 소스를 기억 (다중 카메라 레시피 오적용 방지)
            _sourceCameraId = SelectedCamera.Id;
            ApplyDefaultSelection();

            ReplaceSourceImage(reply.Image, $"[Camera: {owner.CameraName}]",
                $"Captured from {owner.CameraName} ({reply.Image.Width}x{reply.Image.Height}) — 메인 화면 카메라 공유");
            return true;
        }

        /// <summary>
        /// VMS 에 촬영을 요청하고 그 프레임을 받아온다.
        ///
        /// <para><b>요청까지 해야 하는 이유.</b> 예전에는 공유 메모리를 <b>읽기만</b> 했다. 그런데
        /// VMS 의 Writer 는 받는 쪽이 붙어 있을 때만 프레임을 기록하고(라이브 페이지파일 I/O 방지),
        /// 이 창의 Reader 는 버튼을 누르는 그 순간에야 붙는다 — 그 전에 VMS 가 찍어 둔 사진은
        /// 애초에 기록되지 않았다. 그래서 <b>창을 열고 처음 누르면 늘 빈손</b>이었고, VMS 창으로 가서
        /// Grab 을 누르고 돌아와 다시 눌러야 했다 (2026-09-16 현장 보고 — 메인 화면은 #495 에서
        /// 고쳤는데 이 창만 남아 있었다).</para>
        ///
        /// <para>순서가 중요하다: <b>Reader 를 먼저 붙이고</b> 요청해야 이번 촬영분이 기록된다.</para>
        /// </summary>
        [RelayCommand]
        private async Task ReceiveFromVms()
        {
            IsCapturing = true;
            try
            {
                // ① Reader 를 먼저 붙인다 (요청보다 먼저여야 한다)
                _sharedFrameReader ??= new SharedFrameReader();
                if (!_sharedFrameReader.TryConnect())
                {
                    StatusMessage = "VMS 공유 프레임에 연결할 수 없습니다 (VMS 가 실행 중인지 확인).";
                    _sharedFrameReader.Dispose();
                    _sharedFrameReader = null;
                    RefreshSourceAvailability();
                    return;
                }
                if (!_sharedFrameReader.IsWriterAlive)
                {
                    StatusMessage = "VMS 가 비활성 상태입니다.";
                    RefreshSourceAvailability();
                    return;
                }

                // ② VMS 에 촬영을 요청한다. 운전·라이브 중이면 사유를 그대로 돌려준다.
                var request = await RequestGrabFromVmsAsync();
                if (!ShouldReadSharedFrame(request.Outcome))
                {
                    // 거절·무응답·오류 — 공유 메모리에 남은 프레임을 읽지 않는다.
                    // 운전 중에는 VMS 가 사이클마다 프레임을 기록하므로, 여기서 읽으면
                    // 검사 중인 부품 사진이 캘리브레이션 원본으로 올라가고 거절 사유는
                    // "Received frame" 메시지에 덮여 사라진다 (2026-09-17 v1.40.0 현장 E1).
                    return;
                }
                long? expectedFrame = request.FrameCounter;

                // ③ 방금 기록된 프레임을 읽는다
                var frame = _sharedFrameReader.TryReadFrame(skipIfSameFrame: false);
                if (frame?.Image2D == null || frame.Image2D.Empty())
                {
                    frame?.Image2D?.Dispose();
                    StatusMessage = "VMS 로부터 받은 프레임이 없습니다 — VMS 에서 Grab 을 한 번 실행한 뒤 다시 시도하세요.";
                    return;
                }

                if (expectedFrame is long expected && frame.FrameCounter < expected)
                {
                    // 이번 촬영분이 공유 메모리에 안 실렸다 — 옛 이미지를 새 것인 양 쓰면
                    // 엉뚱한 사진으로 캘리브레이션하게 된다.
                    frame.Image2D.Dispose();
                    StatusMessage = $"VMS 가 새 프레임을 기록하지 않았습니다 "
                                  + $"(기대 #{expected}, 수신 #{frame.FrameCounter}) — 다시 시도하세요.";
                    return;
                }

                // 프레임에 실린 카메라 식별자를 기억해 그 카메라의 스텝만 기본 체크되게 한다
                // (v2 헤더 — 없으면 예전처럼 "모름" 이라 전체 체크).
                if (!string.IsNullOrEmpty(frame.CameraId))
                {
                    _sourceCameraId = frame.CameraId;
                    ApplyDefaultSelection();
                }

                // SharedFrameData가 자체적으로 deep-copied Mat을 들고 있으므로 그대로 양도.
                var img = frame.Image2D;
                ReplaceSourceImage(img, "[VMS Shared Frame]",
                    $"Received frame #{frame.FrameCounter} from VMS ({img.Width}x{img.Height})");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"VMS frame receive failed: {ex.Message}", "Receive Failed");
            }
            finally
            {
                IsCapturing = false;
            }
        }

        /// <summary>VMS 촬영 요청의 결과 구분 — 공유 메모리를 읽어도 되는지의 근거.</summary>
        public enum VmsGrabRequestOutcome
        {
            /// <summary>요청 채널이 없다(구버전 VMS 등) — 예전처럼 공유 메모리의 마지막 프레임을 읽어 본다.</summary>
            NoChannel,
            /// <summary>VMS 가 촬영했고 프레임 번호를 알려 줌 — 그 번호와 대조해 읽는다.</summary>
            Success,
            /// <summary>운전·라이브 중 등 VMS 가 거절 — 사유를 남기고 프레임은 읽지 않는다.</summary>
            Rejected,
            /// <summary>시간 초과 — 읽지 않는다 (직전 프레임을 새 것인 양 쓰지 않기 위해).</summary>
            NoResponse,
            /// <summary>요청 중 예외 — 읽지 않는다.</summary>
            Error,
        }

        public readonly record struct VmsGrabRequestResult(VmsGrabRequestOutcome Outcome, long? FrameCounter);

        /// <summary>
        /// 요청 결과에 따라 공유 메모리 프레임을 읽을지 결정한다. 거절·무응답·오류면 읽지 않는다 —
        /// 운전 중에는 VMS 가 사이클마다 프레임을 기록하므로, 거절됐는데도 읽으면 검사 중인 부품
        /// 사진이 캘리브레이션 원본이 되고 거절 사유는 수신 메시지에 덮인다. 채널이 없는 구버전
        /// VMS 만 예전 읽기 동작을 유지한다.
        /// </summary>
        internal static bool ShouldReadSharedFrame(VmsGrabRequestOutcome outcome)
            => outcome is VmsGrabRequestOutcome.NoChannel or VmsGrabRequestOutcome.Success;

        /// <summary>
        /// VMS 에 Grab 을 요청한다. 성공하면 VMS 가 알려 준 프레임 번호(대조용)를 함께 돌려주고,
        /// 거절·무응답·오류는 사유를 상태줄에 남긴 채 그 구분을 돌려준다 — 호출부는
        /// <see cref="ShouldReadSharedFrame"/> 로 읽기 여부를 정한다.
        /// </summary>
        private async Task<VmsGrabRequestResult> RequestGrabFromVmsAsync()
        {
            try
            {
                _grabRequestChannel ??= new GrabRequestChannel();
                if (!_grabRequestChannel.TryConnectAsRequester())
                {
                    _grabRequestChannel.Dispose();
                    _grabRequestChannel = null;
                    // 구버전 VMS 등 — 읽기만 시도
                    return new(VmsGrabRequestOutcome.NoChannel, null);
                }

                StatusMessage = "VMS 에 촬영을 요청하는 중...";

                // 요청한 카메라가 지정돼 있으면 그 카메라로, 없으면 VMS 의 기본 카메라로.
                string cameraId = SelectedCamera?.Id ?? string.Empty;
                var response = await Task.Run(() => _grabRequestChannel.RequestGrab(cameraId));

                if (response == null)
                {
                    StatusMessage = "VMS 가 촬영 요청에 응답하지 않았습니다 (시간 초과) — 프레임을 받지 않았습니다.";
                    return new(VmsGrabRequestOutcome.NoResponse, null);
                }
                if (!response.IsSuccess)
                {
                    StatusMessage = $"VMS 가 촬영을 거절했습니다: {response.Message} — 프레임을 받지 않았습니다.";
                    return new(VmsGrabRequestOutcome.Rejected, null);
                }
                return new(VmsGrabRequestOutcome.Success, response.FrameCounter);
            }
            catch (Exception ex)
            {
                StatusMessage = $"VMS 촬영 요청 오류: {ex.Message}";
                return new(VmsGrabRequestOutcome.Error, null);
            }
        }

        [RelayCommand]
        private void RunCalibration()
        {
            if (_sourceImage == null || _sourceImage.Empty())
            {
                _dialogService.ShowWarning("Load or capture a calibration image first.", "No Image");
                return;
            }

            CalibrationResult result = SelectedMode switch
            {
                CalibrationMode.Checkerboard => _calibrationService.RunCheckerboard(
                    _sourceImage, PatternCols, PatternRows, SquareSizeMm, AccumulateMultiView),
                CalibrationMode.CirclesGrid => _calibrationService.RunCirclesGrid(
                    _sourceImage, PatternCols, PatternRows, CircleSpacingMm,
                    AsymmetricCircles, AccumulateMultiView),
                CalibrationMode.NPointToNPoint => RunNPointMode(),
                CalibrationMode.SinglePointScale => RunSingleScaleMode(),
                _ => new CalibrationResult { Success = false, Message = "Unknown mode" }
            };

            OnPropertyChanged(nameof(AccumulatedViewCount));

            if (!result.Success || result.Metadata == null)
            {
                StatusMessage = result.Message;
                HasResult = false;
                return;
            }

            ReprojectionError = result.Metadata.ReprojectionError;
            PixelSizeMm = result.Metadata.PixelSizeMm;
            StatusMessage = result.Message;
            HasResult = true;
            _lastResultMetadata = result.Metadata;

            if (result.Overlay != null)
            {
                UpdatePreview(result.Overlay);
                result.Overlay.Dispose();
            }
        }

        private CalibrationResult RunNPointMode()
        {
            if (NPointPoints.Count < 4)
                return new CalibrationResult { Success = false, Message = $"NPoint needs >= 4 points (have {NPointPoints.Count})" };

            var pts = NPointPoints
                .Select(p => (new Point2d(p.PixelX, p.PixelY), new Point2d(p.WorldXmm, p.WorldYmm)))
                .ToList();
            return _calibrationService.RunNPoint(pts, _sourceImage!.Width, _sourceImage!.Height);
        }

        private CalibrationResult RunSingleScaleMode()
        {
            if (_scalePoint1 == null || _scalePoint2 == null)
                return new CalibrationResult { Success = false, Message = "Click two points on the image first" };

            return _calibrationService.RunSingleScale(
                _scalePoint1.Value, _scalePoint2.Value, KnownLengthMm,
                _sourceImage!.Width, _sourceImage!.Height);
        }

        /// <summary>
        /// 이미지에서 클릭한 픽셀 좌표를 현재 모드에 맞게 처리.
        /// 코드비하인드의 마우스 핸들러가 화면→원본 좌표 변환 후 호출.
        /// </summary>
        public void AddPickedPoint(double pixelX, double pixelY)
        {
            if (IsNPointMode)
            {
                NPointPoints.Add(new CalibPoint { PixelX = pixelX, PixelY = pixelY });
            }
            else if (IsSingleScaleMode)
            {
                if (_scalePoint1 == null)
                    _scalePoint1 = new Point2d(pixelX, pixelY);
                else if (_scalePoint2 == null)
                    _scalePoint2 = new Point2d(pixelX, pixelY);
                else
                {
                    // 3번째 클릭 → 새 첫 점으로 리셋
                    _scalePoint1 = new Point2d(pixelX, pixelY);
                    _scalePoint2 = null;
                }
                OnPropertyChanged(nameof(ScalePoint1Display));
                OnPropertyChanged(nameof(ScalePoint2Display));
            }
            RedrawOverlay();
        }

        [RelayCommand]
        private void ClearPickedPoints()
        {
            NPointPoints.Clear();
            _scalePoint1 = null;
            _scalePoint2 = null;
            OnPropertyChanged(nameof(ScalePoint1Display));
            OnPropertyChanged(nameof(ScalePoint2Display));
            RedrawOverlay();
            StatusMessage = "Picked points cleared.";
        }

        /// <summary>
        /// _sourceImage 위에 현재 모드의 픽 좌표 마커를 그려서 PreviewImage 업데이트.
        /// _sourceImage는 그대로 보존 (다음 클릭 시 누적 마커를 다시 그림).
        /// </summary>
        private void RedrawOverlay()
        {
            if (_sourceImage == null || _sourceImage.Empty()) return;
            using var canvas = _sourceImage.Channels() >= 3
                ? _sourceImage.Clone()
                : _sourceImage.CvtColor(ColorConversionCodes.GRAY2BGR);

            if (IsNPointMode)
            {
                for (int i = 0; i < NPointPoints.Count; i++)
                {
                    var p = NPointPoints[i];
                    var center = new Point((int)p.PixelX, (int)p.PixelY);
                    Cv2.Circle(canvas, center, 6, new Scalar(0, 255, 255), 2);
                    Cv2.PutText(canvas, (i + 1).ToString(), new Point(center.X + 8, center.Y - 8),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);
                }
            }
            else if (IsSingleScaleMode)
            {
                if (_scalePoint1 is { } p1)
                {
                    Cv2.Circle(canvas, new Point((int)p1.X, (int)p1.Y), 6, new Scalar(0, 255, 0), 2);
                    Cv2.PutText(canvas, "1", new Point((int)p1.X + 8, (int)p1.Y - 8),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);
                }
                if (_scalePoint2 is { } p2)
                {
                    Cv2.Circle(canvas, new Point((int)p2.X, (int)p2.Y), 6, new Scalar(0, 255, 0), 2);
                    Cv2.PutText(canvas, "2", new Point((int)p2.X + 8, (int)p2.Y - 8),
                        HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);
                    if (_scalePoint1 is { } a)
                        Cv2.Line(canvas, new Point((int)a.X, (int)a.Y),
                            new Point((int)p2.X, (int)p2.Y), new Scalar(0, 255, 0), 2);
                }
            }
            UpdatePreview(canvas);
        }

        [RelayCommand]
        private void ResetAccumulated()
        {
            _calibrationService.ResetAccumulated();
            OnPropertyChanged(nameof(AccumulatedViewCount));
            StatusMessage = "Accumulated views cleared.";
        }

        /// <summary>
        /// 계산 결과를 실제로 쓰이는 자리에 넣는다. 세 가지가 한 번에 일어난다.
        ///   ① 레시피의 캘리브레이션 슬롯 교체 (+ 런타임 슬롯 동기화)
        ///   ② 위 목록에서 고른 스텝의 Resolution(mm/px) 갱신 — 왼쪽 Steps 표에 보이는 값
        ///   ③ 레시피 파일 저장
        /// 무엇이 바뀌었는지는 상태 메시지와 요약 줄에 그대로 남는다.
        /// </summary>
        [RelayCommand]
        private void ApplyToRecipe()
        {
            if (_lastResultMetadata == null)
            {
                _dialogService.ShowWarning("Run calibration first.", "No Calibration Result");
                return;
            }

            // ① 런타임 슬롯 — 레시피가 없어도 이번 세션의 측정은 mm 로 나온다
            VisionService.Instance.CurrentCalibrationMetadata = _lastResultMetadata;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null)
            {
                _dialogService.ShowWarning(
                    "No active recipe. Calibration applied to current session only.",
                    "No Recipe");
                StatusMessage = "Applied to session (no recipe to persist into).";
                RefreshAppliedSummary();
                return;
            }

            recipe.Calibration = _lastResultMetadata;

            // ② 고른 스텝의 Resolution — 레시피가 들고 있는 인스턴스라 Steps 표가 즉시 따라온다
            double mmPerPx = _lastResultMetadata.PixelSizeMm;
            var chosen = ApplyTargets.Where(t => t.IsSelected).ToList();
            foreach (var t in chosen)
                t.Step.Resolution = mmPerPx;

            // 워크스페이스가 편집 중인 스텝을 건드렸으면 mm 폴백 값도 즉시 맞춘다
            // (정식 캘리브레이션이 우선이지만, 나중에 지웠을 때 남는 값이 이것이다)
            if (chosen.Any(t => t.Step.Id == VisionService.Instance.CurrentStepId))
                VisionService.Instance.CurrentStepResolutionMmPerPx = mmPerPx;

            // ③ 파일 저장
            if (!_recipeService.SaveRecipe(recipe))
            {
                StatusMessage = $"⚠ 레시피 '{recipe.Name}' 저장에 실패했습니다 — 값은 메모리에만 반영됐습니다.";
                _dialogService.ShowError(
                    $"Failed to save recipe '{recipe.Name}'. The calibration is applied to this session only.",
                    "Save Failed");
                return;
            }

            foreach (var t in ApplyTargets) t.Refresh();
            RefreshAppliedSummary();

            StatusMessage = chosen.Count > 0
                ? $"'{recipe.Name}' 저장 완료 · 스텝 {chosen.Count}개의 Resolution → {mmPerPx:F5} mm/px "
                  + $"({string.Join(", ", chosen.Take(4).Select(t => t.StepName))}{(chosen.Count > 4 ? " …" : "")})"
                : $"'{recipe.Name}' 저장 완료 · 캘리브레이션만 적용 (스텝 Resolution 은 그대로)";
        }

        [RelayCommand]
        private void ClearRecipeCalibration()
        {
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;
            if (!_dialogService.ShowConfirmation(
                "현재 레시피에서 캘리브레이션을 지울까요?\n\n"
                + "왜곡 보정(Image Rectify)은 즉시 통과 상태가 됩니다.\n"
                + "스텝의 Resolution(mm/px) 값은 그대로 남아 측정 도구의 mm 환산에 계속 쓰입니다 — "
                + "픽셀 단위로 되돌리려면 스텝의 Resolution 도 0 으로 바꾸세요.",
                "Clear Calibration")) return;

            recipe.Calibration = null;
            VisionService.Instance.CurrentCalibrationMetadata = null;
            _recipeService.SaveRecipe(recipe);
            HasResult = false;
            _lastResultMetadata = null;
            RefreshAppliedSummary();
            StatusMessage = "레시피에서 캘리브레이션을 지웠습니다 (스텝 Resolution 은 유지).";
        }

        /// <summary>
        /// 새 소스 이미지로 교체 (기존 Mat dispose + 미리보기 갱신 + 상태 메시지).
        /// 인자로 받은 Mat의 소유권을 이 ViewModel이 가져감.
        /// 픽 좌표가 이전 이미지 기준이라 자동 클리어.
        /// </summary>
        private void ReplaceSourceImage(Mat newImage, string pathLabel, string statusMessage)
        {
            _sourceImage?.Dispose();
            _sourceImage = newImage;
            SourceImagePath = pathLabel;
            // 이전 이미지에서 찍은 점은 새 이미지 좌표와 무관 → 클리어
            NPointPoints.Clear();
            _scalePoint1 = null;
            _scalePoint2 = null;
            OnPropertyChanged(nameof(ScalePoint1Display));
            OnPropertyChanged(nameof(ScalePoint2Display));
            UpdatePreview(_sourceImage);
            StatusMessage = statusMessage;
        }

        private void UpdatePreview(Mat image)
        {
            try
            {
                PreviewImage = image.ToWriteableBitmap();
            }
            catch
            {
                PreviewImage = null;
            }
        }

        public void Dispose()
        {
            if (_cameraAcquisition != null)
            {
                try { _cameraAcquisition.DisconnectAsync().GetAwaiter().GetResult(); } catch { /* swallow */ }
                _cameraAcquisition.Dispose();
                _cameraAcquisition = null;
            }
            _sharedFrameReader?.Dispose();
            _sharedFrameReader = null;
            _grabRequestChannel?.Dispose();
            _grabRequestChannel = null;
            _sourceImage?.Dispose();
            _sourceImage = null;
        }
    }
}
