using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
                StatusMessage = $"Loaded existing calibration from recipe ({existing.CalibratedAt:yyyy-MM-dd HH:mm}).";
                _lastResultMetadata = existing;
                HasResult = true;
            }
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
                    OnPropertyChanged(nameof(IsNPointMode));
                    OnPropertyChanged(nameof(IsSingleScaleMode));
                    RedrawOverlay();
                }
            }
        }

        public CalibrationMode SelectedMode =>
            SelectedModeOption?.Value ?? CalibrationMode.Checkerboard;

        public bool IsCheckerboardMode => SelectedMode == CalibrationMode.Checkerboard;
        public bool IsNPointMode => SelectedMode == CalibrationMode.NPointToNPoint;
        public bool IsSingleScaleMode => SelectedMode == CalibrationMode.SinglePointScale;

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
            bool writerAlive = false;
            try
            {
                using var probe = new SharedFrameReader();
                if (probe.TryConnect())
                    writerAlive = probe.IsWriterAlive;
            }
            catch
            {
                writerAlive = false;
            }

            IsVmsWriterAlive = writerAlive;
            AvailabilityMessage = writerAlive
                ? "VMS is running — direct camera connection may conflict. Use 'VMS Shared Frame' if available."
                : string.Empty;
        }

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

            IsCapturing = true;
            try
            {
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
                        StatusMessage = $"Connect failed: {SelectedCamera.Name}. " +
                            (IsVmsWriterAlive ? "VMS may be holding the camera." : "Check connection/cable.");
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

        [RelayCommand]
        private void ReceiveFromVms()
        {
            IsCapturing = true;
            try
            {
                _sharedFrameReader ??= new SharedFrameReader();

                if (!_sharedFrameReader.TryConnect())
                {
                    StatusMessage = "VMS shared frame not available (VMS not running).";
                    RefreshSourceAvailability();
                    return;
                }
                if (!_sharedFrameReader.IsWriterAlive)
                {
                    StatusMessage = "VMS writer is not active.";
                    RefreshSourceAvailability();
                    return;
                }

                var frame = _sharedFrameReader.TryReadFrame(skipIfSameFrame: false);
                if (frame?.Image2D == null || frame.Image2D.Empty())
                {
                    StatusMessage = "No frame received from VMS.";
                    return;
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

        [RelayCommand]
        private void ApplyToRecipe()
        {
            if (_lastResultMetadata == null)
            {
                _dialogService.ShowWarning("Run calibration first.", "No Calibration Result");
                return;
            }

            VisionService.Instance.CurrentCalibrationMetadata = _lastResultMetadata;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null)
            {
                _dialogService.ShowWarning(
                    "No active recipe. Calibration applied to current session only.",
                    "No Recipe");
                StatusMessage = "Applied to session (no recipe to persist into).";
                return;
            }

            recipe.Calibration = _lastResultMetadata;
            _recipeService.SaveRecipe(recipe);
            StatusMessage = $"Saved to recipe '{recipe.Name}'.";
        }

        [RelayCommand]
        private void ClearRecipeCalibration()
        {
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;
            if (!_dialogService.ShowConfirmation(
                "Remove calibration from current recipe? Measurements will revert to pixel-only units.",
                "Clear Calibration")) return;

            recipe.Calibration = null;
            VisionService.Instance.CurrentCalibrationMetadata = null;
            _recipeService.SaveRecipe(recipe);
            HasResult = false;
            _lastResultMetadata = null;
            StatusMessage = "Calibration removed from recipe.";
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
            _sourceImage?.Dispose();
            _sourceImage = null;
        }
    }
}
