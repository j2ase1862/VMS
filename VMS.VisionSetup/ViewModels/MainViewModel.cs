using VMS.Camera.Converters;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.Views;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.ImageProcessing;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PatternMatching;
using VMS.VisionSetup.VisionTools.CodeReading;
using VMS.VisionSetup.VisionTools.Identification;
using VMS.VisionSetup.VisionTools.DeepLearning;
using VMS.VisionSetup.VisionTools.Result;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Media;
using CvRect = OpenCvSharp.Rect;

namespace VMS.VisionSetup.ViewModels
{
    public enum ImageDisplayMode
    {
        OriginalImage,
        ResultImage
    }

    public enum FolderNavigationMode
    {
        NavigateOnly,
        AutoRunAll,
        AutoRunSelected
    }

    #region Messenger Messages

    public class RequestShowToolROIMessage
    {
        public ROIShape? ROIShape { get; }
        public RequestShowToolROIMessage(ROIShape? roiShape) => ROIShape = roiShape;
    }

    public class RequestRefreshROIMessage
    {
        public ROIShape? ROIShape { get; }
        public RequestRefreshROIMessage(ROIShape? roiShape) => ROIShape = roiShape;
    }

    /// <summary>
    /// Geometry3D 설정의 클러스터 콤보 선택 시 2D 뷰어에 해당 클러스터 강조 표시 요청.
    /// </summary>
    public class RequestHighlightClusterMessage
    {
        public string ClusterToolId { get; }
        public int ClusterIndex { get; }
        public RequestHighlightClusterMessage(string clusterToolId, int clusterIndex)
        {
            ClusterToolId = clusterToolId;
            ClusterIndex = clusterIndex;
        }
    }

    /// <summary>
    /// 외부(예: Batch Test 창)에서 특정 Step을 메인 워크스페이스에 로드해 달라고 요청.
    /// </summary>
    public class RequestLoadStepMessage
    {
        public Recipe Recipe { get; }
        public InspectionStep Step { get; }
        public RequestLoadStepMessage(Recipe recipe, InspectionStep step)
        {
            Recipe = recipe;
            Step = step;
        }
    }

    #endregion

    public partial class MainViewModel : ObservableObject
    {
        #region Fields
        // 브랜드명 — AppData 루트 폴더 이름과 동일한 단일 원천을 공유
        private readonly string _appName = VMS.Camera.Configuration.AppDataPaths.RootFolderName;
        private readonly string _appVersion = "1.0.0";
        private readonly IVisionService _visionService;
        private readonly IRecipeService _recipeService;
        private readonly ICameraService _cameraService;
        private readonly IDialogService _dialogService;
        private readonly Action _shutdownAction;
        private readonly ISLMChatService? _chatService;
        private readonly IParameterApplyService? _parameterApplyService;
        private readonly IImageAnalysisService? _imageAnalysisService;
        private readonly IRecipeRetrievalService? _recipeRetrievalService;
        private ChatWindow? _chatWindow;
        private Mat? _currentImage;
        private VisionToolBase? _subscribedTool;
        private bool _isSyncingROI;
        private ICameraAcquisition? _cameraAcquisition;
        private SharedFrameReader? _sharedFrameReader;
        private CancellationTokenSource? _liveReceiveCts;
        private CancellationTokenSource? _cameraLiveCts;
        private string[] _imageFolderFiles = Array.Empty<string>();
        private int _currentImageIndex = -1;
        #endregion

        #region Properties
        public string AppName => _appName;
        public string AppVersion => _appVersion;

        // 도구 트리 (사이드바)
        public ObservableCollection<ToolCategory> ToolTree { get; } = new();

        // 워크스페이스에 배치된 도구들
        public ObservableCollection<ToolItem> DroppedTools { get; } = new();

        // 실행 큐 (순서대로 실행될 도구들)
        public ObservableCollection<VisionToolBase> ExecutionQueue => _visionService.Tools;

        // 실행 결과
        public ObservableCollection<VisionResult> Results => _visionService.Results;

        // 도구 간 연결선 목록
        public ObservableCollection<ToolConnection> Connections { get; } = new();

        // 도구 실행 결과 목록 (DataGrid 바인딩용)
        public ObservableCollection<ToolResultItem> ToolRunResults { get; } = new();

        // CurrentImage 가 Height Map 렌더링(진짜 2D 이미지가 없을 때의 폴백)인지 추적 —
        // 카메라 2D 이미지가 있으면 Height Map 이 2D 탭을 덮어쓰지 않게 하기 위함.
        // 기본 false (카메라/파일 이미지) — Height Map 폴백 경로만 할당 직후 true 로 표시.
        private bool _currentImageIsHeightMap;

        // 현재 이미지
        public Mat? CurrentImage
        {
            get => _currentImage;
            set
            {
                _currentImage?.Dispose();
                SetProperty(ref _currentImage, value);
                _currentImageIsHeightMap = false;
                if (value != null)
                    _visionService.SetImage(value);
                UpdateDisplayImage();
                NotifyCommandsCanExecuteChanged();
            }
        }

        // 이미지 폴더 탐색
        private FolderNavigationMode _folderNavigationMode = FolderNavigationMode.NavigateOnly;
        public FolderNavigationMode FolderNavigationMode
        {
            get => _folderNavigationMode;
            set => SetProperty(ref _folderNavigationMode, value);
        }

        public bool HasImageFolder => _imageFolderFiles.Length > 0;
        public string ImageFolderInfo => _imageFolderFiles.Length > 0
            ? $"{_currentImageIndex + 1} / {_imageFolderFiles.Length}  -  {System.IO.Path.GetFileName(_imageFolderFiles[_currentImageIndex])}"
            : string.Empty;


        // 표시용 이미지
        [ObservableProperty]
        private ImageSource? _displayImage;

        // 오버레이 이미지
        [ObservableProperty]
        private ImageSource? _overlayImage;

        // 결과 이미지
        [ObservableProperty]
        private ImageSource? _resultImage;

        // 결과 Mat (ImageCanvas용)
        private Mat? _resultMat;
        public Mat? ResultMat
        {
            get => _resultMat;
            set
            {
                _resultMat?.Dispose();
                SetProperty(ref _resultMat, value);
                OnPropertyChanged(nameof(DisplayMat));
            }
        }

        // ImageCanvas용 원본 Mat
        public Mat? SourceMat => CurrentImage;

        // ROI 컬렉션
        private ObservableCollection<ROIShape>? _roiShapes;
        public ObservableCollection<ROIShape>? ROIShapes
        {
            get => _roiShapes ??= new ObservableCollection<ROIShape>();
            set => SetProperty(ref _roiShapes, value);
        }

        // 디스플레이 모드
        public ImageDisplayMode[] DisplayModes { get; } = (ImageDisplayMode[])Enum.GetValues(typeof(ImageDisplayMode));

        private ImageDisplayMode _selectedDisplayMode = ImageDisplayMode.OriginalImage;
        public ImageDisplayMode SelectedDisplayMode
        {
            get => _selectedDisplayMode;
            set
            {
                if (SetProperty(ref _selectedDisplayMode, value))
                {
                    OnPropertyChanged(nameof(DisplayMat));
                    OnPropertyChanged(nameof(IsOriginalImageMode));
                }
            }
        }

        public Mat? DisplayMat => _selectedDisplayMode == ImageDisplayMode.OriginalImage ? CurrentImage : _resultMat;

        public bool IsOriginalImageMode => _selectedDisplayMode == ImageDisplayMode.OriginalImage;

        // 선택된 ROI
        private ROIShape? _selectedROI;
        public ROIShape? SelectedROI
        {
            get => _selectedROI;
            set
            {
                SetProperty(ref _selectedROI, value);
                // 선택된 ROI를 현재 도구에 적용
                ApplyROIToSelectedTool();
            }
        }

        // 선택된 도구
        private ToolItem? _selectedTool;
        public ToolItem? SelectedTool
        {
            get => _selectedTool;
            set
            {
                // 이전 도구 PropertyChanged 구독 해제
                if (_subscribedTool != null)
                {
                    _subscribedTool.PropertyChanged -= OnSelectedToolPropertyChanged;
                    _subscribedTool = null;
                }

                // 이전 VM 정리
                _selectedToolSettings?.Dispose();

                SetProperty(ref _selectedTool, value);
                OnPropertyChanged(nameof(SelectedVisionTool));
                NotifyCommandsCanExecuteChanged();

                // 새 도구 PropertyChanged 구독 + VM 생성
                if (value?.VisionTool != null)
                {
                    _subscribedTool = value.VisionTool;
                    _subscribedTool.PropertyChanged += OnSelectedToolPropertyChanged;
                    SelectedToolSettings = CreateToolSettingsViewModel(value.VisionTool);

                    // 도구를 선택하면 우측 패널을 Tool Settings 로 전환 (스텝 선택과 마지막 선택 우선)
                    ShowCameraSettings = false;
                }
                else
                {
                    SelectedToolSettings = null;
                }

                // 도구 전환 시 이전 도구의 결과 잔상 제거 — 새 도구 실행 전까지 Original 표시
                ResultImage = null;
                ResultMat = null;
                OverlayImage = null;
                SelectedDisplayMode = ImageDisplayMode.OriginalImage;

                // 도구 전환 시 ROI 캔버스 항상 clear — 새 도구의 ROI 는 Show ROI 명시 클릭 시 표시.
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(null));
            }
        }

        // 선택된 비전 도구 (설정 패널용)
        public VisionToolBase? SelectedVisionTool => SelectedTool?.VisionTool;

        // 선택된 도구의 ViewModel (ToolSettingsView DataContext)
        private ToolSettingsViewModelBase? _selectedToolSettings;
        public ToolSettingsViewModelBase? SelectedToolSettings
        {
            get => _selectedToolSettings;
            private set => SetProperty(ref _selectedToolSettings, value);
        }

        // 실행 중 여부
        [ObservableProperty]
        private bool _isRunning;

        // 상태 메시지
        [ObservableProperty]
        private string _statusMessage = "Ready";

        // 실행 시간
        [ObservableProperty]
        private string _executionTimeText = "";

        // 3D Point Cloud 데이터
        [ObservableProperty]
        private PointCloudData? _currentPointCloud;

        // Height Slicing 파라미터
        [ObservableProperty]
        private float _heightBaseline;

        [ObservableProperty]
        private float _heightLowerLimit = -60f;

        [ObservableProperty]
        private float _heightUpperLimit = 60f;

        [ObservableProperty]
        private float _pointCloudYMin = -60f;

        [ObservableProperty]
        private float _pointCloudYMax = 60f;

        // Height Slicing 저장 값
        private float? _savedHeightBaseline;
        private float? _savedHeightLowerLimit;
        private float? _savedHeightUpperLimit;

        [ObservableProperty]
        private bool _isHeightSlicingSaved;

        public HeightMapMetadata? CurrentHeightMapMetadata { get; private set; }

        // 3D Display Controls
        [ObservableProperty]
        private int _depthPointSize = 1;

        [ObservableProperty]
        private float _depthRangeMin;

        [ObservableProperty]
        private float _depthRangeMax;

        #region Camera Connection Properties

        // 카메라 정보 팝업 열림 상태
        [ObservableProperty]
        private bool _isCameraInfoPopupOpen;

        // 카메라 연결 상태
        [ObservableProperty]
        private bool _isCameraConnected;

        // 이미지 획득 중 상태
        [ObservableProperty]
        private bool _isAcquiring;

        // VMS 라이브 수신 중 상태
        [ObservableProperty]
        private bool _isReceivingFromVms;

        // 카메라 직접 라이브(연속 Grab) 중 상태 — VMS 수신(IsReceivingFromVms)과 별개
        [ObservableProperty]
        private bool _isCameraLive;

        #endregion

        #region Recipe / Camera / Step Properties

        // 현재 레시피 이름 표시
        [ObservableProperty]
        private string _currentRecipeName = "No Recipe";

        // 카메라 목록
        public ObservableCollection<CameraInfo> Cameras { get; } = new();

        // 선택된 카메라
        private CameraInfo? _selectedCamera;
        public CameraInfo? SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                var previousCamera = _selectedCamera;
                if (SetProperty(ref _selectedCamera, value))
                {
                    // 카메라 변경 시 기존 연결 해제
                    if (previousCamera != null && IsCameraConnected)
                    {
                        _ = DisconnectCamera();
                    }

                    RefreshSteps();
                    AddStepCommand.NotifyCanExecuteChanged();
                    AcquireImageCommand.NotifyCanExecuteChanged();
                    ConnectCameraCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>로봇 웨이포인트가 있는 스텝이 존재하는지 (UI Visibility 바인딩용)</summary>
        public bool NeedsRobot => Steps.Any(s => s.RobotWaypoint != null);

        // 로봇 연결 설정
        private string _robotIpAddress = "192.168.1.100";
        public string RobotIpAddress
        {
            get => _robotIpAddress;
            set => SetProperty(ref _robotIpAddress, value);
        }

        private int _robotPort = 30003;
        public int RobotPort
        {
            get => _robotPort;
            set => SetProperty(ref _robotPort, value);
        }

        // 로봇 회전 표현 방식
        public EulerConvention[] EulerConventions { get; } = (EulerConvention[])Enum.GetValues(typeof(EulerConvention));

        private EulerConvention _selectedEulerConvention = EulerConvention.UR_RotationVector;
        public EulerConvention SelectedEulerConvention
        {
            get => _selectedEulerConvention;
            set => SetProperty(ref _selectedEulerConvention, value);
        }

        // 로봇 통신 프로토콜 모드 (AppSetup에서 설정)
        private RobotProtocolMode _robotProtocolMode = RobotProtocolMode.VendorNative;
        public RobotProtocolMode RobotProtocolMode
        {
            get => _robotProtocolMode;
            set => SetProperty(ref _robotProtocolMode, value);
        }

        // 로봇 연결 상태
        [ObservableProperty]
        private bool _isRobotConnected;

        // 멀티뷰 세션
        private MultiViewSession? _multiViewSession;

        /// <summary>멀티뷰 수집된 스캔 수</summary>
        public string MultiViewStatusText => _multiViewSession != null
            ? $"스캔: {_multiViewSession.Scans.Count}개 | {_multiViewSession.StatusMessage}"
            : "세션 없음";

        // 정합 전략
        public RegistrationStrategy[] RegistrationStrategies { get; } = (RegistrationStrategy[])Enum.GetValues(typeof(RegistrationStrategy));

        private RegistrationStrategy _selectedRegistrationStrategy = RegistrationStrategy.PoseOnly;
        public RegistrationStrategy SelectedRegistrationStrategy
        {
            get => _selectedRegistrationStrategy;
            set => SetProperty(ref _selectedRegistrationStrategy, value);
        }

        // 로봇 서비스 (DI 또는 직접 생성)
        private IRobotService? _robotService;

        /// <summary>시뮬레이션 로봇 사용 여부</summary>
        public bool IsSimulatedRobot => _robotService is SimulatedRobotService;

        /// <summary>현재 로봇 서비스 (Hand-Eye 캘리브레이션 위저드 등 외부 접근용)</summary>
        public IRobotService? RobotService => _robotService;

        /// <summary>현재 카메라 획득 인터페이스</summary>
        public ICameraAcquisition? CameraAcquisition => _cameraAcquisition;

        /// <summary>다이얼로그 서비스 접근</summary>
        public IDialogService DialogServiceAccessor => _dialogService;

        // 웨이포인트 플래너
        private readonly WaypointPlannerService _waypointPlanner = new();
        public WaypointPlannerService WaypointPlanner => _waypointPlanner;

        // 웨이포인트 패턴 선택
        public WaypointPattern[] WaypointPatterns { get; } = (WaypointPattern[])Enum.GetValues(typeof(WaypointPattern));

        private WaypointPattern _selectedWaypointPattern = WaypointPattern.TopPlusRing;
        public WaypointPattern SelectedWaypointPattern
        {
            get => _selectedWaypointPattern;
            set => SetProperty(ref _selectedWaypointPattern, value);
        }

        private int _waypointCount = 5;
        public int WaypointCount
        {
            get => _waypointCount;
            set => SetProperty(ref _waypointCount, Math.Max(1, value));
        }

        private float _scanDistance = 500f;
        public float ScanDistance
        {
            get => _scanDistance;
            set => SetProperty(ref _scanDistance, MathF.Max(50f, value));
        }

        private float _scanElevation = 30f;
        public float ScanElevation
        {
            get => _scanElevation;
            set => SetProperty(ref _scanElevation, Math.Clamp(value, 5f, 85f));
        }

        private CancellationTokenSource? _waypointScanCts;

        // Hand-Eye 캘리브레이션
        private readonly HandEyeCalibrationService _handEyeCalibration = new();
        private Matrix4x4 _handEyeMatrix = Matrix4x4.Identity;

        private string? _handEyeCalibrationPath;
        public string? HandEyeCalibrationPath
        {
            get => _handEyeCalibrationPath;
            set
            {
                if (SetProperty(ref _handEyeCalibrationPath, value))
                {
                    LoadHandEyeCalibration();
                    OnPropertyChanged(nameof(IsHandEyeCalibrated));
                }
            }
        }

        public bool IsHandEyeCalibrated => _handEyeCalibration.IsCalibrated;

        // 스텝 목록 (선택된 카메라 기준)
        public ObservableCollection<InspectionStep> Steps { get; } = new();

        // 선택된 스텝
        private InspectionStep? _selectedStep;
        public InspectionStep? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (SetProperty(ref _selectedStep, value))
                {
                    if (value != null)
                        LoadStepToWorkspace(value);

                    // 우측 패널 전환: 스텝 선택 → 카메라 파라미터, 도구 선택 → 도구 파라미터
                    ShowCameraSettings = value != null;
                    UpdateStepCameraInfo(value);
                    OpenTemplateGalleryCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        // 우측 패널 모드 — true 면 선택 스텝의 카메라 파라미터(노출/게인/3D 후처리),
        // false 면 선택 도구의 Tool Settings 표시 (마지막 선택이 이긴다)
        [ObservableProperty]
        private bool _showCameraSettings;

        // 선택 스텝의 카메라가 3D(구조광)인지 — Camera Settings 패널의 3D 스캔 그룹 노출 여부.
        // 2D 카메라(Basler/HIK 등)는 노출/게인만 표시.
        [ObservableProperty]
        private bool _isStep3DCamera;

        // Camera Settings 패널 상단의 카메라 요약 (이름 · 제조사 · 타입)
        [ObservableProperty]
        private string _stepCameraSummary = string.Empty;

        // 카메라가 실제로 쓰는 현재 노출/게인 (read-back, 지원 카메라만) — json 저장값과
        // 다를 수 있어 "카메라 설정 유지" 모드에서 무엇이 적용되는지 보여준다
        [ObservableProperty]
        private string _stepCameraCurrentText = string.Empty;

        /// <summary>선택 스텝의 카메라를 찾아 3D 여부/요약 갱신 (제조사·타입별 파라미터 분기)</summary>
        private void UpdateStepCameraInfo(InspectionStep? step)
        {
            var cam = step != null
                ? Cameras.FirstOrDefault(c => c.Id == step.CameraId) ?? SelectedCamera
                : null;

            if (cam == null)
            {
                IsStep3DCamera = false;
                StepCameraSummary = string.Empty;
                StepCameraCurrentText = string.Empty;
                return;
            }

            IsStep3DCamera = cam.CameraType is CameraType.AreaScan3D or CameraType.LineScan3D
                || cam.Manufacturer.Replace("_", "").Contains("Mech", StringComparison.OrdinalIgnoreCase);
            StepCameraSummary = $"카메라: {cam.Name} ({cam.Manufacturer} · {cam.CameraType})";
            _ = RefreshStepCameraCurrentTextAsync();
        }

        /// <summary>연결된 카메라에서 현재 2D 노출/게인을 읽어 패널에 표시 (미지원/미연결이면 숨김)</summary>
        private async System.Threading.Tasks.Task RefreshStepCameraCurrentTextAsync()
        {
            var acquisition = _cameraAcquisition;
            if (acquisition == null || !IsCameraConnected)
            {
                StepCameraCurrentText = string.Empty;
                return;
            }

            var current = await acquisition.ReadSettingsAsync();
            StepCameraCurrentText = current == null
                ? string.Empty
                : $"카메라 현재값: 노출 {current.ExposureUs / 1000.0:N1} ms · 게인 {current.Gain:N1} dB";
        }

        // Depth Map 탭 표시 색상 — 0 = 컬러(Jet), 1 = 그레이 (Mech-Eye Viewer UX)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDepthMapGray))]
        private int _depthMapColorModeIndex;

        public bool IsDepthMapGray => DepthMapColorModeIndex == 1;

        // 3D 후처리 프리셋 콤보 항목 (Mech-Eye Viewer 후처리 UX: 끄기/약/중/강)
        // 인스턴스 프로퍼티 — WPF Binding 경로는 정적 프로퍼티를 해석하지 못한다
        public IReadOnlyList<KeyValuePair<PointCloudPostProcessPreset, string>> PostProcessPresetItems { get; } =
            new List<KeyValuePair<PointCloudPostProcessPreset, string>>
            {
                new(PointCloudPostProcessPreset.CameraDefault, "카메라 설정 유지"),
                new(PointCloudPostProcessPreset.Off, "끄기"),
                new(PointCloudPostProcessPreset.Weak, "약"),
                new(PointCloudPostProcessPreset.Normal, "중"),
                new(PointCloudPostProcessPreset.Strong, "강"),
            };

        #endregion

        #endregion

        #region Commands
        public RelayCommand CloseCommand { get; }
        public RelayCommand OpenImageFileCommand { get; }
        public RelayCommand RunAllCommand { get; }
        public RelayCommand RunSelectedCommand { get; }
        public RelayCommand ClearToolsCommand { get; }
        public RelayCommand<ToolItem> RemoveToolCommand { get; }
        public RelayCommand<ToolItem> MoveToolUpCommand { get; }
        public RelayCommand<ToolItem> MoveToolDownCommand { get; }
        public RelayCommand TrainPatternCommand { get; }
        public RelayCommand AutoTuneCommand { get; }
        public RelayCommand ClearResultImageCommand { get; }
        public RelayCommand SaveDisplayedImageCommand { get; }
        public RelayCommand<ToolResultItem> CopyToolResultCommand { get; }
        public RelayCommand AddStepCommand { get; }
        public RelayCommand DeleteStepCommand { get; }
        public RelayCommand MoveStepUpCommand { get; }
        public RelayCommand MoveStepDownCommand { get; }
        public RelayCommand OpenTemplateGalleryCommand { get; }
        public RelayCommand LoadSamplePointCloudCommand { get; }
        public RelayCommand ShowCameraInfoCommand { get; }
        public RelayCommand AcquireImageCommand { get; }
        public RelayCommand StartCameraLiveCommand { get; }
        public RelayCommand StopCameraLiveCommand { get; }
        public RelayCommand ConnectCameraCommand { get; }
        public RelayCommand DisconnectCameraCommand { get; }
        public RelayCommand GenerateHeightMapCommand { get; }
        public RelayCommand SaveRecipeCommand { get; }
        public RelayCommand ReceiveFromVmsCommand { get; }
        public RelayCommand StartLiveReceiveCommand { get; }
        public RelayCommand StopLiveReceiveCommand { get; }
        public RelayCommand SavePointCloudCommand { get; }
        public RelayCommand LoadPointCloudCommand { get; }
        public RelayCommand ConnectRobotCommand { get; }
        public RelayCommand DisconnectRobotCommand { get; }
        public RelayCommand MultiViewCaptureCommand { get; }
        public RelayCommand MultiViewProcessCommand { get; }
        public RelayCommand MultiViewClearCommand { get; }
        public RelayCommand GenerateWaypointsCommand { get; }
        public RelayCommand ClearWaypointsCommand { get; }
        public RelayCommand StartWaypointScanCommand { get; }
        public RelayCommand StopWaypointScanCommand { get; }
        public RelayCommand SaveHeightSlicingCommand { get; }
        public RelayCommand ClearHeightSlicingCommand { get; }
        public RelayCommand OpenImageFolderCommand { get; }
        public RelayCommand PreviousImageCommand { get; }
        public RelayCommand NextImageCommand { get; }
        public RelayCommand ShowChatWindowCommand { get; }
        #endregion

        #region Constructor
        public MainViewModel(
            IVisionService visionService,
            IRecipeService recipeService,
            ICameraService cameraService,
            IDialogService dialogService,
            Action shutdownAction,
            IRobotService? robotService = null,
            ISLMChatService? chatService = null,
            IParameterApplyService? parameterApplyService = null,
            IImageAnalysisService? imageAnalysisService = null,
            IRecipeRetrievalService? recipeRetrievalService = null)
        {
            _visionService = visionService;
            _recipeService = recipeService;
            _cameraService = cameraService;
            _dialogService = dialogService;
            _shutdownAction = shutdownAction;
            _chatService = chatService;
            _parameterApplyService = parameterApplyService;
            _imageAnalysisService = imageAnalysisService;
            _recipeRetrievalService = recipeRetrievalService;

            // AppSetup에서 생성된 로봇 서비스 적용 (연결은 사용자가 수동으로)
            _robotService = robotService;

            InitializeToolTree();

            CloseCommand = new RelayCommand(CloseApplication);
            OpenImageFileCommand = new RelayCommand(OpenImageFile);
            OpenImageFolderCommand = new RelayCommand(OpenImageFolder);
            PreviousImageCommand = new RelayCommand(NavigatePreviousImage, () => _currentImageIndex > 0);
            NextImageCommand = new RelayCommand(NavigateNextImage, () => _currentImageIndex < _imageFolderFiles.Length - 1);
            RunAllCommand = new RelayCommand(async () => await RunAllToolsAsync(), () => !IsRunning && CurrentImage != null);
            RunSelectedCommand = new RelayCommand(RunSelectedTool, () => !IsRunning && SelectedTool != null && CurrentImage != null);
            ClearToolsCommand = new RelayCommand(ClearAllTools);
            RemoveToolCommand = new RelayCommand<ToolItem>(RemoveTool);
            MoveToolUpCommand = new RelayCommand<ToolItem>(MoveToolUp);
            MoveToolDownCommand = new RelayCommand<ToolItem>(MoveToolDown);
            TrainPatternCommand = new RelayCommand(TrainPattern, () => SelectedVisionTool is FeatureMatchTool);
            AutoTuneCommand = new RelayCommand(AutoTuneParameters, () => SelectedVisionTool is FeatureMatchTool);
            ClearResultImageCommand = new RelayCommand(ClearResultImage);
            SaveDisplayedImageCommand = new RelayCommand(SaveDisplayedImage);
            CopyToolResultCommand = new RelayCommand<ToolResultItem>(CopyToolResult);
            AddStepCommand = new RelayCommand(AddStep, () => _recipeService.CurrentRecipe != null && SelectedCamera != null);
            DeleteStepCommand = new RelayCommand(DeleteStep, () => SelectedStep != null);
            MoveStepUpCommand = new RelayCommand(MoveStepUp, () => SelectedStep != null);
            MoveStepDownCommand = new RelayCommand(MoveStepDown, () => SelectedStep != null);
            // 템플릿은 스텝이 선택된 상태에서만 생성 — 생성 결과가 스텝에 저장되므로
            OpenTemplateGalleryCommand = new RelayCommand(OpenTemplateGallery, () => SelectedStep != null);
            LoadSamplePointCloudCommand = new RelayCommand(LoadSamplePointCloud);
            ShowCameraInfoCommand = new RelayCommand(ShowCameraInfo);
            // 직접 연결/취득은 VMS 메인이 실행 중이면 차단 — 카메라 소유권은 항상
            // 하나(VMS 우선). VMS 실행 중에는 Live Receive(공유메모리 수신)만 사용.
            AcquireImageCommand = new RelayCommand(async () => await AcquireImage(), () => SelectedCamera != null && !IsAcquiring && !IsCameraLive && !IsVmsMainRunning);
            StartCameraLiveCommand = new RelayCommand(async () => await StartCameraLive(), () => SelectedCamera != null && !IsCameraLive && !IsAcquiring && !IsVmsMainRunning);
            StopCameraLiveCommand = new RelayCommand(StopCameraLive, () => IsCameraLive);
            ConnectCameraCommand = new RelayCommand(async () => await ConnectCamera(), () => SelectedCamera != null && !IsCameraConnected && !IsVmsMainRunning);
            DisconnectCameraCommand = new RelayCommand(async () => await DisconnectCamera(), () => IsCameraConnected);
            GenerateHeightMapCommand = new RelayCommand(GenerateHeightMap, CanGenerateHeightMap);
            SaveRecipeCommand = new RelayCommand(SaveCurrentRecipe, () => _recipeService.CurrentRecipe != null);
            ReceiveFromVmsCommand = new RelayCommand(async () => await ReceiveFromVms(), () => !IsReceivingFromVms);
            StartLiveReceiveCommand = new RelayCommand(async () => await StartLiveReceive(), () => !IsReceivingFromVms);
            StopLiveReceiveCommand = new RelayCommand(StopLiveReceive, () => IsReceivingFromVms);
            SavePointCloudCommand = new RelayCommand(SavePointCloud, () => CurrentPointCloud != null);
            LoadPointCloudCommand = new RelayCommand(LoadPointCloud);
            SaveHeightSlicingCommand = new RelayCommand(SaveHeightSlicing, () => CurrentPointCloud != null);
            ClearHeightSlicingCommand = new RelayCommand(ClearHeightSlicing, () => IsHeightSlicingSaved);

            // MultiView 관련 커맨드
            ConnectRobotCommand = new RelayCommand(async () => await ConnectRobot(), () => !IsRobotConnected && NeedsRobot);
            DisconnectRobotCommand = new RelayCommand(async () => await DisconnectRobot(), () => IsRobotConnected);
            MultiViewCaptureCommand = new RelayCommand(async () => await MultiViewCapture(), () => NeedsRobot && IsRobotConnected && (IsCameraConnected || IsSimulatedRobot));
            MultiViewProcessCommand = new RelayCommand(async () => await MultiViewProcess(), () => _multiViewSession?.Scans.Count > 0);
            MultiViewClearCommand = new RelayCommand(MultiViewClear, () => _multiViewSession?.Scans.Count > 0);

            // SLM Chat Bot 커맨드
            ShowChatWindowCommand = new RelayCommand(ShowChatWindow);

            // Waypoint Planner 커맨드
            GenerateWaypointsCommand = new RelayCommand(GenerateWaypoints, () => _recipeService.CurrentRecipe != null && SelectedCamera != null);
            ClearWaypointsCommand = new RelayCommand(ClearWaypoints, () => _waypointPlanner.Waypoints.Count > 0);
            StartWaypointScanCommand = new RelayCommand(async () => await StartWaypointScan(),
                () => NeedsRobot && IsRobotConnected && (IsCameraConnected || IsSimulatedRobot) && _waypointPlanner.Waypoints.Count > 0 && !_waypointPlanner.IsScanning);
            StopWaypointScanCommand = new RelayCommand(StopWaypointScan, () => _waypointPlanner.IsScanning);

            // ── VMS 메인 실행 감지 (카메라 소유권 중재) — 2초 주기 폴링 ──
            _vmsMainWatchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _vmsMainWatchTimer.Tick += (_, _) => UpdateVmsMainRunning();
            _vmsMainWatchTimer.Start();
            UpdateVmsMainRunning();

            // 레시피 변경 이벤트 구독
            _recipeService.CurrentRecipeChanged += OnCurrentRecipeChanged;

            // 카메라 목록 로드
            LoadCameras();

            // 현재 레시피가 이미 로드되어 있으면 반영
            var currentRecipe = _recipeService.CurrentRecipe;
            if (currentRecipe != null)
            {
                CurrentRecipeName = currentRecipe.Name;
                RefreshSteps();
            }

            // Register for tool settings messages
            WeakReferenceMessenger.Default.Register<RequestTrainPatternMessage>(this, (r, m) =>
            {
                if (TrainPatternCommand.CanExecute(null))
                    TrainPatternCommand.Execute(null);
            });
            WeakReferenceMessenger.Default.Register<RequestAutoTuneMessage>(this, (r, m) =>
            {
                if (AutoTuneCommand.CanExecute(null))
                    AutoTuneCommand.Execute(null);
            });
            WeakReferenceMessenger.Default.Register<RequestShowToolROIMessage>(this, (r, m) =>
            {
                SelectedDisplayMode = ImageDisplayMode.OriginalImage;
            });

            // Geometry3D 설정에서 클러스터 선택 시 → 해당 클러스터 강조 오버레이 표시
            WeakReferenceMessenger.Default.Register<RequestHighlightClusterMessage>(this, (r, m) =>
            {
                HighlightCluster(m.ClusterToolId, m.ClusterIndex);
            });

            // Batch Test 창에서 Step 선택 시 → 메인 워크스페이스에 그 Step의 도구 로드
            WeakReferenceMessenger.Default.Register<RequestLoadStepMessage>(this, (r, m) =>
            {
                if (_recipeService.CurrentRecipe?.Id != m.Recipe.Id)
                {
                    _recipeService.SetCurrentRecipe(m.Recipe);
                }
                SelectedStep = m.Step;
            });
        }
        #endregion

        #region Methods
        private void InitializeToolTree()
        {
            // Preprocessing 카테고리 (출력 채널 유지: 컬러→컬러, 그레이→그레이)
            var preprocessing = new ToolCategory { CategoryName = "Preprocessing (Color)" };
            preprocessing.Tools.Add(new ToolItem { Name = "Blur", ToolType = "BlurTool" });
            preprocessing.Tools.Add(new ToolItem { Name = "Morphology", ToolType = "MorphologyTool" });
            preprocessing.Tools.Add(new ToolItem { Name = "Image Enhance", ToolType = "ImageEnhanceTool" });
            preprocessing.Tools.Add(new ToolItem { Name = "Polar Unwrap", ToolType = "PolarUnwrapTool" });
            ToolTree.Add(preprocessing);

            // Conversion 카테고리 (출력: 항상 그레이스케일 1채널)
            var conversion = new ToolCategory { CategoryName = "Conversion (Gray)" };
            conversion.Tools.Add(new ToolItem { Name = "Grayscale", ToolType = "GrayscaleTool" });
            conversion.Tools.Add(new ToolItem { Name = "Threshold", ToolType = "ThresholdTool" });
            conversion.Tools.Add(new ToolItem { Name = "Edge Detection", ToolType = "EdgeDetectionTool" });
            conversion.Tools.Add(new ToolItem { Name = "Histogram", ToolType = "HistogramTool" });
            ToolTree.Add(conversion);

            // Pattern Matching 카테고리
            var patternMatching = new ToolCategory { CategoryName = "Pattern Matching" };
            patternMatching.Tools.Add(new ToolItem { Name = "Feature Match", ToolType = "FeatureMatchTool" });
            patternMatching.Tools.Add(new ToolItem { Name = "Shape Match", ToolType = "ShapeMatchTool" });
            ToolTree.Add(patternMatching);

            // Blob Analysis 카테고리
            var blobAnalysis = new ToolCategory { CategoryName = "Blob Analysis" };
            blobAnalysis.Tools.Add(new ToolItem { Name = "Blob", ToolType = "BlobTool" });
            ToolTree.Add(blobAnalysis);

            // Measurement 카테고리
            var measurement = new ToolCategory { CategoryName = "Measurement" };
            measurement.Tools.Add(new ToolItem { Name = "Caliper", ToolType = "CaliperTool" });
            measurement.Tools.Add(new ToolItem { Name = "Line Fit", ToolType = "LineFitTool" });
            measurement.Tools.Add(new ToolItem { Name = "Circle Fit", ToolType = "CircleFitTool" });
            measurement.Tools.Add(new ToolItem { Name = "Geometry", ToolType = "GeometryTool" });
            ToolTree.Add(measurement);

            // Identification 카테고리
            var identification = new ToolCategory { CategoryName = "Identification" };
            identification.Tools.Add(new ToolItem { Name = "OCR", ToolType = "OCRTool" });
            identification.Tools.Add(new ToolItem { Name = "OCV", ToolType = "OCVTool" });
            ToolTree.Add(identification);

            // Code Reading 카테고리
            var codeReading = new ToolCategory { CategoryName = "Code Reading" };
            codeReading.Tools.Add(new ToolItem { Name = "Code Reader", ToolType = "CodeReaderTool" });
            ToolTree.Add(codeReading);

            // 3D 카테고리 — 점군 가공(Processing)과 측정/검사(Measurement)로 세분화.
            // 카테고리는 팔레트 표시 전용이며 레시피 직렬화는 ToolType 기준이라 호환 영향 없음.
            var threeDProcessing = new ToolCategory { CategoryName = "3D Processing" };
            threeDProcessing.Tools.Add(new ToolItem { Name = "PointCloud Filter", ToolType = "PointCloudFilterTool" });
            threeDProcessing.Tools.Add(new ToolItem { Name = "PointCloud Registration", ToolType = "PointCloudRegistrationTool" });
            threeDProcessing.Tools.Add(new ToolItem { Name = "PointCloud Mask Crop", ToolType = "PointCloudMaskCropTool" });
            threeDProcessing.Tools.Add(new ToolItem { Name = "PointCloud Cluster", ToolType = "PointCloudClusterTool" });
            ToolTree.Add(threeDProcessing);

            var threeDMeasurement = new ToolCategory { CategoryName = "3D Measurement" };
            threeDMeasurement.Tools.Add(new ToolItem { Name = "PointCloud Deviation", ToolType = "PointCloudDeviationTool" });
            threeDMeasurement.Tools.Add(new ToolItem { Name = "Height Slicer", ToolType = "HeightSlicerTool" });
            threeDMeasurement.Tools.Add(new ToolItem { Name = "Plane Fit", ToolType = "PlaneFitTool" });
            threeDMeasurement.Tools.Add(new ToolItem { Name = "3D Geometry", ToolType = "Geometry3DTool" });
            ToolTree.Add(threeDMeasurement);

            // Deep Learning 카테고리
            var deepLearning = new ToolCategory { CategoryName = "Deep Learning" };
            deepLearning.Tools.Add(new ToolItem { Name = "Detection (YOLO)", ToolType = "DetectionTool" });
            deepLearning.Tools.Add(new ToolItem { Name = "Segmentation", ToolType = "SegmentationTool" });
            deepLearning.Tools.Add(new ToolItem { Name = "YOLOv8-seg", ToolType = "YoloSegTool" });
            deepLearning.Tools.Add(new ToolItem { Name = "Classify", ToolType = "ClassifyTool" });
            deepLearning.Tools.Add(new ToolItem { Name = "Anomaly", ToolType = "AnomalyTool" });
            ToolTree.Add(deepLearning);

            // Judgment 카테고리
            var judgment = new ToolCategory { CategoryName = "Judgment" };
            judgment.Tools.Add(new ToolItem { Name = "Result", ToolType = "ResultTool" });
            ToolTree.Add(judgment);

            // Color 카테고리
            var color = new ToolCategory { CategoryName = "Color" };
            color.Tools.Add(new ToolItem { Name = "Color Extract", ToolType = "ColorExtractTool" });
            color.Tools.Add(new ToolItem { Name = "Color Match", ToolType = "ColorMatchTool" });
            ToolTree.Add(color);

            // Calibration 카테고리 (캘리브레이션은 메뉴, 보정 적용 도구만 팔레트)
            var calibration = new ToolCategory { CategoryName = "Calibration" };
            calibration.Tools.Add(new ToolItem { Name = "Image Rectify", ToolType = "ImageRectifyTool" });
            ToolTree.Add(calibration);

            // Surface Analysis 카테고리 (조명 기반 표면 결함 검사)
            var surface = new ToolCategory { CategoryName = "Surface Analysis" };
            surface.Tools.Add(new ToolItem { Name = "Photometric Stereo", ToolType = "PhotometricStereoTool" });
            ToolTree.Add(surface);
        }

        #region VMS 메인 실행 감지 — 카메라 소유권 중재

        private System.Windows.Threading.DispatcherTimer? _vmsMainWatchTimer;

        private bool _isVmsMainRunning;
        /// <summary>
        /// VMS 메인 앱 실행 여부. true 면 카메라 직접 연결/취득/라이브를 차단하고
        /// Live Receive(공유메모리 수신)로 유도한다 — 카메라 소유권은 항상 하나(VMS 우선).
        /// </summary>
        public bool IsVmsMainRunning
        {
            get => _isVmsMainRunning;
            private set => SetProperty(ref _isVmsMainRunning, value);
        }

        private void UpdateVmsMainRunning()
        {
            bool running = SharedFrameReader.IsVmsMainRunning();
            if (running == IsVmsMainRunning) return;

            IsVmsMainRunning = running;
            ConnectCameraCommand.NotifyCanExecuteChanged();
            AcquireImageCommand.NotifyCanExecuteChanged();
            StartCameraLiveCommand.NotifyCanExecuteChanged();

            if (running)
            {
                // VMS 기동 감지 — 보유 중인 직접 연결을 해제해 소유권을 넘긴다
                try { if (IsCameraLive) StopCameraLive(); } catch { /* 전환 경로 — 실패해도 계속 */ }
                if (IsCameraConnected) _ = DisconnectCamera();
                StatusMessage = "VMS 실행 감지 — 카메라는 VMS가 소유합니다. 이미지는 'Live Receive (from VMS)'로 수신하세요.";
            }
            else
            {
                StatusMessage = "VMS 종료 감지 — 카메라 직접 연결을 사용할 수 있습니다.";
            }
        }

        #endregion

        private void CloseApplication()
        {
            Cleanup();
            _shutdownAction();
        }

        /// <summary>
        /// 종료 시 리소스 정리 — 백그라운드 수신 루프 / 카메라 SDK 스레드 / 공유 메모리
        /// 핸들이 남아 창을 닫아도 프로세스가 종료되지 않는 문제 방지.
        /// MainView.Closing 과 CloseApplication 양쪽에서 호출 (idempotent).
        /// </summary>
        public void Cleanup()
        {
            try { _vmsMainWatchTimer?.Stop(); } catch { }
            _vmsMainWatchTimer = null;
            try { StopCameraLive(); } catch { /* 종료 경로 — 실패해도 계속 */ }
            try { StopLiveReceive(); } catch { }
            try { _sharedFrameReader?.Dispose(); } catch { }
            _sharedFrameReader = null;
            try { _cameraAcquisition?.Dispose(); } catch { }
            _cameraAcquisition = null;
            try { _chatService?.Dispose(); } catch { }
        }

        private void ShowChatWindow()
        {
            if (_chatService == null)
            {
                System.Windows.MessageBox.Show(
                    "SLM Chat 서비스가 초기화되지 않았습니다.\nOllama가 설치되어 있는지 확인하세요.",
                    "SLM Chat", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (_chatWindow == null || !_chatWindow.IsLoaded)
            {
                var toolGenerator = new SLMToolGeneratorService(_parameterApplyService);
                var chatViewModel = new ChatViewModel(_chatService, toolGenerator, this,
                    _imageAnalysisService, _recipeRetrievalService);
                _chatWindow = new ChatWindow { DataContext = chatViewModel };
            }

            _chatWindow.Show();
            _chatWindow.Activate();
        }

        private void OpenImageFile()
        {
            var filePath = _dialogService.ShowOpenFileDialog(
                "이미지 파일 열기",
                "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All Files|*.*");

            if (filePath != null)
            {
                try
                {
                    var mat = Cv2.ImRead(filePath);
                    if (!mat.Empty())
                    {
                        CurrentImage = mat;
                        SelectedDisplayMode = ImageDisplayMode.OriginalImage;
                        StatusMessage = $"이미지 로드 완료: {System.IO.Path.GetFileName(filePath)}";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"이미지 로드 실패: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"이미지 로드 실패: {ex.Message}");
                }
            }
        }

        private void OpenImageFolder()
        {
            var folderPath = _dialogService.ShowFolderBrowserDialog("이미지 폴더 선택");
            if (folderPath == null) return;

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
            _imageFolderFiles = System.IO.Directory.GetFiles(folderPath)
                .Where(f => extensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToArray();

            if (_imageFolderFiles.Length == 0)
            {
                StatusMessage = "선택한 폴더에 이미지 파일이 없습니다.";
                return;
            }

            _currentImageIndex = 0;
            LoadCurrentFolderImage();
        }

        private void NavigatePreviousImage()
        {
            if (_currentImageIndex <= 0) return;
            _currentImageIndex--;
            LoadCurrentFolderImage();
        }

        private void NavigateNextImage()
        {
            if (_currentImageIndex >= _imageFolderFiles.Length - 1) return;
            _currentImageIndex++;
            LoadCurrentFolderImage();
        }

        // async void — RelayCommand(NavigatePrevious/Next)/LoadImageFolder 가 동기 메서드라
        // 시그니처를 async Task 로 바꾸려면 ICommand 체인 전반을 AsyncRelayCommand 로 교체
        // 해야 함 (큰 변경). 따라서 async void 를 유지하되 **단일 try/catch 로 모든 await
        // 까지 감싸** 프로세스 종료 위험 차단.
        private async void LoadCurrentFolderImage()
        {
            try
            {
                var mat = Cv2.ImRead(_imageFolderFiles[_currentImageIndex]);
                if (!mat.Empty())
                {
                    CurrentImage = mat;
                    // AutoRunAll/AutoRunSelected 모드는 직후 RunAll/RunSelected 가 Result 로 재설정 —
                    // Original 로 깜빡임 방지 차 NavigateOnly 일 때만 Original 강제.
                    if (FolderNavigationMode == FolderNavigationMode.NavigateOnly)
                    {
                        SelectedDisplayMode = ImageDisplayMode.OriginalImage;
                    }
                }

                OnPropertyChanged(nameof(HasImageFolder));
                OnPropertyChanged(nameof(ImageFolderInfo));
                PreviousImageCommand.NotifyCanExecuteChanged();
                NextImageCommand.NotifyCanExecuteChanged();

                // 모드에 따라 자동 실행
                if (FolderNavigationMode == FolderNavigationMode.AutoRunAll && RunAllCommand.CanExecute(null))
                {
                    await RunAllToolsAsync();
                }
                else if (FolderNavigationMode == FolderNavigationMode.AutoRunSelected && RunSelectedCommand.CanExecute(null))
                {
                    RunSelectedTool();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"이미지 로드/실행 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[MainViewModel.LoadCurrentFolderImage] {ex}");
            }
        }

        private void UpdateDisplayImage()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                DisplayImage = null;
                return;
            }

            try
            {
                DisplayImage = CurrentImage.ToWriteableBitmap();
                OnPropertyChanged(nameof(SourceMat));  // ImageCanvas에 이미지 변경 알림
                OnPropertyChanged(nameof(DisplayMat));
            }
            catch
            {
                DisplayImage = null;
            }
        }

        /// <summary>
        /// 도구 드롭 처리 - 새 비전 도구 인스턴스 생성
        /// </summary>
        public ToolItem? CreateDroppedTool(ToolItem sourceTool, double x, double y)
        {
            var visionTool = VisionService.CreateTool(sourceTool.ToolType);
            if (visionTool == null)
                return null;

            var newTool = new ToolItem
            {
                Name = $"{sourceTool.Name} #{DroppedTools.Count(t => t.ToolType == sourceTool.ToolType) + 1}",
                ToolType = sourceTool.ToolType,
                X = x,
                Y = y,
                VisionTool = visionTool
            };

            visionTool.Name = newTool.Name;
            DroppedTools.Add(newTool);
            _visionService.AddTool(visionTool);

            return newTool;
        }

        /// <summary>
        /// 모든 도구 실행. ChatViewModel 등 외부에서 자동 실행할 수 있도록 public.
        /// </summary>
        public async System.Threading.Tasks.Task RunAllToolsAsync()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                StatusMessage = "이미지가 로드되지 않았습니다.";
                return;
            }

            IsRunning = true;
            StatusMessage = "실행 중...";

            try
            {
                var results = await _visionService.ExecuteAllAsync();

                ExecutionTimeText = $"실행 시간: {_visionService.TotalExecutionTime:F2}ms";

                // VisionService에서 합성된 오버레이 사용
                var compositeOverlay = _visionService.LastCompositeOverlay;
                if (compositeOverlay != null && !compositeOverlay.Empty())
                {
                    ResultImage = compositeOverlay.ToWriteableBitmap();
                    ResultMat = compositeOverlay.Clone();
                    OverlayImage = ResultImage;
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }
                else if (results.LastOrDefault()?.OutputImage != null)
                {
                    ResultImage = results.Last().OutputImage!.ToWriteableBitmap();
                    ResultMat = results.Last().OutputImage!.Clone();
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }

                int successCount = results.Count(r => r.Success);
                StatusMessage = $"실행 완료: {successCount}/{results.Count} 성공";

                // 파이프라인 경고 표면화 (연결 사이클, 이미지 연결 폴백 등 — 실행은 계속되지만 사용자가 알아야 함)
                if (!string.IsNullOrEmpty(_visionService.LastPipelineWarning))
                    StatusMessage += $" [경고] {_visionService.LastPipelineWarning}";

                UpdateToolRunResults();
            }
            catch (Exception ex)
            {
                StatusMessage = $"실행 오류: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// 선택된 도구만 실행
        /// </summary>
        private void RunSelectedTool()
        {
            if (SelectedTool?.VisionTool == null || CurrentImage == null)
                return;

            IsRunning = true;
            StatusMessage = "실행 중...";

            try
            {
                var result = _visionService.ExecuteTool(SelectedTool.VisionTool, CurrentImage);

                ExecutionTimeText = $"실행 시간: {SelectedTool.VisionTool.ExecutionTime:F2}ms";

                if (result.OverlayImage != null)
                {
                    ResultImage = result.OverlayImage.ToWriteableBitmap();
                    ResultMat = result.OverlayImage.Clone();
                    OverlayImage = ResultImage;
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }
                else if (result.OutputImage != null)
                {
                    ResultImage = result.OutputImage.ToWriteableBitmap();
                    ResultMat = result.OutputImage.Clone();
                    SelectedDisplayMode = ImageDisplayMode.ResultImage;
                }

                StatusMessage = result.Success
                    ? $"실행 완료: {result.Message}"
                    : $"실행 실패: {result.Message}";

                // 파이프라인 경고 표면화 (업스트림 이미지 연결 폴백 등)
                if (!string.IsNullOrEmpty(_visionService.LastPipelineWarning))
                    StatusMessage += $" [경고] {_visionService.LastPipelineWarning}";

                UpdateToolRunResults(SelectedTool.VisionTool);
            }
            catch (Exception ex)
            {
                StatusMessage = $"실행 오류: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// 도구 실행 결과를 ToolRunResults 컬렉션에 반영
        /// </summary>
        /// <param name="targetTool">지정 시 해당 도구의 결과만 표시, null이면 전체 표시</param>
        private void UpdateToolRunResults(VisionToolBase? targetTool = null)
        {
            ToolRunResults.Clear();

            foreach (var toolItem in DroppedTools)
            {
                var visionTool = toolItem.VisionTool;
                if (visionTool == null) continue;

                // 특정 도구만 표시하는 경우 해당 도구가 아니면 건너뛰기
                if (targetTool != null && visionTool != targetTool)
                    continue;

                var lastResult = visionTool.LastResult;
                var resultValue = string.Empty;
                var entries = new List<ResultEntry>();

                if (lastResult != null)
                {
                    // 전체 Key–Value는 행 펼침(RowDetails)·툴팁·복사용으로 항상 수집
                    if (lastResult.Data != null)
                    {
                        foreach (var kv in lastResult.Data)
                            entries.Add(new ResultEntry { Key = kv.Key, Value = FormatResultValue(kv.Value) });
                    }
                    if (!string.IsNullOrWhiteSpace(lastResult.Message))
                        entries.Add(new ResultEntry { Key = "Message", Value = lastResult.Message });

                    resultValue = BuildResultSummary(visionTool, lastResult);
                }

                ToolRunResults.Add(new ToolResultItem
                {
                    ToolName = toolItem.Name,
                    Result = lastResult?.Success ?? false,
                    ResultValue = resultValue,
                    Entries = entries,
                    DetailText = string.Join(Environment.NewLine, entries.Select(e => $"{e.Key} = {e.Value}"))
                });
            }
        }

        private static string FormatResultValue(object? value) => value switch
        {
            double d => d.ToString("F3"),
            float f => f.ToString("F3"),
            decimal m => m.ToString("F3"),
            _ => value?.ToString() ?? ""
        };

        /// <summary>
        /// Run Results 요약 한 줄 — 툴별 핵심 값만 표시.
        /// 전체 값(클러스터별 상세, 쌍별 거리 등 대량 키)은 행 펼침에서 확인.
        /// </summary>
        private static string BuildResultSummary(VisionToolBase tool, VisionResult result)
        {
            var data = result.Data;
            if (data == null || data.Count == 0)
                return result.Message;

            switch (tool)
            {
                case BlobTool:
                {
                    var parts = new List<string>();
                    if (data.TryGetValue("BlobCount", out var count))
                        parts.Add($"Count={count}");
                    if (data.TryGetValue("TotalArea", out var area))
                        parts.Add($"TotalArea={FormatResultValue(area)}");
                    return string.Join(", ", parts);
                }

                case OCRTool:
                    return data.TryGetValue("RecognizedText", out var text)
                        ? text?.ToString()?.Trim() ?? ""
                        : result.Message;

                case VisionTools.PointCloud.PointCloudClusterTool:
                {
                    var parts = new List<string>();
                    if (data.TryGetValue("ClusterCount", out var cc))
                        parts.Add($"ClusterCount={cc}");
                    if (data.TryGetValue("LargestPoints", out var lp))
                        parts.Add($"Largest={lp}pt");
                    if (data.TryGetValue("Cluster0_1_DistanceMm", out var d01))
                        parts.Add($"Dist(0-1)={FormatResultValue(d01)}");
                    return string.Join(", ", parts);
                }

                case VisionTools.PointCloud.PointCloudMaskCropTool:
                {
                    var parts = new List<string>();
                    if (data.TryGetValue("InputPoints", out var inp) && data.TryGetValue("OutputPoints", out var outp))
                        parts.Add($"{inp} → {outp}pt");
                    if (data.TryGetValue("KeptRatio", out var kr))
                        parts.Add($"Kept={Convert.ToDouble(kr):P1}");
                    return string.Join(", ", parts);
                }

                case Geometry3DTool:
                {
                    if (data.TryGetValue("Distance3D", out var dist))
                        return $"Distance3D={FormatResultValue(dist)}";
                    if (data.TryGetValue("AngleDeg", out var ang))
                        return $"AngleDeg={FormatResultValue(ang)}";
                    return result.Message;
                }

                default:
                {
                    // 일반 툴: 앞쪽 키 위주로 표시하고 나머지는 개수만 (행 펼침에서 전체 확인)
                    const int maxEntries = 8;
                    var shown = data.Take(maxEntries)
                        .Select(kv => $"{kv.Key}={FormatResultValue(kv.Value)}");
                    var summary = string.Join(", ", shown);
                    if (data.Count > maxEntries)
                        summary += $" … (+{data.Count - maxEntries}개)";
                    return summary;
                }
            }
        }

        /// <summary>Run Results 행 우클릭 → 결과 전체(Key = Value)를 클립보드로 복사.</summary>
        private void CopyToolResult(ToolResultItem? item)
        {
            if (item == null) return;
            try
            {
                var text = $"{item.ToolName}  [{(item.Result ? "OK" : "FAIL")}]"
                    + Environment.NewLine + item.DetailText;
                System.Windows.Clipboard.SetText(text);
                StatusMessage = $"'{item.ToolName}' 결과를 클립보드에 복사했습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"결과 복사 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 모든 도구 제거
        /// </summary>
        private void ClearAllTools()
        {
            ClearAllConnections();
            DroppedTools.Clear();
            _visionService.ClearTools();
            SelectedTool = null;
            StatusMessage = "모든 도구가 제거되었습니다.";
        }

        /// <summary>
        /// 특정 도구 제거
        /// </summary>
        private void RemoveTool(ToolItem? tool)
        {
            if (tool == null) return;

            // 해당 도구와 관련된 연결선 모두 제거
            RemoveConnectionsForTool(tool);

            if (tool.VisionTool != null)
                _visionService.RemoveTool(tool.VisionTool);

            DroppedTools.Remove(tool);

            if (SelectedTool == tool)
                SelectedTool = null;
        }

        #region Connection Management

        /// <summary>
        /// 도구 간 연결 추가
        /// </summary>
        public void AddConnection(ToolItem source, ToolItem target, ConnectionType type)
        {
            // 중복 연결 방지 (같은 소스, 타겟, 타입)
            var existing = Connections.FirstOrDefault(c =>
                c.SourceToolItem?.Id == source.Id &&
                c.TargetToolItem?.Id == target.Id &&
                c.Type == type);

            if (existing != null)
            {
                StatusMessage = $"이미 동일한 연결이 존재합니다: {source.Name} → {target.Name} ({type})";
                return;
            }

            var connection = new ToolConnection
            {
                SourceToolItem = source,
                TargetToolItem = target,
                Type = type
            };

            Connections.Add(connection);

            // VisionService에 연결 정보 등록
            if (source.VisionTool != null && target.VisionTool != null)
            {
                _visionService.AddConnection(source.VisionTool, target.VisionTool, type);
            }

            StatusMessage = $"연결 생성됨: {source.Name} → {target.Name} ({type})";
        }

        /// <summary>
        /// 특정 도구의 모든 연결 제거
        /// </summary>
        public void RemoveConnectionsForTool(ToolItem tool)
        {
            var toRemove = Connections
                .Where(c => c.SourceToolItem?.Id == tool.Id || c.TargetToolItem?.Id == tool.Id)
                .ToList();

            foreach (var conn in toRemove)
            {
                // VisionService에서도 제거
                if (conn.SourceToolItem?.VisionTool != null && conn.TargetToolItem?.VisionTool != null)
                {
                    _visionService.RemoveConnection(
                        conn.SourceToolItem.VisionTool,
                        conn.TargetToolItem.VisionTool,
                        conn.Type);
                }
                Connections.Remove(conn);
            }

            if (toRemove.Count > 0)
                StatusMessage = $"{tool.Name}의 연결 {toRemove.Count}개가 제거되었습니다.";
        }

        /// <summary>
        /// 모든 연결 제거
        /// </summary>
        private void ClearAllConnections()
        {
            Connections.Clear();
            _visionService.ClearConnections();
        }

        #endregion

        /// <summary>
        /// Result Image 초기화
        /// </summary>
        public void ClearResultImage()
        {
            ResultImage = null;
            ResultMat = null;
            OverlayImage = null;
            SelectedDisplayMode = ImageDisplayMode.OriginalImage;
            StatusMessage = "Result Image가 초기화되었습니다.";
        }

        /// <summary>
        /// 클러스터 강조 표시 — 해당 Cluster 툴의 직전 오버레이 위에 선택 클러스터의
        /// 외접 사각형·중심을 굵게 다시 그려 Result Image로 표시한다.
        /// (Geometry3D 설정의 클러스터 콤보 선택에서 호출)
        /// </summary>
        private void HighlightCluster(string clusterToolId, int clusterIndex)
        {
            try
            {
                var clusterTool = _visionService.Tools.FirstOrDefault(t => t.Id == clusterToolId)
                    as VisionTools.PointCloud.PointCloudClusterTool;
                if (clusterTool == null)
                {
                    StatusMessage = "강조할 클러스터 툴을 찾을 수 없습니다.";
                    return;
                }

                var shape = clusterTool.LastOverlayShapes.FirstOrDefault(s => s.Index == clusterIndex);
                if (shape == null)
                {
                    StatusMessage = clusterTool.LastOverlayShapes.Count == 0
                        ? "강조할 클러스터 오버레이가 없습니다 — Run(F5)을 먼저 실행하세요 (Draw Overlay 켬 필요)."
                        : $"클러스터 #{clusterIndex}가 오버레이에 없습니다.";
                    return;
                }

                // 베이스: 클러스터 오버레이 (해제됐으면 현재 이미지로 폴백)
                var overlay = clusterTool.LastResult?.OverlayImage;
                bool overlayUsable = overlay != null && !overlay.IsDisposed && !overlay.Empty();
                var baseMat = overlayUsable ? overlay! : _visionService.CurrentImage;
                if (baseMat == null || baseMat.Empty())
                {
                    StatusMessage = "강조 표시할 베이스 이미지가 없습니다.";
                    return;
                }

                using var highlighted = baseMat.Channels() >= 3
                    ? baseMat.Clone()
                    : baseMat.CvtColor(ColorConversionCodes.GRAY2BGR);
                var accent = new Scalar(0, 215, 255); // 주황-노랑 강조색 (BGR)
                if (shape.ObbCorners.Length == 4)
                    Cv2.Polylines(highlighted, new[] { shape.ObbCorners }, true, accent, 4);
                Cv2.DrawMarker(highlighted, shape.Center, accent, MarkerTypes.Cross, 18, 3);

                ResultImage = highlighted.ToWriteableBitmap();
                ResultMat = highlighted.Clone();
                OverlayImage = ResultImage;
                SelectedDisplayMode = ImageDisplayMode.ResultImage;
                StatusMessage = $"클러스터 #{clusterIndex} 강조 표시";
            }
            catch (Exception ex)
            {
                // 콤보 선택(바인딩 setter) 경로에서 호출되므로 예외를 밖으로 던지지 않는다
                StatusMessage = $"클러스터 강조 표시 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 현재 표시 중인 이미지(DisplayMat — Original 또는 Result)를 파일로 저장.
        /// Result 모드면 처리 결과 이미지, Original 모드면 원본 이미지.
        /// </summary>
        public void SaveDisplayedImage()
        {
            var mat = DisplayMat;
            if (mat == null || mat.Empty())
            {
                StatusMessage = "저장할 이미지가 없습니다.";
                return;
            }

            string defaultName = SelectedDisplayMode == ImageDisplayMode.ResultImage
                ? $"result_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                : $"original_{DateTime.Now:yyyyMMdd_HHmmss}.png";

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "이미지 저장",
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp|TIFF (*.tif)|*.tif",
                FileName = defaultName,
                DefaultExt = ".png"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                OpenCvSharp.Cv2.ImWrite(dlg.FileName, mat);
                StatusMessage = $"저장됨: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"저장 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 도구 순서 위로 이동
        /// </summary>
        private void MoveToolUp(ToolItem? tool)
        {
            if (tool == null) return;

            int index = DroppedTools.IndexOf(tool);
            if (index > 0)
            {
                DroppedTools.Move(index, index - 1);

                // 실행 큐도 동기화
                if (tool.VisionTool != null)
                {
                    int toolIndex = ExecutionQueue.IndexOf(tool.VisionTool);
                    if (toolIndex > 0)
                        _visionService.MoveTool(toolIndex, toolIndex - 1);
                }
            }
        }

        /// <summary>
        /// 도구 순서 아래로 이동
        /// </summary>
        private void MoveToolDown(ToolItem? tool)
        {
            if (tool == null) return;

            int index = DroppedTools.IndexOf(tool);
            if (index < DroppedTools.Count - 1)
            {
                DroppedTools.Move(index, index + 1);

                // 실행 큐도 동기화
                if (tool.VisionTool != null)
                {
                    int toolIndex = ExecutionQueue.IndexOf(tool.VisionTool);
                    if (toolIndex < ExecutionQueue.Count - 1)
                        _visionService.MoveTool(toolIndex, toolIndex + 1);
                }
            }
        }

        /// <summary>
        /// 패턴 학습 (FeatureMatchTool용)
        /// </summary>
        private void TrainPattern()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                StatusMessage = "이미지가 로드되지 않았습니다.";
                return;
            }

            // 도구에 UseROI가 설정되어 있으면 ROI 영역만 학습, 아니면 전체 이미지
            Mat trainingImage = CurrentImage;

            if (SelectedVisionTool != null && SelectedVisionTool.UseROI
                && SelectedVisionTool.ROI.Width > 0 && SelectedVisionTool.ROI.Height > 0)
            {
                var roi = SelectedVisionTool.ROI;
                // 이미지 범위 내로 클리핑
                int x = Math.Max(0, roi.X);
                int y = Math.Max(0, roi.Y);
                int w = Math.Min(roi.Width, CurrentImage.Width - x);
                int h = Math.Min(roi.Height, CurrentImage.Height - y);
                var rect = new CvRect(x, y, w, h);

                if (rect.Width > 10 && rect.Height > 10)
                {
                    trainingImage = new Mat(CurrentImage, rect);
                    StatusMessage = $"ROI 영역으로 학습 중... ({rect.Width}x{rect.Height})";
                }
            }

            if (SelectedVisionTool is FeatureMatchTool featureTool)
            {
                var targetModel = featureTool.SelectedModel;
                if (featureTool.TrainPattern(trainingImage, targetModel))
                {
                    if (targetModel != null)
                        StatusMessage = $"Model '{targetModel.Name}' 학습 완료";
                    else
                        StatusMessage = $"새 Model 학습 완료 (총 {featureTool.Models.Count}개)";
                }
                else
                {
                    StatusMessage = "Feature 학습 실패";
                }
            }
        }

        /// <summary>
        /// 파라미터 자동 튜닝 (FeatureMatchTool용)
        /// </summary>
        private void AutoTuneParameters()
        {
            if (CurrentImage == null || CurrentImage.Empty())
            {
                StatusMessage = "이미지가 로드되지 않았습니다.";
                return;
            }

            Mat tuningImage = CurrentImage;

            if (SelectedVisionTool != null && SelectedVisionTool.UseROI
                && SelectedVisionTool.ROI.Width > 0 && SelectedVisionTool.ROI.Height > 0)
            {
                var roi = SelectedVisionTool.ROI;
                int x = Math.Max(0, roi.X);
                int y = Math.Max(0, roi.Y);
                int w = Math.Min(roi.Width, CurrentImage.Width - x);
                int h = Math.Min(roi.Height, CurrentImage.Height - y);
                var rect = new CvRect(x, y, w, h);

                if (rect.Width > 10 && rect.Height > 10)
                    tuningImage = new Mat(CurrentImage, rect);
            }

            if (SelectedVisionTool is FeatureMatchTool featureTool)
            {
                featureTool.AutoTuneParameters(tuningImage);
                StatusMessage = "파라미터 자동 튜닝 완료";
            }
        }

        #region ROI Methods

        /// <summary>
        /// ROI 생성 이벤트 처리
        /// </summary>
        public void OnROICreated(ROIShape roi)
        {
            StatusMessage = $"ROI 생성됨: {roi.Name} ({roi.ShapeType})";

            // ROI가 생성되면 현재 선택된 도구에 자동 적용
            if (SelectedVisionTool != null)
            {
                ApplyROIToTool(SelectedVisionTool, roi);
            }
        }

        /// <summary>
        /// ROI 수정 이벤트 처리
        /// </summary>
        public void OnROIModified(ROIShape roi)
        {
            if (SelectedVisionTool == null) return;

            // Check if the modified shape is the SearchRegion
            if (SelectedVisionTool is FeatureMatchTool ft && ft.AssociatedSearchRegionShape == roi)
            {
                _isSyncingROI = true;
                try
                {
                    ft.SearchRegion = roi.GetBoundingRect();
                    ft.UseSearchRegion = true;
                }
                finally
                {
                    _isSyncingROI = false;
                }
                return;
            }

            // Otherwise, apply as regular ROI
            if (SelectedROI == roi)
            {
                ApplyROIToTool(SelectedVisionTool, roi);
            }
        }

        /// <summary>
        /// ROI 선택 변경 이벤트 처리
        /// </summary>
        public void OnROISelectionChanged(ROIShape? roi)
        {
            SelectedROI = roi;

            if (roi != null)
            {
                StatusMessage = $"ROI 선택됨: {roi.Name}";
            }
        }

        /// <summary>
        /// 선택된 ROI를 현재 도구에 적용
        /// </summary>
        private void ApplyROIToSelectedTool()
        {
            if (SelectedVisionTool != null && SelectedROI != null)
            {
                ApplyROIToTool(SelectedVisionTool, SelectedROI);
            }
        }

        /// <summary>
        /// ROI를 특정 도구에 적용
        /// </summary>
        private void ApplyROIToTool(VisionToolBase tool, ROIShape roi)
        {
            _isSyncingROI = true;
            try
            {
                // 측정 도구에 검색 방향 화살표 표시
                roi.ShowSearchArrow = tool is LineFitTool or CaliperTool or CircleFitTool;

                if (roi is CircleROI circleROI && tool is CircleFitTool cft)
                {
                    // CircleROI → CircleFitTool 좌표 동기화
                    cft.CenterPoint = new OpenCvSharp.Point2d(circleROI.CenterX, circleROI.CenterY);
                    cft.ExpectedRadius = circleROI.Radius;

                    // 검색 방향 동기화
                    circleROI.SearchOutward = cft.SearchDirection == CircleSearchDirection.InwardToOutward;

                    // ROI 바운딩 rect도 저장 (GetROIImage 등 기본 기능용)
                    tool.ROI = circleROI.GetBoundingRect();
                }
                else if (roi is AnnulusROI annulusROI && tool is VisionTools.ImageProcessing.PolarUnwrapTool put)
                {
                    // AnnulusROI → PolarUnwrap 좌표 / 반경 동기화
                    put.CenterX = annulusROI.CenterX;
                    put.CenterY = annulusROI.CenterY;
                    put.InnerRadius = annulusROI.InnerRadius;
                    put.OuterRadius = annulusROI.OuterRadius;
                    tool.ROI = annulusROI.GetBoundingRect();
                    tool.AssociatedROIShape = annulusROI;
                }
                else if (roi is RectangleAffineROI affineROI)
                {
                    // Affine ROI: 실제 Width/Height와 회전 각도/중심 좌표 저장
                    tool.ROI = new CvRect(
                        (int)(affineROI.CenterX - affineROI.Width / 2),
                        (int)(affineROI.CenterY - affineROI.Height / 2),
                        (int)affineROI.Width,
                        (int)affineROI.Height);
                    tool.ROIAngle = affineROI.Angle;
                    tool.ROICenterX = affineROI.CenterX;
                    tool.ROICenterY = affineROI.CenterY;

                    // CaliperTool: 탐색 방향 동기화
                    if (tool is CaliperTool caliper)
                    {
                        affineROI.SearchAlongWidth = caliper.SearchAxis == CaliperSearchAxis.AlongWidth;
                    }
                }
                else
                {
                    var rect = roi.GetBoundingRect();
                    tool.ROI = rect;
                }

                tool.UseROI = true;
                tool.AssociatedROIShape = roi;

                var appliedRect = tool.ROI;
                StatusMessage = $"ROI 적용됨: {tool.Name} - ({appliedRect.X}, {appliedRect.Y}, {appliedRect.Width}, {appliedRect.Height})";
            }
            finally
            {
                _isSyncingROI = false;
            }
        }

        /// <summary>
        /// Search Region 생성 이벤트 처리 (FeatureMatchTool용)
        /// </summary>
        /// <summary>
        /// 이미지에서 픽셀 좌표가 클릭되면 호출 — 현재 선택된 도구가 ColorMatchTool이면 그 픽셀의 BGR을
        /// 추출해 SelectedModel을 한 픽셀 학습 (Lab 변환).
        /// </summary>
        public void OnColorPickedFromImage(double imgX, double imgY)
        {
            var img = VisionService.Instance.CurrentImage;
            if (img == null || img.Empty()) return;
            int ix = (int)Math.Round(imgX);
            int iy = (int)Math.Round(imgY);
            if (ix < 0 || iy < 0 || ix >= img.Width || iy >= img.Height) return;

            try
            {
                // BGR 3채널 또는 그레이 처리
                if (img.Channels() >= 3)
                {
                    var p = img.Get<Vec3b>(iy, ix);
                    if (SelectedVisionTool is VisionTools.Color.ColorMatchTool cm)
                    {
                        cm.PickFromPixel(p.Item0, p.Item1, p.Item2);
                        StatusMessage = $"Picked @({ix},{iy}) BGR=({p.Item0},{p.Item1},{p.Item2})";
                    }
                }
                else
                {
                    byte v = img.Get<byte>(iy, ix);
                    if (SelectedVisionTool is VisionTools.Color.ColorMatchTool cm)
                    {
                        cm.PickFromPixel(v, v, v);
                        StatusMessage = $"Picked @({ix},{iy}) Gray={v}";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Pick failed: {ex.Message}";
            }
        }

        public void OnSearchRegionCreated(ROIShape roi)
        {
            roi.Color = System.Windows.Media.Colors.Yellow;
            roi.Name = "SearchRegion";

            if (SelectedVisionTool is FeatureMatchTool ft)
            {
                _isSyncingROI = true;
                try
                {
                    ft.SearchRegion = roi.GetBoundingRect();
                    ft.UseSearchRegion = true;
                    ft.AssociatedSearchRegionShape = roi;
                }
                finally
                {
                    _isSyncingROI = false;
                }

                // Show both ROI and SearchRegion on canvas
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ft.AssociatedROIShape));

                StatusMessage = $"Search Region 설정됨: ({ft.SearchRegion.X}, {ft.SearchRegion.Y}, {ft.SearchRegion.Width}, {ft.SearchRegion.Height})";
            }
            else if (SelectedVisionTool is ShapeMatchTool sm)
            {
                _isSyncingROI = true;
                try
                {
                    sm.SearchRegion = roi.GetBoundingRect();
                    sm.UseSearchRegion = true;
                    sm.AssociatedSearchRegionShape = roi;
                }
                finally
                {
                    _isSyncingROI = false;
                }

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(sm.AssociatedROIShape));

                StatusMessage = $"Search Region 설정됨: ({sm.SearchRegion.X}, {sm.SearchRegion.Y}, {sm.SearchRegion.Width}, {sm.SearchRegion.Height})";
            }
            else if (SelectedVisionTool is VisionTools.Color.ColorExtractTool cx)
            {
                _isSyncingROI = true;
                try
                {
                    cx.SearchRegion = roi.GetBoundingRect();
                    cx.UseSearchRegion = true;
                    cx.AssociatedSearchRegionShape = roi;
                }
                finally
                {
                    _isSyncingROI = false;
                }

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(cx.AssociatedROIShape));

                StatusMessage = $"Search Region 설정됨: ({cx.SearchRegion.X}, {cx.SearchRegion.Y}, {cx.SearchRegion.Width}, {cx.SearchRegion.Height})";
            }
            else if (SelectedVisionTool is VisionTools.Color.ColorMatchTool cm)
            {
                _isSyncingROI = true;
                try
                {
                    cm.SearchRegion = roi.GetBoundingRect();
                    cm.UseSearchRegion = true;
                    cm.AssociatedSearchRegionShape = roi;
                }
                finally
                {
                    _isSyncingROI = false;
                }

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(cm.AssociatedROIShape));

                StatusMessage = $"Search Region 설정됨: ({cm.SearchRegion.X}, {cm.SearchRegion.Y}, {cm.SearchRegion.Width}, {cm.SearchRegion.Height})";
            }
            else if (SelectedVisionTool is OCVTool ocv)
            {
                _isSyncingROI = true;
                try
                {
                    ocv.SearchRegion = roi.GetBoundingRect();
                    ocv.UseSearchRegion = true;
                    ocv.AssociatedSearchRegionShape = roi;
                }
                finally
                {
                    _isSyncingROI = false;
                }

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ocv.AssociatedROIShape));

                StatusMessage = $"Search Region 설정됨: ({ocv.SearchRegion.X}, {ocv.SearchRegion.Y}, {ocv.SearchRegion.Width}, {ocv.SearchRegion.Height})";
            }
        }

        /// <summary>
        /// Search Region 해제 (FeatureMatchTool용)
        /// </summary>
        public void ClearSearchRegion()
        {
            if (SelectedVisionTool is FeatureMatchTool ft)
            {
                ft.UseSearchRegion = false;
                ft.SearchRegion = new CvRect();
                ft.AssociatedSearchRegionShape = null;

                // Refresh canvas to show only the ROI
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ft.AssociatedROIShape));

                StatusMessage = "Search Region이 해제되었습니다.";
            }
            else if (SelectedVisionTool is ShapeMatchTool sm)
            {
                sm.UseSearchRegion = false;
                sm.SearchRegion = new CvRect();
                sm.AssociatedSearchRegionShape = null;

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(sm.AssociatedROIShape));

                StatusMessage = "Search Region이 해제되었습니다.";
            }
            else if (SelectedVisionTool is VisionTools.Color.ColorExtractTool cx)
            {
                cx.UseSearchRegion = false;
                cx.SearchRegion = new CvRect();
                cx.AssociatedSearchRegionShape = null;

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(cx.AssociatedROIShape));

                StatusMessage = "Search Region이 해제되었습니다.";
            }
            else if (SelectedVisionTool is VisionTools.Color.ColorMatchTool cm)
            {
                cm.UseSearchRegion = false;
                cm.SearchRegion = new CvRect();
                cm.AssociatedSearchRegionShape = null;

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(cm.AssociatedROIShape));

                StatusMessage = "Search Region이 해제되었습니다.";
            }
            else if (SelectedVisionTool is OCVTool ocv)
            {
                ocv.UseSearchRegion = false;
                ocv.SearchRegion = new CvRect();
                ocv.AssociatedSearchRegionShape = null;

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ocv.AssociatedROIShape));

                StatusMessage = "Search Region이 해제되었습니다.";
            }
        }

        /// <summary>
        /// 선택된 도구의 ROI 해제
        /// </summary>
        public void ClearToolROI()
        {
            if (SelectedVisionTool != null)
            {
                SelectedVisionTool.UseROI = false;
                StatusMessage = $"ROI 해제됨: {SelectedVisionTool.Name}";
            }
        }

        /// <summary>
        /// 선택된 도구의 ROI 프록시 속성 변경 시 캔버스 ROI 동기화
        /// (텍스트 필드 편집 → 캔버스 업데이트)
        /// </summary>
        private void OnSelectedToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isSyncingROI) return;
            if (sender is not VisionToolBase tool) return;

            // CircleFitTool 속성 변경 → CircleROI 동기화
            if (tool is CircleFitTool cft &&
                e.PropertyName is nameof(CircleFitTool.CenterPoint) or nameof(CircleFitTool.ExpectedRadius)
                    or nameof(CircleFitTool.SearchDirection))
            {
                if (tool.AssociatedROIShape is CircleROI circleROI)
                {
                    circleROI.CenterX = cft.CenterPoint.X;
                    circleROI.CenterY = cft.CenterPoint.Y;
                    circleROI.Radius = cft.ExpectedRadius;
                    circleROI.SearchOutward = cft.SearchDirection == CircleSearchDirection.InwardToOutward;
                    tool.ROI = circleROI.GetBoundingRect();
                    WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(circleROI));
                }
            }

            // CaliperTool.SearchAxis 변경 → RectangleAffineROI 동기화
            if (tool is CaliperTool caliper && e.PropertyName is nameof(CaliperTool.SearchAxis))
            {
                if (tool.AssociatedROIShape is RectangleAffineROI affineROI)
                {
                    affineROI.SearchAlongWidth = caliper.SearchAxis == CaliperSearchAxis.AlongWidth;
                    WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(affineROI));
                }
            }

            // ROI 프록시 속성 변경 시 AssociatedROIShape 좌표 동기화
            if (e.PropertyName is nameof(VisionToolBase.ROIX) or nameof(VisionToolBase.ROIY)
                or nameof(VisionToolBase.ROIWidth) or nameof(VisionToolBase.ROIHeight))
            {
                if (tool.AssociatedROIShape is RectangleAffineROI affineROI)
                {
                    // Affine ROI: CenterX/Y와 Width/Height 업데이트 (Angle 유지)
                    affineROI.CenterX = tool.ROIX + tool.ROIWidth / 2.0;
                    affineROI.CenterY = tool.ROIY + tool.ROIHeight / 2.0;
                    affineROI.Width = Math.Max(1, tool.ROIWidth);
                    affineROI.Height = Math.Max(1, tool.ROIHeight);
                    tool.ROICenterX = affineROI.CenterX;
                    tool.ROICenterY = affineROI.CenterY;
                    WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(affineROI));
                }
                else if (tool.AssociatedROIShape is RectangleROI rectROI)
                {
                    rectROI.X = tool.ROIX;
                    rectROI.Y = tool.ROIY;
                    rectROI.Width = Math.Max(1, tool.ROIWidth);
                    rectROI.Height = Math.Max(1, tool.ROIHeight);
                    WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(rectROI));
                }
            }

            // UseSearchRegion 토글 시 검색 영역 그래픽 표시/숨김
            if (tool is FeatureMatchTool ft2 &&
                e.PropertyName is nameof(FeatureMatchTool.UseSearchRegion))
            {
                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(ft2.AssociatedROIShape));
            }

            // SearchRegion 프록시 속성 변경 시 AssociatedSearchRegionShape 좌표 동기화
            if (tool is FeatureMatchTool ft &&
                e.PropertyName is nameof(FeatureMatchTool.SearchRegionX) or nameof(FeatureMatchTool.SearchRegionY)
                    or nameof(FeatureMatchTool.SearchRegionWidth) or nameof(FeatureMatchTool.SearchRegionHeight))
            {
                if (ft.AssociatedSearchRegionShape is RectangleROI searchRectROI)
                {
                    searchRectROI.X = ft.SearchRegionX;
                    searchRectROI.Y = ft.SearchRegionY;
                    searchRectROI.Width = Math.Max(1, ft.SearchRegionWidth);
                    searchRectROI.Height = Math.Max(1, ft.SearchRegionHeight);
                    WeakReferenceMessenger.Default.Send(new RequestRefreshROIMessage(searchRectROI));
                }
            }
        }

        #endregion

        #region Recipe / Camera / Step Methods

        private void OnCurrentRecipeChanged(object? sender, Recipe? recipe)
        {
            if (recipe != null)
            {
                CurrentRecipeName = recipe.Name;
                MigrateStepSequencing(recipe);

                // Height Slicing 설정 복원
                if (recipe.HeightSlicing != null)
                {
                    _savedHeightBaseline = recipe.HeightSlicing.HeightBaseline;
                    _savedHeightLowerLimit = recipe.HeightSlicing.HeightLowerLimit;
                    _savedHeightUpperLimit = recipe.HeightSlicing.HeightUpperLimit;
                    IsHeightSlicingSaved = true;
                }
                else
                {
                    _savedHeightBaseline = null;
                    _savedHeightLowerLimit = null;
                    _savedHeightUpperLimit = null;
                    IsHeightSlicingSaved = false;
                }
                ClearHeightSlicingCommand.NotifyCanExecuteChanged();

                // MultiView 설정 복원
                LoadRobotSettingsFromRecipe(recipe);
            }
            else
            {
                CurrentRecipeName = "No Recipe";
            }

            RefreshSteps();
            AddStepCommand.NotifyCanExecuteChanged();
            SaveRecipeCommand.NotifyCanExecuteChanged();
        }

        private void LoadCameras()
        {
            Cameras.Clear();
            _cameraService.LoadCameraRegistry();
            foreach (var cam in _cameraService.GetAllCameras())
            {
                Cameras.Add(cam);
            }
        }

        private void RefreshSteps()
        {
            Steps.Clear();
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            foreach (var step in recipe.Steps.OrderBy(s => s.Sequence))
            {
                // 카메라 필터가 없거나 선택된 카메라와 일치하는 스텝만 표시
                if (SelectedCamera == null || string.IsNullOrEmpty(step.CameraId) || step.CameraId == SelectedCamera.Id)
                {
                    Steps.Add(step);
                }
            }

            // Step 구성 변경에 따라 로봇 관련 UI 갱신
            OnPropertyChanged(nameof(NeedsRobot));
            ConnectRobotCommand?.NotifyCanExecuteChanged();
            MultiViewCaptureCommand?.NotifyCanExecuteChanged();
            GenerateWaypointsCommand?.NotifyCanExecuteChanged();
            StartWaypointScanCommand?.NotifyCanExecuteChanged();
        }

        private void AddStep()
        {
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null || SelectedCamera == null) return;

            var step = _recipeService.AddStep(recipe, SelectedCamera.Id);
            if (step != null)
            {
                int camIdx = GetCameraDisplayIndex(SelectedCamera.Id);
                step.Name = $"{camIdx}-{step.Sequence}";
                RefreshSteps();
                SelectedStep = step;
                StatusMessage = $"Step added: {step.Name}";
            }
        }

        private void DeleteStep()
        {
            if (SelectedStep == null) return;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            if (_dialogService.ShowConfirmation(
                $"'{SelectedStep.Name}'을(를) 삭제하시겠습니까?",
                "Delete Step"))
            {
                var cameraId = SelectedStep.CameraId;
                _recipeService.RemoveStep(recipe, SelectedStep.Id);
                RenameStepsForCamera(recipe, cameraId);
                SelectedStep = null;
                ClearAllTools();
                RefreshSteps();
                StatusMessage = "Step deleted";
            }
        }

        private void MoveStepUp()
        {
            if (SelectedStep == null) return;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            if (SelectedStep.Sequence <= 1) return;

            var stepToMove = SelectedStep;
            _recipeService.MoveStep(recipe, stepToMove.Id, stepToMove.Sequence - 1);
            RenameStepsForCamera(recipe, stepToMove.CameraId);
            RefreshSteps();
            SelectedStep = stepToMove;
        }

        private void MoveStepDown()
        {
            if (SelectedStep == null) return;

            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null) return;

            int cameraStepCount = recipe.Steps.Count(s => s.CameraId == SelectedStep.CameraId);
            if (SelectedStep.Sequence >= cameraStepCount) return;

            var stepToMove = SelectedStep;
            _recipeService.MoveStep(recipe, stepToMove.Id, stepToMove.Sequence + 1);
            RenameStepsForCamera(recipe, stepToMove.CameraId);
            RefreshSteps();
            SelectedStep = stepToMove;
        }

        /// <summary>
        /// 스텝의 도구와 연결을 워크스페이스에 로드
        /// </summary>
        public void LoadStepToWorkspace(InspectionStep step)
        {
            // 기존 워크스페이스 정리
            ClearAllConnections();
            DroppedTools.Clear();
            _visionService.ClearTools();
            SelectedTool = null;

            // ToolConfig → VisionToolBase 역직렬화
            var toolConfigs = step.Tools.OrderBy(t => t.Sequence).ToList();
            var configToToolItem = new Dictionary<string, ToolItem>();

            foreach (var config in toolConfigs)
            {
                // 비활성(IsEnabled=false) 도구도 워크스페이스에 로드한다.
                // 실행 스킵은 VisionService.ExecuteAll()의 IsEnabled 체크가 담당하므로 여기서 제외하면 안 됨 —
                // 로드에서 제외하면 SaveWorkspaceToStep()이 Tools.Clear() 후 워크스페이스만 저장하므로
                // 로드→저장 한 번에 비활성 도구(와 그 연결)가 레시피에서 영구 삭제된다.
                var visionTool = ToolSerializer.DeserializeTool(config);
                if (visionTool == null) continue;

                var toolItem = new ToolItem
                {
                    Name = config.Name,
                    ToolType = config.ToolType,
                    X = config.X,
                    Y = config.Y,
                    VisionTool = visionTool
                };

                DroppedTools.Add(toolItem);
                _visionService.AddTool(visionTool);
                configToToolItem[config.Id] = toolItem;
            }

            // 연결 복원
            foreach (var config in toolConfigs)
            {
                if (!configToToolItem.ContainsKey(config.Id)) continue;
                var targetToolItem = configToToolItem[config.Id];

                foreach (var connConfig in config.Connections)
                {
                    if (configToToolItem.TryGetValue(connConfig.SourceToolId, out var sourceToolItem))
                    {
                        var connType = connConfig.ConnectionType switch
                        {
                            "Image" => ConnectionType.Image,
                            "Coordinates" => ConnectionType.Coordinates,
                            "Result" => ConnectionType.Result,
                            _ => ConnectionType.Image
                        };

                        AddConnection(sourceToolItem, targetToolItem, connType);
                    }
                }
            }

            StatusMessage = $"Step loaded: {step.Name} ({DroppedTools.Count} tools)";
        }

        /// <summary>
        /// 현재 워크스페이스의 도구와 연결을 선택된 스텝에 저장
        /// </summary>
        public void SaveWorkspaceToStep()
        {
            if (SelectedStep == null) return;

            SelectedStep.Tools.Clear();

            // 도구 ID 매핑 (ToolItem.Id → ToolConfig.Id)
            var toolItemToConfigId = new Dictionary<string, string>();

            foreach (var toolItem in DroppedTools)
            {
                if (toolItem.VisionTool == null) continue;

                var config = ToolSerializer.SerializeTool(toolItem.VisionTool);
                config.X = toolItem.X;
                config.Y = toolItem.Y;
                config.Connections = new List<ToolConnectionConfig>();

                toolItemToConfigId[toolItem.Id] = config.Id;
                SelectedStep.Tools.Add(config);
            }

            // 연결 정보 저장
            foreach (var conn in Connections)
            {
                if (conn.SourceToolItem == null || conn.TargetToolItem == null) continue;

                if (!toolItemToConfigId.TryGetValue(conn.SourceToolItem.Id, out var sourceConfigId)) continue;
                if (!toolItemToConfigId.TryGetValue(conn.TargetToolItem.Id, out var targetConfigId)) continue;

                var targetConfig = SelectedStep.Tools.FirstOrDefault(t => t.Id == targetConfigId);
                if (targetConfig != null)
                {
                    targetConfig.Connections.Add(new ToolConnectionConfig
                    {
                        SourceToolId = sourceConfigId,
                        ConnectionType = conn.Type.ToString()
                    });
                }
            }

            StatusMessage = $"Step saved: {SelectedStep.Name} ({SelectedStep.Tools.Count} tools)";
        }

        /// <summary>
        /// 예제 템플릿 갤러리를 열고, 선택된 템플릿의 툴 체인을 현재 스텝 워크스페이스에 생성.
        /// 스텝이 선택된 상태에서만 호출 가능 (CanExecute) — 생성 즉시 스텝에 저장된다.
        /// </summary>
        private void OpenTemplateGallery()
        {
            if (SelectedStep == null) return;

            var template = _dialogService.ShowTemplateGalleryDialog();
            if (template == null) return;

            try
            {
                var tools = RecipeTemplateCatalog.CreateTools(template);

                // 기존 툴과 겹치지 않도록 최하단 아래 새 행에 좌→우 배치
                var positions = RecipeTemplateCatalog.ComputeInsertPositions(
                    DroppedTools.Select(t => (t.X, t.Y)), tools.Count);

                var items = new List<ToolItem>(tools.Count);
                for (int i = 0; i < tools.Count; i++)
                {
                    var item = new ToolItem
                    {
                        Name = tools[i].Name,
                        ToolType = tools[i].ToolType,
                        X = positions[i].X,
                        Y = positions[i].Y,
                        VisionTool = tools[i]
                    };
                    DroppedTools.Add(item);
                    _visionService.AddTool(tools[i]);
                    items.Add(item);
                }

                foreach (var conn in template.Connections)
                    AddConnection(items[conn.SourceIndex], items[conn.TargetIndex], conn.Type);

                SaveWorkspaceToStep();
                StatusMessage = $"템플릿 생성: {template.Title} — 이미지(또는 3D 데이터) 로드 후 Run(F5)으로 테스트하세요";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"템플릿 생성 실패: {ex.Message}", "예제 템플릿");
            }
        }

        private int GetCameraDisplayIndex(string? cameraId)
        {
            if (string.IsNullOrEmpty(cameraId)) return 0;
            for (int i = 0; i < Cameras.Count; i++)
            {
                if (Cameras[i].Id == cameraId) return i + 1;
            }
            return 0;
        }

        private void RenameStepsForCamera(Recipe recipe, string cameraId)
        {
            int camIdx = GetCameraDisplayIndex(cameraId);
            var cameraSteps = recipe.Steps
                .Where(s => s.CameraId == cameraId)
                .OrderBy(s => s.Sequence)
                .ToList();
            for (int i = 0; i < cameraSteps.Count; i++)
            {
                cameraSteps[i].Name = camIdx > 0 ? $"{camIdx}-{i + 1}" : $"Step {i + 1}";
            }
        }

        private void MigrateStepSequencing(Recipe recipe)
        {
            var groups = recipe.Steps.GroupBy(s => s.CameraId);
            foreach (var group in groups)
            {
                var steps = group.OrderBy(s => s.Sequence).ToList();
                int camIdx = GetCameraDisplayIndex(group.Key);
                for (int i = 0; i < steps.Count; i++)
                {
                    steps[i].Sequence = i + 1;
                    if (camIdx > 0)
                        steps[i].Name = $"{camIdx}-{i + 1}";
                }
            }
        }

        #endregion

        #region Camera Connection & Acquisition

        private void ShowCameraInfo()
        {
            IsCameraInfoPopupOpen = !IsCameraInfoPopupOpen;
        }

        private async System.Threading.Tasks.Task ConnectCamera()
        {
            if (SelectedCamera == null) return;

            try
            {
                _cameraAcquisition?.Dispose();
                _cameraAcquisition = CameraAcquisitionFactory.Create(SelectedCamera);

                var success = await _cameraAcquisition.ConnectAsync(SelectedCamera);
                IsCameraConnected = success;

                StatusMessage = success
                    ? $"카메라 연결됨: {SelectedCamera.Name}"
                    : $"카메라 연결 실패: {SelectedCamera.Name}";

                if (success)
                    _ = RefreshStepCameraCurrentTextAsync();

                ConnectCameraCommand.NotifyCanExecuteChanged();
                DisconnectCameraCommand.NotifyCanExecuteChanged();
                MultiViewCaptureCommand.NotifyCanExecuteChanged();
                StartWaypointScanCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 연결 오류: {ex.Message}";
                IsCameraConnected = false;
            }
        }

        private async System.Threading.Tasks.Task DisconnectCamera()
        {
            try
            {
                StopCameraLive();   // 라이브 반복 Grab 중이면 먼저 중지

                if (_cameraAcquisition != null)
                {
                    await _cameraAcquisition.DisconnectAsync();
                    _cameraAcquisition.Dispose();
                    _cameraAcquisition = null;
                }

                IsCameraConnected = false;
                StatusMessage = "카메라 연결 해제됨";

                ConnectCameraCommand.NotifyCanExecuteChanged();
                DisconnectCameraCommand.NotifyCanExecuteChanged();
                MultiViewCaptureCommand.NotifyCanExecuteChanged();
                StartWaypointScanCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"카메라 연결 해제 오류: {ex.Message}";
            }
        }

        /// <summary>
        /// 선택 스텝의 카메라 파라미터(2D 노출/게인 + 3D 후처리/뎁스 범위)를 획득 전에 적용.
        /// Acquire / Camera Live 양쪽에서 호출 — 미지원 구현체는 no-op, 동일 값은 캐시 스킵.
        /// 2D 는 "카메라 설정 유지"(Use2DCameraDefault, 기본)면 push 하지 않는다 —
        /// Mech-Eye Viewer 등에서 튜닝한 카메라 값 보존.
        /// </summary>
        private async System.Threading.Tasks.Task ApplyStepCameraSettingsAsync()
        {
            if (SelectedStep == null || _cameraAcquisition == null) return;

            if (!SelectedStep.Use2DCameraDefault)
            {
                var applied = await _cameraAcquisition.ApplySettingsAsync(SelectedStep.Exposure, SelectedStep.Gain);
                // 지원 카메라(Mech 등 3D)인데 실패 → 침묵하지 않고 상태바로 알림 (camera.log 에 상세)
                if (!applied && IsStep3DCamera)
                    StatusMessage = "노출/게인 적용 실패 — logs\\camera.log 확인";
            }
            await _cameraAcquisition.Apply3DSettingsAsync(new Scan3DSettings
            {
                PostProcessPreset = SelectedStep.PointCloudPostProcess,
                UseDepthRange = SelectedStep.UseDepthRange,
                DepthRangeMinMm = SelectedStep.DepthRangeMinMm,
                DepthRangeMaxMm = SelectedStep.DepthRangeMaxMm
            });
        }

        private async System.Threading.Tasks.Task AcquireImage()
        {
            if (SelectedCamera == null) return;

            IsAcquiring = true;
            AcquireImageCommand.NotifyCanExecuteChanged();

            try
            {
                // Auto-connect if not connected
                if (!IsCameraConnected)
                {
                    await ConnectCamera();
                    if (!IsCameraConnected)
                    {
                        StatusMessage = "카메라 연결 실패로 획득 중단";
                        return;
                    }
                }

                // 선택된 스텝의 노출/게인 + 3D 후처리/뎁스 범위를 카메라에 적용 후 획득 —
                // 지원 구현체(Mech-Mind 등)만 실제 반영되고 나머지는 no-op.
                // 스텝 미선택 시 카메라 현재 설정 그대로.
                await ApplyStepCameraSettingsAsync();

                var result = await _cameraAcquisition!.AcquireAsync();

                if (result.Success && result.Image2D != null)
                {
                    CurrentImage = result.Image2D;

                    // 3D 포인트 클라우드가 있으면 적용
                    // OnCurrentPointCloudChanged에서 파라미터 복원 + 자동 Height Map 생성 처리
                    if (result.PointCloud != null)
                    {
                        CurrentPointCloud = result.PointCloud;
                    }

                    StatusMessage = result.Message;
                }
                else
                {
                    StatusMessage = $"이미지 획득 실패: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"이미지 획득 오류: {ex.Message}";
            }
            finally
            {
                IsAcquiring = false;
                AcquireImageCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// 카메라 직접 라이브 — 선택 스텝의 노출/게인을 적용한 뒤 AcquireAsync 를 반복해
        /// Image Viewer 를 갱신한다. VMS 공유 메모리 수신(StartLiveReceive)과 별개로,
        /// VMS 본체 없이 카메라만으로 라이브 확인이 필요할 때 사용 (노출 튜닝 등).
        /// 하드웨어 스트리밍(StartProcessing)이 아닌 소프트웨어 반복 Grab 이므로
        /// 프레임률은 카메라 획득 시간에 종속된다.
        /// </summary>
        private async System.Threading.Tasks.Task StartCameraLive()
        {
            if (SelectedCamera == null || IsCameraLive) return;

            // Acquire Image 와 동일 규칙: 미연결이면 자동 연결
            if (!IsCameraConnected)
            {
                await ConnectCamera();
                if (!IsCameraConnected)
                {
                    StatusMessage = "카메라 연결 실패로 라이브 중단";
                    return;
                }
            }

            _cameraLiveCts = new CancellationTokenSource();
            var ct = _cameraLiveCts.Token;
            IsCameraLive = true;
            NotifyCameraLiveCommands();
            StatusMessage = $"카메라 라이브 시작: {SelectedCamera.Name}";

            var stopped = "카메라 라이브 중지";
            try
            {
                while (!ct.IsCancellationRequested && _cameraAcquisition != null)
                {
                    // 선택 스텝의 노출/게인 + 3D 후처리를 매 프레임 적용 — 라이브 중 Camera
                    // Settings 패널에서 값을 편집하면 실시간 반영 (노출/후처리 튜닝 워크플로).
                    // 지원 구현체(Mech-Mind 등)는 동일 값 캐시로 no-op 이라 SDK 왕복 비용 없음.
                    await ApplyStepCameraSettingsAsync();

                    var result = await _cameraAcquisition.AcquireAsync();
                    if (ct.IsCancellationRequested) break;

                    if (!result.Success)
                    {
                        // 연속 실패 스팸 방지 — 첫 실패에서 라이브 중단
                        stopped = $"카메라 라이브 중단: {result.Message}";
                        break;
                    }

                    if (result.Image2D != null) CurrentImage = result.Image2D;
                    if (result.PointCloud != null) CurrentPointCloud = result.PointCloud;
                }
            }
            catch (Exception ex)
            {
                stopped = $"카메라 라이브 오류: {ex.Message}";
            }
            finally
            {
                _cameraLiveCts?.Dispose();
                _cameraLiveCts = null;
                IsCameraLive = false;
                NotifyCameraLiveCommands();
                StatusMessage = stopped;
            }
        }

        private void StopCameraLive()
        {
            _cameraLiveCts?.Cancel();
        }

        private void NotifyCameraLiveCommands()
        {
            AcquireImageCommand.NotifyCanExecuteChanged();
            StartCameraLiveCommand.NotifyCanExecuteChanged();
            StopCameraLiveCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// 카메라 목록을 CameraService에서 다시 로드
        /// </summary>
        public void RefreshCamerasFromService()
        {
            var selectedId = SelectedCamera?.Id;
            LoadCameras();

            // 이전에 선택된 카메라 복원
            if (selectedId != null)
            {
                SelectedCamera = Cameras.FirstOrDefault(c => c.Id == selectedId);
            }
        }

        #endregion

        #region Robot / MultiView

        private void LoadHandEyeCalibration()
        {
            if (string.IsNullOrEmpty(_handEyeCalibrationPath) || !System.IO.File.Exists(_handEyeCalibrationPath))
            {
                _handEyeMatrix = Matrix4x4.Identity;
                return;
            }

            if (_handEyeCalibration.LoadResult(_handEyeCalibrationPath))
            {
                _handEyeMatrix = _handEyeCalibration.ResultMatrix;
                StatusMessage = $"핸드-아이 캘리브레이션 로드됨: {System.IO.Path.GetFileName(_handEyeCalibrationPath)}";
            }
            else
            {
                _handEyeMatrix = Matrix4x4.Identity;
                StatusMessage = "핸드-아이 캘리브레이션 파일 로드 실패";
            }
        }

        [RelayCommand]
        private void BrowseHandEyeCalibration()
        {
            var path = _dialogService.ShowOpenFileDialog("핸드-아이 캘리브레이션 선택", "캘리브레이션 파일|*.json|모든 파일|*.*");
            if (!string.IsNullOrEmpty(path))
            {
                HandEyeCalibrationPath = path;
            }
        }

        private async System.Threading.Tasks.Task ConnectRobot()
        {
            try
            {
                _robotService?.Dispose();

                var config = new RobotConnectionConfig
                {
                    Convention = SelectedEulerConvention,
                    IpAddress = RobotIpAddress,
                    Port = RobotPort,
                    ProtocolMode = RobotProtocolMode
                };
                _robotService = RobotServiceFactory.Create(config);

                var success = await _robotService.ConnectAsync(RobotIpAddress, RobotPort);
                IsRobotConnected = success;

                if (success)
                {
                    // 세션 초기화
                    _multiViewSession?.Dispose();
                    _multiViewSession = new MultiViewSession
                    {
                        Strategy = SelectedRegistrationStrategy
                    };

                    StatusMessage = $"로봇 연결됨: {RobotIpAddress}:{RobotPort} ({SelectedEulerConvention})";
                }
                else
                {
                    StatusMessage = "로봇 연결 실패";
                }

                ConnectRobotCommand.NotifyCanExecuteChanged();
                DisconnectRobotCommand.NotifyCanExecuteChanged();
                MultiViewCaptureCommand.NotifyCanExecuteChanged();
                StartWaypointScanCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"로봇 연결 오류: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task DisconnectRobot()
        {
            try
            {
                if (_robotService != null)
                {
                    await _robotService.DisconnectAsync();
                    _robotService.Dispose();
                    _robotService = null;
                }

                IsRobotConnected = false;
                StatusMessage = "로봇 연결 해제됨";

                ConnectRobotCommand.NotifyCanExecuteChanged();
                DisconnectRobotCommand.NotifyCanExecuteChanged();
                MultiViewCaptureCommand.NotifyCanExecuteChanged();
                StartWaypointScanCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"로봇 해제 오류: {ex.Message}";
            }
        }

        /// <summary>
        /// 현재 위치에서 1회 촬영 + 로봇 포즈 수집 (MultiView 모드)
        /// 로봇이 이미 목표 위치에 도달한 상태에서 호출
        /// </summary>
        private async System.Threading.Tasks.Task MultiViewCapture()
        {
            if (_robotService == null || _cameraAcquisition == null || _multiViewSession == null)
                return;

            try
            {
                IsAcquiring = true;
                AcquireImageCommand.NotifyCanExecuteChanged();

                // Auto-connect camera
                if (!IsCameraConnected)
                {
                    await ConnectCamera();
                    if (!IsCameraConnected) return;
                }

                var scan = await _multiViewSession.CaptureAtCurrentPoseAsync(_robotService, _cameraAcquisition);

                if (scan != null)
                {
                    // 2D 이미지 표시 (마지막 촬영본)
                    if (scan.Image2D != null)
                        CurrentImage = scan.Image2D.Clone();

                    // 3D 포인트 클라우드 표시
                    if (scan.PointCloud != null)
                        CurrentPointCloud = scan.PointCloud;

                    StatusMessage = $"MultiView 스캔 {_multiViewSession.Scans.Count}개 수집완료 | {scan.Pose}";
                }

                OnPropertyChanged(nameof(MultiViewStatusText));
                MultiViewProcessCommand.NotifyCanExecuteChanged();
                MultiViewClearCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"MultiView 촬영 오류: {ex.Message}";
            }
            finally
            {
                IsAcquiring = false;
                AcquireImageCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// 수집된 스캔 데이터를 정합하여 통합 포인트 클라우드 생성
        /// </summary>
        private async System.Threading.Tasks.Task MultiViewProcess()
        {
            if (_multiViewSession == null || _multiViewSession.Scans.Count == 0) return;

            try
            {
                _multiViewSession.Strategy = SelectedRegistrationStrategy;
                StatusMessage = "MultiView 정합 처리 중...";

                var merged = await _multiViewSession.ProcessAsync();

                if (merged != null)
                {
                    CurrentPointCloud = merged;
                    StatusMessage = $"MultiView 정합 완료: {merged.PointCount:N0} pts";
                }
                else
                {
                    StatusMessage = "MultiView 정합 실패";
                }

                OnPropertyChanged(nameof(MultiViewStatusText));
            }
            catch (Exception ex)
            {
                StatusMessage = $"MultiView 정합 오류: {ex.Message}";
            }
        }

        #endregion

        #region Waypoint Planner

        private void GenerateWaypoints()
        {
            var recipe = _recipeService.CurrentRecipe;
            if (recipe == null || SelectedCamera == null) return;

            // Object 중심: 현재 PointCloud의 중심점, 없으면 원점
            var center = System.Numerics.Vector3.Zero;
            if (CurrentPointCloud != null && CurrentPointCloud.PointCount > 0)
            {
                float cx = 0, cy = 0, cz = 0;
                int count = CurrentPointCloud.PointCount;
                for (int i = 0; i < count; i++)
                {
                    cx += CurrentPointCloud.Positions[i].X;
                    cy += CurrentPointCloud.Positions[i].Y;
                    cz += CurrentPointCloud.Positions[i].Z;
                }
                center = new System.Numerics.Vector3(cx / count, cy / count, cz / count);
            }

            _waypointPlanner.GenerateWaypoints(
                SelectedWaypointPattern, WaypointCount, center, ScanDistance, ScanElevation);

            // 기존 로봇 웨이포인트 스텝 제거 후 웨이포인트별 스텝 생성
            SyncWaypointsToSteps(recipe);

            // 3D Viewer에 웨이포인트 표시 갱신
            OnPropertyChanged(nameof(WaypointPlanner));
            ClearWaypointsCommand.NotifyCanExecuteChanged();
            StartWaypointScanCommand.NotifyCanExecuteChanged();
            StatusMessage = $"{_waypointPlanner.Waypoints.Count}개 웨이포인트 생성됨 ({SelectedWaypointPattern})";
        }

        private void ClearWaypoints()
        {
            var recipe = _recipeService.CurrentRecipe;

            _waypointPlanner.ClearWaypoints();

            // 로봇 웨이포인트가 있는 스텝 제거
            if (recipe != null)
            {
                var robotSteps = recipe.Steps.Where(s => s.RobotWaypoint != null).ToList();
                foreach (var step in robotSteps)
                    recipe.Steps.Remove(step);
                RefreshSteps();
            }

            OnPropertyChanged(nameof(WaypointPlanner));
            ClearWaypointsCommand.NotifyCanExecuteChanged();
            StartWaypointScanCommand.NotifyCanExecuteChanged();
            StatusMessage = "웨이포인트 초기화됨";
        }

        /// <summary>
        /// 웨이포인트 목록을 InspectionStep과 동기화:
        /// 기존 로봇 스텝 제거 → 웨이포인트별 새 스텝 생성
        /// </summary>
        private void SyncWaypointsToSteps(Recipe recipe)
        {
            var cameraId = SelectedCamera?.Id ?? string.Empty;

            // 기존 로봇 웨이포인트 스텝 제거
            var robotSteps = recipe.Steps.Where(s => s.RobotWaypoint != null && s.CameraId == cameraId).ToList();
            foreach (var step in robotSteps)
                recipe.Steps.Remove(step);

            // 웨이포인트별 새 스텝 생성
            int camIdx = GetCameraDisplayIndex(cameraId);
            foreach (var wp in _waypointPlanner.Waypoints)
            {
                var step = new InspectionStep
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"{camIdx}-{wp.Index + 1} {wp.Name}",
                    Sequence = wp.Index + 1,
                    CameraId = cameraId,
                    RobotWaypoint = wp,
                    Tools = new List<ToolConfig>()
                };
                recipe.Steps.Add(step);
            }

            RefreshSteps();
        }

        /// <summary>
        /// 웨이포인트 자동 스캔 실행: VMS → Robot 포즈 전송 → 이동 → 촬영 → 정합
        /// </summary>
        private async System.Threading.Tasks.Task StartWaypointScan()
        {
            if (_robotService == null || _cameraAcquisition == null) return;

            // 세션 초기화
            _multiViewSession?.Dispose();
            _multiViewSession = new MultiViewSession
            {
                Strategy = SelectedRegistrationStrategy
            };

            _waypointPlanner.ResetCompletionStatus();
            _waypointScanCts = new CancellationTokenSource();

            StopWaypointScanCommand.NotifyCanExecuteChanged();
            StartWaypointScanCommand.NotifyCanExecuteChanged();

            try
            {
                // 핸드-아이 역행렬 계산
                Matrix4x4.Invert(_handEyeMatrix, out var handEyeInverse);

                var success = await _waypointPlanner.ExecuteScanSequenceAsync(
                    _robotService,
                    _cameraAcquisition,
                    _multiViewSession,
                    handEyeInverse,
                    SelectedEulerConvention,
                    "MOVEJ",
                    300,
                    _waypointScanCts.Token);

                if (success && _multiViewSession.Scans.Count > 0)
                {
                    // 자동 정합
                    StatusMessage = "정합 처리 중...";
                    var merged = await _multiViewSession.ProcessAsync();
                    if (merged != null)
                    {
                        CurrentPointCloud = merged;
                        StatusMessage = $"웨이포인트 스캔 완료: {merged.PointCount:N0} pts";
                    }
                }

                OnPropertyChanged(nameof(MultiViewStatusText));
                MultiViewProcessCommand.NotifyCanExecuteChanged();
                MultiViewClearCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"웨이포인트 스캔 오류: {ex.Message}";
            }
            finally
            {
                _waypointScanCts?.Dispose();
                _waypointScanCts = null;
                StopWaypointScanCommand.NotifyCanExecuteChanged();
                StartWaypointScanCommand.NotifyCanExecuteChanged();
            }
        }

        private void StopWaypointScan()
        {
            _waypointScanCts?.Cancel();
            StatusMessage = "웨이포인트 스캔 중지 요청됨";
        }

        /// <summary>스캔 데이터 초기화</summary>
        private void MultiViewClear()
        {
            _multiViewSession?.ClearScans();
            OnPropertyChanged(nameof(MultiViewStatusText));
            MultiViewProcessCommand.NotifyCanExecuteChanged();
            MultiViewClearCommand.NotifyCanExecuteChanged();
            StatusMessage = "MultiView 세션 초기화됨";
        }

        #endregion

        #region 3D Point Cloud

        /// <summary>
        /// 샘플 포인트 클라우드 데이터 생성 (200x200 격자 지형)
        /// </summary>
        private void LoadSamplePointCloud()
        {
            const int gridSize = 200;
            int count = gridSize * gridSize;
            var positions = new Vector3[count];
            var colors = new System.Windows.Media.Color[count];
            float spacing = 1.0f;
            float offsetX = -gridSize * spacing * 0.5f;
            float offsetZ = -gridSize * spacing * 0.5f;

            int idx = 0;
            for (int iz = 0; iz < gridSize; iz++)
            {
                for (int ix = 0; ix < gridSize; ix++)
                {
                    float x = offsetX + ix * spacing;
                    float z = offsetZ + iz * spacing;

                    float y = 30f * MathF.Sin(x * 0.05f) * MathF.Cos(z * 0.05f)
                            + 15f * MathF.Sin(x * 0.1f + 1f)
                            + 10f * MathF.Cos(z * 0.08f + 2f)
                            + 5f * MathF.Sin((x + z) * 0.15f);

                    positions[idx] = new Vector3(x, y, z);
                    colors[idx] = System.Windows.Media.Color.FromRgb(200, 200, 200);
                    idx++;
                }
            }

            CurrentPointCloud = new PointCloudData
            {
                Name = "Sample Terrain",
                Positions = positions,
                Colors = colors,
                GridWidth = gridSize,
                GridHeight = gridSize
            };

            StatusMessage = $"3D 샘플 지형 데이터 로드 완료: {count:N0} points";
        }

        partial void OnCurrentPointCloudChanged(PointCloudData? value)
        {
            // 도구가 접근 가능하도록 VisionService 슬롯과 동기화
            VisionService.Instance.CurrentPointCloud = value;

            if (value != null && value.Positions.Length > 0)
            {
                int count = value.PointCount;
                float zMin = float.MaxValue;
                float zMax = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    float z = value.Positions[i].Z;
                    if (z < zMin) zMin = z;
                    if (z > zMax) zMax = z;
                }

                PointCloudYMin = zMin;
                PointCloudYMax = zMax;
                DepthRangeMin = zMin;
                DepthRangeMax = zMax;

                if (_savedHeightBaseline.HasValue)
                {
                    HeightBaseline = _savedHeightBaseline.Value;
                    HeightLowerLimit = _savedHeightLowerLimit!.Value;
                    HeightUpperLimit = _savedHeightUpperLimit!.Value;
                }
                else
                {
                    HeightBaseline = 0f;
                    HeightLowerLimit = zMin;
                    HeightUpperLimit = zMax;
                }
            }

            GenerateHeightMapCommand.NotifyCanExecuteChanged();
            SaveHeightSlicingCommand.NotifyCanExecuteChanged();
            SavePointCloudCommand.NotifyCanExecuteChanged();

            // 자동 Height Map 생성 — PointCloud가 설정되면 즉시 rule-base 도구 사용 가능
            if (value != null && value.IsOrganized)
            {
                GenerateHeightMap();

                // 레시피에 저장된 DepthRange 슬라이더 값 복원 (GenerateHeightMap이 덮어쓴 후)
                var slicing = _recipeService.CurrentRecipe?.HeightSlicing;
                if (slicing != null)
                {
                    DepthRangeMin = slicing.DepthRangeMin;
                    DepthRangeMax = slicing.DepthRangeMax;
                }
            }
        }

        private void SavePointCloud()
        {
            if (CurrentPointCloud == null) return;

            var filePath = _dialogService.ShowSaveFileDialog(
                "VPC Point Cloud (*.vpc)|*.vpc", ".vpc", CurrentPointCloud.Name);

            if (filePath != null)
            {
                try
                {
                    CurrentPointCloud.SaveToFile(filePath);
                    StatusMessage = $"3D 데이터 저장 완료: {System.IO.Path.GetFileName(filePath)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"3D 데이터 저장 실패: {ex.Message}";
                }
            }
        }

        private void LoadPointCloud()
        {
            var filePath = _dialogService.ShowOpenFileDialog(
                "3D 데이터 열기",
                "VPC Point Cloud (*.vpc)|*.vpc|All Files (*.*)|*.*");

            if (filePath != null)
            {
                try
                {
                    var data = PointCloudData.LoadFromFile(filePath);
                    var old = CurrentPointCloud;
                    CurrentPointCloud = data;
                    old?.Dispose();
                    StatusMessage = $"3D 데이터 로드 완료: {data.Name} ({data.PointCount:N0} points)";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"3D 데이터 로드 실패: {ex.Message}";
                }
            }
        }

        // 정사투영 지원으로 비격자(unorganized) 점군도 허용 — Registration/Cluster
        // 이후에도 Height Map 생성 가능. 자동 생성은 성능상 organized 전용 유지.
        private bool CanGenerateHeightMap() => CurrentPointCloud is { PointCount: > 0 };

        private void SaveHeightSlicing()
        {
            _savedHeightBaseline = HeightBaseline;
            _savedHeightLowerLimit = HeightLowerLimit;
            _savedHeightUpperLimit = HeightUpperLimit;
            IsHeightSlicingSaved = true;
            ClearHeightSlicingCommand.NotifyCanExecuteChanged();

            // 레시피에 영속 저장
            var recipe = _recipeService.CurrentRecipe;
            if (recipe != null)
            {
                recipe.HeightSlicing = new HeightSlicingSettings
                {
                    HeightBaseline = HeightBaseline,
                    HeightLowerLimit = HeightLowerLimit,
                    HeightUpperLimit = HeightUpperLimit,
                    DepthRangeMin = DepthRangeMin,
                    DepthRangeMax = DepthRangeMax
                };
                _recipeService.SaveRecipe(recipe);
            }

            StatusMessage = $"Height Slicing 설정 저장됨: Baseline={HeightBaseline:F1}, 범위=[{HeightLowerLimit:F1}, {HeightUpperLimit:F1}]";
        }

        private void ClearHeightSlicing()
        {
            _savedHeightBaseline = null;
            _savedHeightLowerLimit = null;
            _savedHeightUpperLimit = null;
            IsHeightSlicingSaved = false;
            ClearHeightSlicingCommand.NotifyCanExecuteChanged();

            // 레시피에서도 제거
            var recipe = _recipeService.CurrentRecipe;
            if (recipe != null)
            {
                recipe.HeightSlicing = null;
                _recipeService.SaveRecipe(recipe);
            }

            StatusMessage = "Height Slicing 설정 초기화됨";
        }

        partial void OnDepthRangeMinChanged(float value) => ApplyDepthRangeFilter();

        partial void OnDepthRangeMaxChanged(float value) => ApplyDepthRangeFilter();

        private void ApplyDepthRangeFilter()
        {
            // Depth Map 탭의 범위 슬라이더 재색칠은 DepthMapViewer 가 자체 처리(RangeMin/Max).
            // 여기서는 2D 탭이 Height Map 폴백을 표시 중일 때만 그 폴백을 갱신 —
            // 카메라 2D 이미지가 표시 중이면 절대 덮어쓰지 않는다.
            if (!_currentImageIsHeightMap)
                return;

            var depthMap32F = _visionService.CurrentDepthMap32F;
            if (depthMap32F == null)
                return;

            // CurrentDepthMap32F는 baseline 상대값 (pos.Z - HeightBaseline) 이므로
            // DepthRangeMin/Max (raw Z값)에서 baseline을 빼서 좌표계 일치
            float filterMin = DepthRangeMin - HeightBaseline;
            float filterMax = DepthRangeMax - HeightBaseline;
            CurrentImage = PointCloudConverter.DepthMap32FTo8UFiltered(depthMap32F, filterMin, filterMax);
            _currentImageIsHeightMap = true;
        }

        private void GenerateHeightMap()
        {
            if (CurrentPointCloud == null || !CurrentPointCloud.IsOrganized)
                return;

            try
            {
                var (heightMap8U, _, metadata) = _visionService.GenerateHeightMap(
                    CurrentPointCloud, HeightBaseline, HeightLowerLimit, HeightUpperLimit);

                // 2D 탭은 카메라의 진짜 2D 이미지를 유지 (Mech-Eye Viewer UX — 노출/게인
                // 확인 가능). Height Map 8U 는 2D 이미지가 없는 경우(.vpc 로드 등)만
                // 폴백으로 표시 — 이 폴백이 없으면 오프라인 워크플로에서 2D 도구
                // 파이프라인이 입력 이미지 없음으로 실행 불가.
                if (CurrentImage == null || _currentImageIsHeightMap)
                {
                    CurrentImage = heightMap8U;
                    _currentImageIsHeightMap = true;
                }
                else
                {
                    heightMap8U.Dispose();
                }
                CurrentHeightMapMetadata = metadata;
                // float map은 VisionService.CurrentDepthMap32F에 clone 저장됨
                // metadata.DepthMap32F에도 원본 참조 보관됨 (GetInterpolatedZ용)

                // DepthRange 슬라이더를 HeightLimit 범위로 동기화
                DepthRangeMin = HeightLowerLimit;
                DepthRangeMax = HeightUpperLimit;

                StatusMessage = $"Height Map 생성 완료: {metadata.Width}x{metadata.Height} " +
                    $"(Baseline={HeightBaseline:F1}, Range=[{HeightLowerLimit:F1}, {HeightUpperLimit:F1}])";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Height Map 생성 실패: {ex.Message}";
            }
        }

        #endregion

        /// <summary>
        /// 명령의 CanExecute 상태 갱신
        /// </summary>
        private void NotifyCommandsCanExecuteChanged()
        {
            RunAllCommand.NotifyCanExecuteChanged();
            RunSelectedCommand.NotifyCanExecuteChanged();
            TrainPatternCommand.NotifyCanExecuteChanged();
            AutoTuneCommand.NotifyCanExecuteChanged();
            AddStepCommand.NotifyCanExecuteChanged();
            DeleteStepCommand.NotifyCanExecuteChanged();
            MoveStepUpCommand.NotifyCanExecuteChanged();
            MoveStepDownCommand.NotifyCanExecuteChanged();
            OpenTemplateGalleryCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// 도구에 맞는 ToolSettingsViewModel 생성
        /// </summary>
        private static ToolSettingsViewModelBase? CreateToolSettingsViewModel(VisionToolBase tool)
        {
            return tool switch
            {
                GrayscaleTool t => new GrayscaleToolSettingsViewModel(t),
                BlurTool t => new BlurToolSettingsViewModel(t),
                ThresholdTool t => new ThresholdToolSettingsViewModel(t),
                EdgeDetectionTool t => new EdgeDetectionToolSettingsViewModel(t),
                MorphologyTool t => new MorphologyToolSettingsViewModel(t),
                HistogramTool t => new HistogramToolSettingsViewModel(t),
                VisionTools.ImageProcessing.ImageEnhanceTool t => new ImageEnhanceToolSettingsViewModel(t),
                VisionTools.ImageProcessing.PolarUnwrapTool t => new PolarUnwrapToolSettingsViewModel(t),
                FeatureMatchTool t => new FeatureMatchToolSettingsViewModel(t),
                ShapeMatchTool t => new ShapeMatchToolSettingsViewModel(t),
                BlobTool t => new BlobToolSettingsViewModel(t),
                CaliperTool t => new CaliperToolSettingsViewModel(t),
                LineFitTool t => new LineFitToolSettingsViewModel(t),
                CircleFitTool t => new CircleFitToolSettingsViewModel(t),
                GeometryTool t => new GeometryToolSettingsViewModel(t),
                HeightSlicerTool t => new HeightSlicerToolSettingsViewModel(t),
                PlaneFitTool t => new PlaneFitToolSettingsViewModel(t),
                Geometry3DTool t => new Geometry3DToolSettingsViewModel(t),
                ResultTool t => new ResultToolSettingsViewModel(t),
                OCRTool t => new OCRToolSettingsViewModel(t),
                OCVTool t => new OCVToolSettingsViewModel(t),
                CodeReaderTool t => new CodeReaderToolSettingsViewModel(t),
                DetectionTool t => new DetectionToolSettingsViewModel(t),
                ClassifyTool t => new ClassifyToolSettingsViewModel(t),
                AnomalyTool t => new AnomalyToolSettingsViewModel(t),
                EnsembleTool t => new EnsembleToolSettingsViewModel(t),
                SegmentationTool t => new SegmentationToolSettingsViewModel(t),
                YoloSegTool t => new YoloSegToolSettingsViewModel(t),
                VisionTools.Calibration.ImageRectifyTool t => new ImageRectifyToolSettingsViewModel(t),
                VisionTools.Color.ColorExtractTool t => new ColorExtractToolSettingsViewModel(t),
                VisionTools.Color.ColorMatchTool t => new ColorMatchToolSettingsViewModel(t),
                VisionTools.PointCloud.PointCloudFilterTool t => new PointCloudFilterToolSettingsViewModel(t),
                VisionTools.PointCloud.PointCloudRegistrationTool t => new PointCloudRegistrationToolSettingsViewModel(t),
                VisionTools.PointCloud.PointCloudClusterTool t => new PointCloudClusterToolSettingsViewModel(t),
                VisionTools.PointCloud.PointCloudDeviationTool t => new PointCloudDeviationToolSettingsViewModel(t),
                VisionTools.PointCloud.PointCloudMaskCropTool t => new PointCloudMaskCropToolSettingsViewModel(t),
                VisionTools.SurfaceAnalysis.PhotometricStereoTool t => new PhotometricStereoToolSettingsViewModel(t),
                _ => null
            };
        }

        #endregion

        #region VMS Frame Receive

        private async System.Threading.Tasks.Task ReceiveFromVms()
        {
            try
            {
                _sharedFrameReader ??= new SharedFrameReader();

                if (!_sharedFrameReader.TryConnect())
                {
                    StatusMessage = "VMS에 연결할 수 없습니다 (VMS가 실행 중인지 확인)";
                    _sharedFrameReader.Dispose();
                    _sharedFrameReader = null;
                    return;
                }

                if (!_sharedFrameReader.IsWriterAlive)
                {
                    StatusMessage = "VMS가 비활성 상태입니다";
                    return;
                }

                var frame = _sharedFrameReader.TryReadFrame(skipIfSameFrame: false);
                if (frame == null)
                {
                    StatusMessage = "프레임 읽기 실패 (VMS에서 Grab을 먼저 실행하세요)";
                    return;
                }

                ApplySharedFrame(frame);
                StatusMessage = $"VMS 프레임 수신 완료 (Frame #{frame.FrameCounter})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"VMS 프레임 수신 오류: {ex.Message}";
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task StartLiveReceive()
        {
            _sharedFrameReader ??= new SharedFrameReader();

            if (!_sharedFrameReader.TryConnect())
            {
                StatusMessage = "VMS에 연결할 수 없습니다 (VMS가 실행 중인지 확인)";
                _sharedFrameReader.Dispose();
                _sharedFrameReader = null;
                return;
            }

            IsReceivingFromVms = true;
            NotifyLiveReceiveCommands();
            StatusMessage = "VMS 라이브 수신 시작";

            _liveReceiveCts = new CancellationTokenSource();
            var ct = _liveReceiveCts.Token;

            await System.Threading.Tasks.Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!_sharedFrameReader.IsWriterAlive)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            StatusMessage = "VMS가 종료되었습니다";
                            StopLiveReceive();
                        });
                        break;
                    }

                    if (_sharedFrameReader.WaitForFrame(500))
                    {
                        var frame = _sharedFrameReader.TryReadFrame();
                        if (frame != null)
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                ApplySharedFrame(frame);
                            });
                        }
                    }
                }
            }, ct);
        }

        private void StopLiveReceive()
        {
            _liveReceiveCts?.Cancel();
            _liveReceiveCts?.Dispose();
            _liveReceiveCts = null;
            IsReceivingFromVms = false;
            NotifyLiveReceiveCommands();
            StatusMessage = "VMS 라이브 수신 중지";
        }

        private void ApplySharedFrame(SharedFrameData frame)
        {
            if (frame.Image2D != null)
            {
                CurrentImage = frame.Image2D;
            }

            if (frame.PointCloud != null)
            {
                CurrentPointCloud = frame.PointCloud;
            }
        }

        private void NotifyLiveReceiveCommands()
        {
            ReceiveFromVmsCommand.NotifyCanExecuteChanged();
            StartLiveReceiveCommand.NotifyCanExecuteChanged();
            StopLiveReceiveCommand.NotifyCanExecuteChanged();
        }

        #endregion

        #region Recipe & Camera Manager Commands

        public Recipe? GetCurrentRecipe() => _recipeService.CurrentRecipe;

        public void SaveCurrentRecipe()
        {
            var currentRecipe = _recipeService.CurrentRecipe;
            if (currentRecipe != null)
            {
                SaveWorkspaceToStep();
                SaveRobotSettingsToRecipe(currentRecipe);
                currentRecipe.ModifiedAt = DateTime.Now;
                _recipeService.SaveRecipe(currentRecipe);
                StatusMessage = $"Recipe saved: {currentRecipe.Name}";
            }
            else
            {
                _dialogService.ShowInformation(
                    "저장할 레시피가 없습니다. Recipe Manager에서 레시피를 로드하세요.",
                    "No Recipe");
            }
        }

        public void OpenRecipeManager()
        {
            var loadedRecipe = _dialogService.ShowRecipeManagerDialog();
            if (loadedRecipe != null)
            {
                CurrentRecipeName = loadedRecipe.Name;
                LoadRobotSettingsFromRecipe(loadedRecipe);
                StatusMessage = $"Recipe loaded: {loadedRecipe.Name}";
            }
        }

        /// <summary>로봇 설정을 Recipe에 저장</summary>
        private void SaveRobotSettingsToRecipe(Recipe recipe)
        {
            recipe.RobotIpAddress = RobotIpAddress;
            recipe.RobotPort = RobotPort;
            recipe.EulerConvention = SelectedEulerConvention;
            recipe.RegistrationStrategy = SelectedRegistrationStrategy;
            recipe.HandEyeCalibrationPath = HandEyeCalibrationPath;
        }

        /// <summary>Recipe에서 로봇 설정 복원</summary>
        private void LoadRobotSettingsFromRecipe(Recipe recipe)
        {
            RobotIpAddress = recipe.RobotIpAddress;
            RobotPort = recipe.RobotPort;
            SelectedEulerConvention = recipe.EulerConvention;
            SelectedRegistrationStrategy = recipe.RegistrationStrategy;
            HandEyeCalibrationPath = recipe.HandEyeCalibrationPath;
            OnPropertyChanged(nameof(NeedsRobot));
        }

        public void OpenCameraManager()
        {
            _dialogService.ShowCameraManagerDialog();
            RefreshCamerasFromService();
        }

        public void OpenSequenceEditor()
        {
            // host(VMS App.xaml.cs) 또는 standalone(VMS.VisionSetup App.xaml.cs) 이 채운 디바이스 entry
            // 들을 그대로 전달 — PLC + IO 보드가 DeviceType 정보와 함께 콤보에 표시됨.
            _dialogService.ShowSequenceEditorDialog(VMS.VisionSetup.Services.SequenceEditorContext.ExtraDevices);
        }

        public void OpenCalibrationManager()
        {
            _dialogService.ShowCalibrationManagerDialog();
        }

        public void LaunchDeepLearning()
        {
            try
            {
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var exePath = System.IO.Path.Combine(currentDir, "VMS.DeepLearning.exe");

                if (!System.IO.File.Exists(exePath))
                {
                    _dialogService.ShowWarning(
                        "VMS.DeepLearning 프로그램을 찾을 수 없습니다.\n" +
                        "프로젝트를 먼저 빌드해 주세요.",
                        "Deep Learning");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"VMS.DeepLearning 실행 실패: {ex.Message}", "Error");
            }
        }

        /// <summary>
        /// WeldTeach(로봇 티칭 PoC) 실행 파일 탐색 — 실행 메뉴 표시 여부의 게이트.
        /// PoC 는 GS 인증·배포 범위 외지만 컴파일 게이트(#if DEBUG)를 쓰면 설치본(Release)
        /// 에서 테스트할 수 없으므로, <b>exe 존재 기반 런타임 게이트</b>를 쓴다:
        /// 배포 MSI 에는 WeldTeach 가 포함되지 않아 인증·현장 설치본에서는 메뉴가 나타나지
        /// 않고, exe 가 있는 개발·테스트 PC 에서는 Release 실행본에서도 쓸 수 있다.
        /// </summary>
        public static string? FindWeldTeachExe()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                System.IO.Path.Combine(baseDir, "VMS.WeldTeach.exe"),
                // 개발 트리: VMS.VisionSetup\bin\{cfg}\net8.0-windows7.0\ → 저장소 루트 → WeldTeach 출력
                System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir,
                    @"..\..\..\..\VMS.WeldTeach\bin\Debug\net8.0-windows7.0\VMS.WeldTeach.exe")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir,
                    @"..\..\..\..\VMS.WeldTeach\bin\Release\net8.0-windows7.0\VMS.WeldTeach.exe")),
            };
            return candidates.FirstOrDefault(System.IO.File.Exists);
        }

        /// <summary>WeldTeach 실행 (메뉴는 FindWeldTeachExe 성공 시에만 주입됨 — MainView).</summary>
        public void LaunchWeldTeach()
        {
            try
            {
                var exePath = FindWeldTeachExe();
                if (exePath == null)
                {
                    _dialogService.ShowWarning(
                        "VMS.WeldTeach 실행 파일을 찾을 수 없습니다.\n" +
                        "dotnet build VMS.WeldTeach\\VMS.WeldTeach.csproj 로 먼저 빌드해 주세요.",
                        "WeldTeach");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    // OCCT 네이티브 DLL 로드가 실행 파일 폴더 기준 — 작업 폴더를 맞춰 준다
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)!,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"VMS.WeldTeach 실행 실패: {ex.Message}", "Error");
            }
        }

        public void RenameTool(ToolItem tool)
        {
            if (tool == null) return;

            var newName = _dialogService.ShowRenameDialog(tool.Name);
            if (newName != null)
            {
                tool.Name = newName;
                if (tool.VisionTool != null)
                {
                    tool.VisionTool.Name = newName;
                }
            }
        }

        #endregion
    }
}
