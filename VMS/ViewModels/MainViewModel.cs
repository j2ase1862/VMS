using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.Core.Imaging;
using VMS.Core.Interfaces;
using VMS.Core.Models.Predictive;
using VMS.Core.Models.Updates;
using VMS.Core.Security;
using VMS.Core.Services;
using VMS.Interfaces;
using VMS.Models;
using VMS.PLC.Interfaces;
using VMS.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VMS.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "BODA Vision System";

        #region Predictive Defect Rate Widget (Plan §5.3 V5)

        /// <summary>
        /// 가장 최근 폴링 결과. PredictionPollingService.PredictionUpdated 에서 UI 스레드로 마샬링되어 들어옴.
        /// 파생 표시 properties(PredictionDisplayText/PredictionAccentBrush/PredictionTooltip)는
        /// partial method OnCurrentPredictionChanged 에서 OnPropertyChanged 로 갱신.
        /// </summary>
        [ObservableProperty]
        private PredictionCurrentDto? _currentPrediction;

        partial void OnCurrentPredictionChanged(PredictionCurrentDto? value)
        {
            OnPropertyChanged(nameof(PredictionDisplayText));
            OnPropertyChanged(nameof(PredictionAccentBrush));
            OnPropertyChanged(nameof(PredictionTooltip));
            OnPropertyChanged(nameof(IsPredictionAvailable));
        }

        public bool IsPredictionAvailable => CurrentPrediction != null;

        public string PredictionDisplayText
        {
            get
            {
                var p = CurrentPrediction;
                if (p is null) return "—";
                if (p.Status == "ok" && p.PredictedNgRate.HasValue)
                    return $"{p.PredictedNgRate.Value * 100:F1}%";
                return p.Status switch
                {
                    "no_model" => "모델 미등록",
                    "no_data"  => "데이터 없음",
                    "error"    => "오류",
                    _ => p.Status,
                };
            }
        }

        /// <summary>
        /// 위젯 BorderBrush — 임계 색상(plan §5.3 "임계 초과 시 색 변경").
        /// 정상 OK < 5% : Green, < 10% : Orange, ≥ 10% : Red. 비-ok 상태: Gray.
        /// </summary>
        public Brush PredictionAccentBrush
        {
            get
            {
                var p = CurrentPrediction;
                if (p?.Status != "ok" || !p.PredictedNgRate.HasValue)
                    return new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)); // gray
                var pct = p.PredictedNgRate.Value * 100;
                if (pct >= 10.0) return new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // red
                if (pct >= 5.0)  return new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // orange
                return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // green
            }
        }

        public string PredictionTooltip
        {
            get
            {
                var p = CurrentPrediction;
                if (p is null) return "예측 데이터 수신 대기 중";
                if (p.Status == "ok" && p.PredictedNgRate.HasValue)
                {
                    var pct = p.PredictedNgRate.Value * 100;
                    var hour = p.WindowStart?.ToLocalTime().ToString("MM-dd HH:mm") ?? "?";
                    return $"다음 1시간 예상 NG율: {pct:F2}%\n대상 윈도우: {hour}\n모델: {p.ModelName ?? "?"} v{p.ModelVersion ?? "?"}\n이번 시간 검사: {p.InspectionCountThisHour}건";
                }
                return string.IsNullOrEmpty(p.Message) ? p.Status : $"{p.Status}: {p.Message}";
            }
        }

        private void OnPredictionUpdated(PredictionCurrentDto dto)
        {
            // Polling 서비스는 Timer 스레드에서 호출 — UI 바인딩은 dispatcher 필수
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                CurrentPrediction = dto;
            });
        }

        #endregion

        private bool _isSidePanelOpen;
        public bool IsSidePanelOpen
        {
            get => _isSidePanelOpen;
            set
            {
                // 패널 열기 시 로그인 필요
                if (value && !_isSidePanelOpen && _userService != null && !_userService.IsLoggedIn)
                {
                    if (!_dialogService.ShowLoginDialog(_userService))
                    {
                        OnPropertyChanged(); // ToggleButton 원복
                        return;
                    }
                    UpdateUserDisplay();
                    LogService?.Log($"User logged in: {_userService.CurrentUser?.DisplayName}", LogLevel.Success, "Auth");
                }
                SetProperty(ref _isSidePanelOpen, value);
            }
        }

        [ObservableProperty]
        private bool _isRecipePanelOpen;

        [ObservableProperty]
        private CameraViewModel? _selectedCamera;

        [ObservableProperty]
        private ObservableCollection<CameraViewModel> _cameras = new();

        [ObservableProperty]
        private string _currentRecipeName = "No Recipe Loaded";

        [ObservableProperty]
        private Recipe? _currentRecipe;

        private string? _currentRecipeFilePath;

        [ObservableProperty]
        private ObservableCollection<RecipeInfo> _recipeList = new();

        [ObservableProperty]
        private RecipeInfo? _selectedRecipeInfo;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private bool _isLiveMode;

        [ObservableProperty]
        private bool _isRollerInspecting;

        [ObservableProperty]
        private string _systemStatus = "Ready";

        [ObservableProperty]
        private int _totalInspections;

        [ObservableProperty]
        private int _totalPass;

        [ObservableProperty]
        private int _totalFail;

        [ObservableProperty]
        private double _canvasWidth = 1920;

        [ObservableProperty]
        private double _canvasHeight = 1080;

        private const double CanvasPadding = 100;
        private const double MinCanvasWidth = 1920;
        private const double MinCanvasHeight = 1080;

        public double PassRate => TotalInspections > 0
            ? Math.Round((double)TotalPass / TotalInspections * 100, 1)
            : 0;

        // ── User management ──
        [ObservableProperty]
        private string _currentUserDisplay = string.Empty;

        [ObservableProperty]
        private UserGrade _currentUserGrade;

        public bool CanEditRecipe => _userService?.HasPermission(UserPermission.EditRecipe) ?? true;
        public bool CanDeleteRecipe => _userService?.HasPermission(UserPermission.DeleteRecipe) ?? true;
        public bool CanManageUsers => _userService?.HasPermission(UserPermission.ManageUsers) ?? false;
        public bool CanLaunchVisionSetup => _userService?.HasPermission(UserPermission.LaunchVisionSetup) ?? true;
        public bool CanLaunchAppSetup => _userService?.HasPermission(UserPermission.LaunchAppSetup) ?? true;
        public bool HasConnectedCamera => Cameras.Any(c => c.IsConnected);
        /// <summary>
        /// 검사 시작/정지 권한. **시스템 사용자가 로그인하지 않았으면 허용**하고, 로그인한
        /// 경우에만 등급 권한을 적용한다 (사용자 매뉴얼 §3.5 "시스템 사용자 미로그인 시 기본 허용").
        ///
        /// 신원 체계가 둘로 나뉘어 있기 때문이다 — 시스템 사용자(Admin/Engineer/Operator 등급)는
        /// 설정·레시피 편집용이고, 생산 신원은 <b>작업자 계정(사번+PIN 키오스크 로그인)</b> 이
        /// 담당한다(검사 이력에 OperatorId 기록). AUTO RUN 에 시스템 사용자 로그인까지 요구하면
        /// 현장이 이중 로그인을 해야 해 공용 계정 상시 로그인으로 우회되기 쉽다.
        ///
        /// 기존 코드의 <c>?? true</c> 는 "서비스 미주입" 만 커버해 운영 빌드에서는 사실상
        /// 로그인이 필수였고, 매뉴얼·버튼 툴팁과 어긋나 있었다 (2026-08-14 정정).
        /// </summary>
        private bool HasStartStopPermission => Services.StartStopGate.Allows(_userService);

        public bool CanStartStop =>
            HasConnectedCamera
            && HasStartStopPermission
            // Web 통합 환경 (OperatorAuthService 존재) 에서는 Operator 로그인 + WO 선택 필수.
            // OperatorAuthService 가 없으면 standalone — 기존 동작 유지.
            && (_operatorAuthService == null
                || (IsOperatorLoggedIn
                    && SelectedWorkOrder != null
                    // Completed/Closed 상태에서는 결과 업로드해도 카운터가 증가하지 않으므로 비활성.
                    && (SelectedWorkOrder.Status == "Planned" || SelectedWorkOrder.Status == "InProgress")));

        /// <summary>
        /// 카메라 조작(Grab / Live Start) 활성 조건 — CanStartStop 에서 Work Order 요건을 뺀 것.
        /// Grab/Live 는 레시피·WO 데이터를 사용하지 않는 순수 카메라 조작이므로, 카메라 상태
        /// 확인·노출 튜닝을 WO 선택 전에도 할 수 있어야 한다 (현장 피드백 2026-07-16).
        /// 검사 결과가 생성되는 AUTO RUN / Roller 는 추적성(WO/Lot/SN) 확보를 위해
        /// CanStartStop(WO 필수)을 유지한다.
        /// </summary>
        public bool CanOperateCamera =>
            HasConnectedCamera
            && HasStartStopPermission
            && (_operatorAuthService == null || IsOperatorLoggedIn);

        /// <summary>
        /// 단발 Grab 활성 조건 — CanOperateCamera 에서 연결 요건을 뺀 것. Grab 은
        /// 미연결 시 자동 연결하므로 연결 여부로 막으면 최초 연결 진입점이 사라진다.
        /// 권한/Operator 로그인 게이트는 Live 와 동일하게 적용 (현장 실증 2026-08-06:
        /// Grab 만 게이트가 빠져 Live 와 비대칭이던 것을 정리).
        /// </summary>
        public bool CanGrabCamera =>
            HasStartStopPermission
            && (_operatorAuthService == null || IsOperatorLoggedIn);

        /// <summary>
        /// 카메라 조작 버튼이 비활성일 때 툴팁으로 보여줄 사유 — 현장에서 "왜 안
        /// 눌리는지" 확인할 방법이 없던 문제 해소. 조작 가능하면 null.
        /// </summary>
        public string? CameraOperateBlockReason =>
            !HasStartStopPermission
                ? "로그인한 시스템 사용자에게 Start/Stop 권한이 없습니다 — 로그아웃하거나 권한 있는 계정으로 로그인하세요"
                : (_operatorAuthService != null && !IsOperatorLoggedIn)
                    ? "Operator 로그인이 필요합니다"
                    : !HasConnectedCamera
                        ? "연결된 카메라가 없습니다 — Grab 을 누르면 자동 연결됩니다"
                        : null;

        /// <summary>
        /// Roller 검사 토글 활성 — 검사 결과가 생기므로 CanStartStop(WO 포함) 유지.
        /// 추가로 롤러 검사는 라인스캔 전용이므로, AppSetup 구성 카메라 중
        /// LineScan2D/LineScan3D 가 있을 때만 활성 (에어리어 전용 장비에서는 항상 비활성).
        /// </summary>
        public bool CanToggleRollerInspection =>
            CanStartStop
            && Cameras.Any(c => c.CameraType == VMS.Camera.Models.CameraType.LineScan2D
                             || c.CameraType == VMS.Camera.Models.CameraType.LineScan3D);

        // ── Equipment status (StatusBar) ──
        public string PlcVendorName { get; }
        public string PlcIpAddress { get; }
        public bool IsPlcConfigured => PlcVendorName != "None";
        public bool IsPlcConnected => _plcConnection?.IsConnected ?? false;

        // ── IO 보드 상태 (PLC 없이 보드만으로 운전하는 구성에서 필수 단서) ──
        // 과거에는 보드 연결 실패가 화면 어디에도 드러나지 않아, 벤더 None 일 때 시뮬레이션
        // PLC 가 "연결됨" 으로 뜨는 것과 겹쳐 정상으로 오해되기 쉬웠다.
        private readonly IReadOnlyList<VMS.PLC.Interfaces.IIoBoardConnection> _ioBoards;

        /// <summary>IO 보드가 하나라도 설정돼 있는지 — 헤더 칩 표시 여부.</summary>
        public bool HasIoBoards => _ioBoards.Count > 0;

        public int TotalIoBoardCount => _ioBoards.Count;
        public int ConnectedIoBoardCount => _ioBoards.Count(b => b.IsConnected);

        /// <summary>전 보드 정상 연결 여부 — 하나라도 끊겨 있으면 false(적색 표시).</summary>
        public bool IsIoBoardHealthy => HasIoBoards && ConnectedIoBoardCount == TotalIoBoardCount;

        public string IoBoardStatusText => $"IO {ConnectedIoBoardCount}/{TotalIoBoardCount}";

        /// <summary>보드는 상태 변경 이벤트가 없어 갱신 시점(운전 시작 등)에 명시적으로 호출.</summary>
        public void RefreshIoBoardStatus()
        {
            OnPropertyChanged(nameof(ConnectedIoBoardCount));
            OnPropertyChanged(nameof(IsIoBoardHealthy));
            OnPropertyChanged(nameof(IoBoardStatusText));
        }

        private bool _isWebConnected;
        public bool IsWebConnected
        {
            get => _isWebConnected;
            set
            {
                if (SetProperty(ref _isWebConnected, value))
                    WebStatusText = value ? "Web Connected" : "Web Disconnected";
            }
        }

        private string _webStatusText = "Web Disconnected";
        public string WebStatusText
        {
            get => _webStatusText;
            set => SetProperty(ref _webStatusText, value);
        }


        public int ConnectedCameraCount => Cameras.Count(c => c.IsConnected);
        public int TotalCameraCount => Cameras.Count;

        /// <summary>
        /// "All" = 전부 연결, "Partial" = 일부, "None" = 0대 연결, "Empty" = 카메라 미설정
        /// </summary>
        public string CameraConnectionStatus
        {
            get
            {
                if (TotalCameraCount == 0) return "Empty";
                var connected = ConnectedCameraCount;
                if (connected == TotalCameraCount) return "All";
                if (connected > 0) return "Partial";
                return "None";
            }
        }

        // ── Dashboard ──
        [ObservableProperty]
        private DashboardViewModel _dashboard = new();

        [ObservableProperty]
        private NgImageItem? _selectedNgImage;

        // ── System Log ──
        public ISystemLogService? LogService { get; }

        private readonly IConfigurationService _configService;
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly IProcessService _processService;
        private readonly IInspectionService _inspectionService;
        private readonly IAutoProcessService? _autoProcessService;
        private readonly IUserService? _userService;
        private readonly SharedFrameWriter? _sharedFrameWriter;
        private readonly IPlcConnection? _plcConnection;
        private readonly IRollerInspectionService? _rollerInspectionService;
        private readonly HeartbeatService? _heartbeatService;
        private readonly IParameterSyncService? _parameterSyncService;
        private readonly VMS.Core.Services.OperatorAuthService? _operatorAuthService;
        private readonly VMS.Core.Services.WorkOrderClient? _workOrderClient;
        private readonly VMS.Core.Services.LotClient? _lotClient;
        private readonly VMS.Core.Services.VmsHubClient? _vmsHubClient;
        private readonly IPredictionPollingService? _predictionPollingService;
        private readonly IUpdateService? _updateService;
        private int? _lastCompletedNotifiedWoId; // C5: 중복 Completed 알림 가드
        private readonly Action _shutdownAction;
        private SystemConfiguration _systemConfig;
        // 검사 판정 이미지(양품/불량) 저장 설정 — 시작 시 로드, 설정 윈도우 종료 후 재로드.
        private ImageSaveOptions _imageSaveOptions = ImageSaveOptions.LoadFromAppData();
        // 검사 이미지 Web 업로드(Upload 모드) — 없으면 미전송.
        private readonly VMS.Services.ImageUpload.IImageUploadService? _imageUploadService;

        public MainViewModel(
            IConfigurationService configService,
            IRecipeService recipeService,
            IDialogService dialogService,
            IProcessService processService,
            IInspectionService inspectionService,
            Action shutdownAction,
            IAutoProcessService? autoProcessService = null,
            IUserService? userService = null,
            ISystemLogService? logService = null,
            SharedFrameWriter? sharedFrameWriter = null,
            IPlcConnection? plcConnection = null,
            string plcVendorName = "None",
            string plcIpAddress = "",
            IRollerInspectionService? rollerInspectionService = null,
            HeartbeatService? heartbeatService = null,
            IParameterSyncService? parameterSyncService = null,
            VMS.Core.Services.OperatorAuthService? operatorAuthService = null,
            VMS.Core.Services.WorkOrderClient? workOrderClient = null,
            VMS.Core.Services.LotClient? lotClient = null,
            VMS.Core.Services.VmsHubClient? vmsHubClient = null,
            IPredictionPollingService? predictionPollingService = null,
            IUpdateService? updateService = null,
            VMS.Services.ImageUpload.IImageUploadService? imageUploadService = null,
            IReadOnlyList<VMS.PLC.Interfaces.IIoBoardConnection>? ioBoards = null)
        {
            _ioBoards = ioBoards ?? Array.Empty<VMS.PLC.Interfaces.IIoBoardConnection>();
            _configService = configService;
            _recipeService = recipeService;
            _dialogService = dialogService;
            _processService = processService;
            _inspectionService = inspectionService;
            _autoProcessService = autoProcessService;
            _userService = userService;
            LogService = logService;
            _sharedFrameWriter = sharedFrameWriter;
            _plcConnection = plcConnection;
            PlcVendorName = plcVendorName;
            PlcIpAddress = plcIpAddress;
            _rollerInspectionService = rollerInspectionService;
            _heartbeatService = heartbeatService;
            _parameterSyncService = parameterSyncService;
            _operatorAuthService = operatorAuthService;
            _workOrderClient = workOrderClient;
            _lotClient = lotClient;
            _vmsHubClient = vmsHubClient;
            _predictionPollingService = predictionPollingService;
            _updateService = updateService;
            _imageUploadService = imageUploadService;
            _shutdownAction = shutdownAction;
            _systemConfig = new SystemConfiguration();

            // Subscribe to Web heartbeat connection state changes
            if (_heartbeatService != null)
            {
                _heartbeatService.ConnectionStatusChanged += OnWebConnectionStatusChanged;
            }

            // Stage 1: 작업자 세션 변경 시 ViewModel + SyncService 동기화
            if (_operatorAuthService != null)
            {
                _operatorAuthService.SessionChanged += OnOperatorSessionChanged;
                _ = _operatorAuthService.FetchCurrentSessionAsync();
            }

            // Stage 3: WO 진행률 / 완료 이벤트 구독 (서버가 업로드 응답에 포함)
            if (_parameterSyncService != null)
            {
                _parameterSyncService.WorkOrderProgressed += OnWorkOrderProgressed;
                _parameterSyncService.WorkOrderCompleted += OnWorkOrderCompletedFromServer;
            }

            // C5: SignalR — 다른 클라이언트의 업로드도 받아 헤더 칩 진행률 즉시 반영
            if (_vmsHubClient != null)
            {
                _vmsHubClient.WorkOrderUpdated += OnWorkOrderProgressed;
                _vmsHubClient.WorkOrderCompleted += OnWorkOrderCompletedFromServer;
            }

            // Plan §5.3 V5 — 예측 폴링 결과를 UI 스레드로 marshall
            if (_predictionPollingService != null)
            {
                _predictionPollingService.PredictionUpdated += OnPredictionUpdated;
            }

            // D8: InspectionService 가 push 한 레코드에 WorkOrderNo 보강 (Id 만 가지고 있음).
            // RecordAdded 는 UI 스레드에서 발생 (Add 가 Dispatcher.Invoke 함).
            VMS.Core.Services.RecentInspectionsService.Instance.RecordAdded += record =>
            {
                if (record.WorkOrderId.HasValue
                    && SelectedWorkOrder?.Id == record.WorkOrderId
                    && string.IsNullOrEmpty(record.WorkOrderNo))
                {
                    record.WorkOrderNo = SelectedWorkOrder.OrderNo;
                }
            };


            // Subscribe to PLC connection state changes
            if (_plcConnection != null)
            {
                _plcConnection.ConnectionStateChanged += (_, _) =>
                    OnPropertyChanged(nameof(IsPlcConnected));
            }

            // Subscribe to roller inspection results
            if (_rollerInspectionService != null)
            {
                _rollerInspectionService.PaperCaptured += result =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 캡처된 용지 이미지를 첫 번째 카메라에 표시
                        var cam = Cameras.FirstOrDefault(c => c.IsEnabled);
                        if (cam != null && result.PaperImage != null)
                        {
                            cam.CurrentImage = result.PaperImage;
                        }

                        LogService?.Log(
                            $"Paper captured: {result.FrameCount} frames, {result.CaptureTimeMs:F0}ms",
                            LogLevel.Success, "Roller");
                    });
                };
            }


            // Initialize user display
            UpdateUserDisplay();

            LoadConfiguration();
            RefreshRecipeList();

            // Web 레시피 목록 변경 시 자동 갱신
            if (_parameterSyncService != null)
            {
                _parameterSyncService.RecipeListChanged += recipes =>
                {
                    Application.Current.Dispatcher.BeginInvoke(async () => await SyncWebRecipesToLocalAsync());
                };
            }

            // VisionSetup 이 레시피를 저장하면 실행 중인 VMS 에 자동 반영 (2026-08-10 현장:
            // 저장해도 VMS 는 구버전 사본으로 계속 검사 → 수동 재로드 전까지 미반영이었음)
            _recipeService.ExternalRecipeFileChanged += OnExternalRecipeFileChanged;
            _recipeService.StartWatchingRecipeFiles();

            LogService?.Log("Application started", LogLevel.Success, "System");
        }

        private void UpdateUserDisplay()
        {
            if (_userService?.CurrentUser != null)
            {
                var user = _userService.CurrentUser;
                CurrentUserDisplay = $"[{user.Grade}] {user.DisplayName}";
                CurrentUserGrade = user.Grade;
            }
            else
            {
                CurrentUserDisplay = string.Empty;
            }

            OnPropertyChanged(nameof(CanEditRecipe));
            OnPropertyChanged(nameof(CanDeleteRecipe));
            OnPropertyChanged(nameof(CanManageUsers));
            OnPropertyChanged(nameof(CanLaunchVisionSetup));
            OnPropertyChanged(nameof(CanLaunchAppSetup));
            OnPropertyChanged(nameof(CanStartStop));
            OnPropertyChanged(nameof(CanOperateCamera));
            OnPropertyChanged(nameof(CanGrabCamera));
            OnPropertyChanged(nameof(CameraOperateBlockReason));
            OnPropertyChanged(nameof(CanToggleRollerInspection));
            // VMS 시스템 사용자 변경 시 Role-게이트 properties 도 재평가 (Admin 우회 통과 반영)
            OnPropertyChanged(nameof(CanLeadOrAbove));
            OnPropertyChanged(nameof(CanSupervisor));
        }

        /// <summary>
        /// Update canvas size based on camera positions
        /// Called when cameras are moved or resized
        /// </summary>
        public void UpdateCanvasSize()
        {
            if (Cameras.Count == 0)
            {
                CanvasWidth = MinCanvasWidth;
                CanvasHeight = MinCanvasHeight;
                return;
            }

            double maxX = 0;
            double maxY = 0;

            foreach (var cam in Cameras)
            {
                var rightEdge = cam.X + cam.Width;
                var bottomEdge = cam.Y + cam.Height;

                if (rightEdge > maxX) maxX = rightEdge;
                if (bottomEdge > maxY) maxY = bottomEdge;
            }

            // Add padding and ensure minimum size
            CanvasWidth = Math.Max(MinCanvasWidth, maxX + CanvasPadding);
            CanvasHeight = Math.Max(MinCanvasHeight, maxY + CanvasPadding);
        }

        private void LoadConfiguration()
        {
            _systemConfig = _configService.LoadSystemConfiguration();
            ApplicationTitle = _systemConfig.ApplicationName;

            // Load cameras from configuration
            Cameras.Clear();
            double xOffset = 10;
            double yOffset = 10;

            foreach (var camConfig in _systemConfig.Cameras)
            {
                var camVm = CameraViewModel.FromConfiguration(camConfig, _dialogService, _configService, _inspectionService);
                camVm.X = xOffset;
                camVm.Y = yOffset;
                WireCameraEvents(camVm);
                Cameras.Add(camVm);

                xOffset += 420;
                if (xOffset > 1200)
                {
                    xOffset = 10;
                    yOffset += 320;
                }
            }

            // If no cameras configured, add default virtual cameras
            if (Cameras.Count == 0)
            {
                AddDefaultCameras();
            }

            // Apply saved layout if exists — default 카메라도 안정 ID 를 사용하므로
            // 두 경로(system_config / default) 모두에서 복원 가능. 반드시 default 추가 *이후*.
            var layoutConfig = _configService.LoadLayoutConfiguration();
            foreach (var layout in layoutConfig.CameraLayouts)
            {
                var camera = Cameras.FirstOrDefault(c => c.Id == layout.CameraId);
                camera?.ApplyLayout(layout);
            }

            // Update canvas size based on loaded camera positions
            UpdateCanvasSize();
        }

        private void WireCameraEvents(CameraViewModel cam)
        {
            cam.FrameAcquired += result => _sharedFrameWriter?.WriteFrame(result);

            cam.InspectionCompleted += (cameraName, ok, image, stepNumber, correlationKey) =>
            {
                TotalInspections++;
                if (ok) TotalPass++;
                else TotalFail++;

                // Tact 는 AUTO RUN 연속 운전 중에만 갱신 (수동 Inspect 는 대기 시간이
                // Tact 로 오인됨 — 2026-08-10 현장). 처리 시간은 항상 표시.
                Dashboard.RecordInspectionResult(ok, cameraName, image,
                    processingTimeMs: cam.LastExecutionTimeMs, updateTact: IsRunning);

                // 판정 이미지 저장 — 설정(imageSave) 의 폴더 구조/파일명 규칙으로 OK/NG 이미지 기록.
                var imageContext = new InspectionImageContext
                {
                    Ok = ok,
                    CameraName = cameraName,
                    StepNumber = stepNumber,
                    CorrelationKey = correlationKey,
                    RecipeName = (CurrentRecipeName == "No Recipe Loaded") ? string.Empty : (CurrentRecipeName ?? string.Empty),
                    WorkOrder = SelectedWorkOrder?.OrderNo ?? WorkOrderIdText ?? string.Empty,
                    Lot = LotIdText ?? string.Empty,
                    Serial = SerialNumberText ?? string.Empty,
                    Timestamp = DateTime.Now
                };
                VMS.Services.InspectionImageSaver.Save(_imageSaveOptions, image, imageContext);

                // Web 업로드 큐 적재(Upload 모드 + OK/NG 전송 토글 시) — 택트와 분리된 비동기.
                _imageUploadService?.Enqueue(image, imageContext, _imageSaveOptions);

                LogService?.Log(
                    $"Inspection {(ok ? "OK" : "NG")} - {cameraName}",
                    ok ? LogLevel.Success : LogLevel.Warning,
                    "Inspection");
            };

            cam.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CameraViewModel.IsConnected))
                {
                    OnPropertyChanged(nameof(HasConnectedCamera));
                    OnPropertyChanged(nameof(CanStartStop));
                    OnPropertyChanged(nameof(CanOperateCamera));
                    OnPropertyChanged(nameof(CameraOperateBlockReason));
                    OnPropertyChanged(nameof(CanToggleRollerInspection));
                    OnPropertyChanged(nameof(ConnectedCameraCount));
                    OnPropertyChanged(nameof(CameraConnectionStatus));
                }
            };
        }

        private void AddDefaultCameras()
        {
            for (int i = 1; i <= 2; i++)
            {
                var vm = new CameraViewModel(_dialogService, _configService, _inspectionService)
                {
                    // 재실행 시 layout_config.json 매칭이 가능하도록 안정 ID 사용.
                    Id = $"default-camera-{i}",
                    Name = $"Camera {i}",
                    IpAddress = $"192.168.0.{100 + i}",
                    Manufacturer = CameraManufacturer.Virtual,
                    X = 10 + (i - 1) * 420,
                    Y = 10,
                    Width = 400,
                    Height = 300
                };
                vm.Steps.Add(new StepViewModel
                {
                    StepNumber = 1,
                    Name = "Step 1",
                    Exposure = 5000,
                    Gain = 1.0
                });
                vm.SelectedStep = vm.Steps[0];
                WireCameraEvents(vm);
                Cameras.Add(vm);
            }
        }

        [RelayCommand]
        private void ToggleSidePanel()
        {
            IsSidePanelOpen = !IsSidePanelOpen;
        }

        [RelayCommand]
        private void SaveLayout()
        {
            var layoutConfig = new LayoutConfiguration
            {
                CameraLayouts = Cameras.Select(c => c.ToLayout()).ToList()
            };

            if (_configService.SaveLayoutConfiguration(layoutConfig))
            {
                _dialogService.ShowInformation(
                    "레이아웃이 저장되었습니다.",
                    "Save Layout");
            }
            else
            {
                _dialogService.ShowError(
                    "레이아웃 저장에 실패했습니다.",
                    "Error");
            }
        }

        [RelayCommand]
        private void ToggleRecipePanel()
        {
            IsRecipePanelOpen = !IsRecipePanelOpen;
            if (IsRecipePanelOpen)
            {
                RefreshRecipeList();
            }
        }

        [RelayCommand]
        private void LoadRecipe()
        {
            if (SelectedRecipeInfo == null) return;

            var recipe = _recipeService.LoadRecipe(SelectedRecipeInfo.FilePath);
            if (recipe != null)
            {
                CurrentRecipe = recipe;
                CurrentRecipeName = recipe.Name;
                _currentRecipeFilePath = SelectedRecipeInfo.FilePath;
                IsRecipePanelOpen = false;
                SystemStatus = $"Recipe loaded: {recipe.Name}";
                LogService?.Log($"Recipe loaded: {recipe.Name}", LogLevel.Info, "Recipe");

                // Propagate recipe to all cameras
                foreach (var cam in Cameras)
                    cam.SetRecipe(recipe);
            }
            else
            {
                _dialogService.ShowError(
                    "레시피를 불러올 수 없습니다.",
                    "Error");
            }
        }

        [RelayCommand]
        private void NewRecipe()
        {
            var recipe = _recipeService.CreateNewRecipe("New Recipe");
            _recipeService.SaveRecipe(recipe);
            RefreshRecipeList();
            CurrentRecipe = recipe;
            CurrentRecipeName = recipe.Name;
            SystemStatus = "New recipe created";
        }

        [RelayCommand]
        private void SaveCurrentRecipe()
        {
            if (CurrentRecipe == null)
            {
                _dialogService.ShowWarning(
                    "저장할 레시피가 없습니다.",
                    "Warning");
                return;
            }

            if (_recipeService.SaveRecipe(CurrentRecipe))
            {
                RefreshRecipeList();
                SystemStatus = $"Recipe saved: {CurrentRecipe.Name}";
            }
            else
            {
                _dialogService.ShowError(
                    "레시피 저장에 실패했습니다.",
                    "Error");
            }
        }

        [RelayCommand]
        private void DeleteRecipe()
        {
            if (SelectedRecipeInfo == null) return;

            if (_dialogService.ShowConfirmation(
                $"레시피 '{SelectedRecipeInfo.Name}'을(를) 삭제하시겠습니까?",
                "Delete Recipe"))
            {
                if (_recipeService.DeleteRecipe(SelectedRecipeInfo.Id))
                {
                    if (CurrentRecipe?.Id == SelectedRecipeInfo.Id)
                    {
                        CurrentRecipe = null;
                        CurrentRecipeName = "No Recipe Loaded";
                    }
                    RefreshRecipeList();
                    SystemStatus = "Recipe deleted";
                }
            }
        }

        [RelayCommand]
        private void ExportRecipe()
        {
            if (CurrentRecipe == null)
            {
                _dialogService.ShowWarning(
                    "내보낼 레시피가 없습니다.",
                    "Warning");
                return;
            }

            var filePath = _dialogService.ShowSaveFileDialog(
                "Recipe Files (*.json)|*.json",
                ".json",
                $"{CurrentRecipe.Name}.json");

            if (filePath != null)
            {
                if (_recipeService.ExportRecipe(CurrentRecipe, filePath))
                {
                    SystemStatus = $"Recipe exported: {filePath}";
                }
                else
                {
                    _dialogService.ShowError(
                        "레시피 내보내기에 실패했습니다.",
                        "Error");
                }
            }
        }

        [RelayCommand]
        private void ImportRecipe()
        {
            var filePath = _dialogService.ShowOpenFileDialog(
                "Recipe Files (*.json)|*.json",
                ".json");

            if (filePath != null)
            {
                var recipe = _recipeService.ImportRecipe(filePath);
                if (recipe != null)
                {
                    RefreshRecipeList();
                    CurrentRecipe = recipe;
                    CurrentRecipeName = recipe.Name;
                    SystemStatus = $"Recipe imported: {recipe.Name}";
                }
                else
                {
                    _dialogService.ShowError(
                        "레시피 가져오기에 실패했습니다.",
                        "Error");
                }
            }
        }

        private void RefreshRecipeList()
        {
            RebuildRecipeList();

            // Web에서 추가된 레시피를 로컬에 동기화
            if (_parameterSyncService != null)
            {
                _ = SyncWebRecipesToLocalAsync();
            }
        }

        private void RebuildRecipeList()
        {
            RecipeList.Clear();
            foreach (var info in _recipeService.GetRecipeList())
            {
                RecipeList.Add(info);
            }
        }

        // Recipe 패널 새로고침 버튼 — 60초 폴링을 기다리지 않고 Web 레시피를 지금
        // 끌어온 뒤 목록을 재구축한다 (Web 에서 방금 만든 레시피 즉시 확인용, 2026-08-10).
        // 결과를 SystemStatus/로그로 알려 "만들었는데 안 보임"의 원인(ClientIndex 불일치,
        // 이름 연결)을 화면에서 바로 판별할 수 있게 한다.
        [RelayCommand]
        private async Task RefreshRecipesAsync()
        {
            if (_parameterSyncService == null)
            {
                RebuildRecipeList();
                SystemStatus = "Recipe list refreshed (Web 연동 미구성)";
                return;
            }

            var result = await SyncWebRecipesToLocalAsync();
            RebuildRecipeList();

            var clientIndex = _configService.LoadSystemConfiguration().ClientIndex;
            if (!result.Success)
            {
                SystemStatus = $"Web 레시피 동기화 실패 — Web 서버 연결 또는 ClientIndex({clientIndex}) 등록 확인";
                LogService?.Log(SystemStatus, LogLevel.Warning, "Recipe");
                return;
            }

            SystemStatus = result.Total == 0
                ? $"Web 레시피 0개 (ClientIndex {clientIndex}) — Web 에서 이 클라이언트용으로 만들었는지 확인"
                : $"Web 레시피 {result.Total}개 동기화 (ClientIndex {clientIndex}) — 새 항목 {result.Added}" +
                  (result.LinkedByName > 0 ? $", 이름 연결 {result.LinkedByName}" : string.Empty);
            LogService?.Log(SystemStatus, LogLevel.Info, "Recipe");
            if (result.LinkedByName > 0)
            {
                // 원인 B 가시화 — 같은 이름의 로컬 레시피가 있으면 Web 레시피는 별도 항목으로
                // 표시되지 않고 그 로컬 레시피에 파라미터가 이름 매칭으로 연결된다 (의도된 동작)
                LogService?.Log(
                    $"이름 연결 {result.LinkedByName}건: 같은 이름의 로컬 레시피가 있어 별도 항목으로 표시하지 않습니다 " +
                    "(파라미터는 해당 로컬 레시피에 이름 매칭으로 적용)", LogLevel.Info, "Recipe");
            }
        }

        // ── 외부 레시피 변경 자동 반영 (VisionSetup 저장 감지) ──

        // AUTO RUN 중에는 즉시 교체하지 않고 정지 시점에 적용 (검사 도중 파라미터 교체 방지).
        // 상태바 한 줄로는 현장에서 놓치므로 헤더 레시피 칩에 대기 배지로도 노출 (2026-08-19).
        [ObservableProperty]
        private bool _isRecipeReloadPending;

        /// <summary>
        /// 현재 레시피의 소스 파일 경로 갱신 — PLC RecipeChange 등 파일 로드 없이 레시피가
        /// 바뀌는 경로에서 호출. 갱신하지 않으면 외부 변경 워처가 이전 레시피 파일과
        /// 비교해 실행 중 레시피의 변경을 놓친다.
        /// </summary>
        public void SetCurrentRecipeFilePath(string filePath)
            => _currentRecipeFilePath = filePath;

        private void OnExternalRecipeFileChanged(string filePath)
        {
            var currentPath = _currentRecipeFilePath;
            if (string.IsNullOrEmpty(currentPath)) return;
            if (!string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(currentPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                // 파일명이 달라도 내용의 id 가 현재 레시피와 같으면 같은 레시피의 다른 파일 —
                // 과거 이름 기반 저장으로 생긴 중복(recipe_<이름>.json vs recipe_<GUID>.json)을
                // 여기서 흡수한다. 그 파일을 현재 소스로 채택해 이후 저장도 추적.
                if (!ChangedFileMatchesCurrentRecipeId(filePath)) return;
                _currentRecipeFilePath = filePath;
                LogService?.Log($"레시피 '{CurrentRecipeName}' 의 소스 파일을 '{Path.GetFileName(filePath)}' 로 전환 (id 일치)",
                    LogLevel.Info, "Recipe");
            }

            // 워처 스레드에서 호출됨 — UI 스레드로 이동
            Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                if (IsRunning)
                {
                    IsRecipeReloadPending = true;
                    SystemStatus = $"레시피 '{CurrentRecipeName}' 변경 감지 — 검사 정지 시 다시 불러옵니다";
                    LogService?.Log(SystemStatus, LogLevel.Warning, "Recipe");
                    return;
                }

                await Task.Delay(300); // 저장 직후 파일 잠금/부분 쓰기 여유
                ReloadCurrentRecipe();
            });
        }

        /// <summary>변경된 레시피 파일의 id 가 현재 로드된 레시피와 같은지 (워처 스레드에서 호출).</summary>
        private bool ChangedFileMatchesCurrentRecipeId(string filePath)
            => RecipeFileMatchesId(filePath, CurrentRecipe?.Id);

        /// <summary>레시피 JSON 파일의 id 필드가 주어진 id 와 일치하는지 (camelCase 직렬화 기준).</summary>
        internal static bool RecipeFileMatchesId(string filePath, string? currentId)
        {
            try
            {
                if (string.IsNullOrEmpty(currentId)) return false;

                using var stream = File.OpenRead(filePath);
                using var doc = System.Text.Json.JsonDocument.Parse(stream);
                return doc.RootElement.TryGetProperty("id", out var idProp)
                    && string.Equals(idProp.GetString(), currentId, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // 저장 직후 파일 잠금/부분 쓰기 등 — 판별 불가면 무시 (기존 경로 일치 동작 유지)
                return false;
            }
        }

        private void ReloadCurrentRecipe()
        {
            IsRecipeReloadPending = false;
            var path = _currentRecipeFilePath;
            if (string.IsNullOrEmpty(path)) return;

            var recipe = _recipeService.LoadRecipe(path);
            if (recipe == null)
            {
                LogService?.Log($"레시피 재로드 실패 — 파일을 읽을 수 없습니다: {path}", LogLevel.Warning, "Recipe");
                return;
            }

            CurrentRecipe = recipe;
            CurrentRecipeName = recipe.Name;
            foreach (var cam in Cameras)
                cam.SetRecipe(recipe);   // 검사 캐시도 함께 초기화

            SystemStatus = $"레시피 '{recipe.Name}' 외부 변경 반영 완료 (VisionSetup 저장 감지)";
            LogService?.Log(SystemStatus, LogLevel.Success, "Recipe");
        }

        /// <summary>Web 레시피 동기화 요약 — 새로고침 버튼이 상태 표시에 사용.</summary>
        private readonly record struct WebRecipeSyncResult(bool Success, int Total, int Added, int LinkedByName);

        private async Task<WebRecipeSyncResult> SyncWebRecipesToLocalAsync()
        {
            try
            {
                if (!await _parameterSyncService!.SyncRecipesAsync())
                    return new WebRecipeSyncResult(false, 0, 0, 0);

                var webRecipes = _parameterSyncService.Recipes;
                var localRecipes = _recipeService.GetRecipeList();
                var localNames = localRecipes.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool anyNew = false;
                int added = 0;

                foreach (var web in webRecipes)
                {
                    var webFilePath = Path.Combine(_recipeService.RecipesDirectory, $"web_{web.Id}.json");
                    if (!File.Exists(webFilePath) && !localNames.Contains(web.Name))
                    {
                        var recipe = new Recipe
                        {
                            Id = $"web_{web.Id}",
                            Name = web.Name,
                            Description = web.Description,
                            CreatedAt = DateTime.UtcNow,
                            ModifiedAt = DateTime.UtcNow
                        };
                        // 빈 stub 저장이 실행 중인 CurrentRecipe 를 교체하면 안 된다 (검사 중 유입 가능)
                        _recipeService.SaveRecipe(recipe, webFilePath, setAsCurrent: false);
                        anyNew = true;
                        added++;
                    }
                }

                // Web 파일 정리: 서버에서 삭제된 레시피 또는 로컬에 동일 이름이 있는 중복 파일 제거
                var webIds = webRecipes.Select(r => r.Id).ToHashSet();
                var webNameById = webRecipes.ToDictionary(r => r.Id, r => r.Name);
                var nonWebLocalNames = localRecipes
                    .Where(r => !r.FilePath.Contains("web_"))
                    .Select(r => r.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var webFiles = Directory.GetFiles(_recipeService.RecipesDirectory, "web_*.json");
                foreach (var file in webFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (fileName.StartsWith("web_") && int.TryParse(fileName.Substring(4), out var fileWebId))
                    {
                        // 서버에서 삭제되었거나, 로컬에 동일 이름 레시피가 이미 존재하면 삭제
                        bool deletedOnServer = !webIds.Contains(fileWebId);
                        bool duplicateName = webNameById.TryGetValue(fileWebId, out var webName)
                            && nonWebLocalNames.Contains(webName);
                        if (deletedOnServer || duplicateName)
                        {
                            File.Delete(file);
                            anyNew = true;
                        }
                    }
                }

                if (anyNew)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        RecipeList.Clear();
                        foreach (var info in _recipeService.GetRecipeList())
                        {
                            RecipeList.Add(info);
                        }
                    });
                }

                // 원인 B 가시화용 — 같은 이름의 로컬(비 web) 레시피와 연결되어 별도 표시되지 않는 수
                var linkedByName = webRecipes.Count(w => nonWebLocalNames.Contains(w.Name));
                return new WebRecipeSyncResult(true, webRecipes.Count, added, linkedByName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Web recipe sync failed: {ex.Message}");
                return new WebRecipeSyncResult(false, 0, 0, 0);
            }
        }

        [RelayCommand]
        private void LaunchVisionToolSetup()
        {
            try
            {
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var exePath = Path.Combine(currentDir, "VMS.VisionSetup.exe");

                if (File.Exists(exePath))
                {
                    // Pass current recipe file path as argument
                    string? arguments = null;
                    if (!string.IsNullOrEmpty(_currentRecipeFilePath) && File.Exists(_currentRecipeFilePath))
                    {
                        arguments = $"\"{_currentRecipeFilePath}\"";
                    }

                    _processService.LaunchProcess(exePath, arguments);
                    LogService?.Log("Vision Tool Setup launched", LogLevel.Info, "System");
                }
                else
                {
                    _dialogService.ShowWarning(
                        "VMS.VisionSetup 프로그램을 찾을 수 없습니다.\n" +
                        "프로젝트를 먼저 빌드해 주세요.",
                        "Not Found");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(
                    $"프로그램 실행 오류: {ex.Message}",
                    "Error");
            }
        }

        [RelayCommand]
        private void LaunchSetup()
        {
            try
            {
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var exePath = Path.Combine(currentDir, "VMS.AppSetup.exe");

                if (File.Exists(exePath))
                {
                    _processService.LaunchProcess(exePath);
                    LogService?.Log("System Setup launched", LogLevel.Info, "System");
                }
                else
                {
                    _dialogService.ShowWarning(
                        "BODA.Setup 프로그램을 찾을 수 없습니다.\n" +
                        "프로젝트를 먼저 빌드해 주세요.",
                        "Not Found");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(
                    $"프로그램 실행 오류: {ex.Message}",
                    "Error");
            }
        }

        #region Update Notifier (Phase B)

        /// <summary>
        /// 최근 업데이트 체크 결과. null = 아직 체크 안 함 / 통신 실패.
        /// IsUpdateAvailable / UpdateBadgeText 가 이 값을 파생.
        /// </summary>
        [ObservableProperty]
        private UpdateInfo? _latestUpdate;

        public bool IsUpdateAvailable => LatestUpdate?.IsUpdateAvailable == true;

        public string UpdateBadgeText => IsUpdateAvailable
            ? $"새 버전 {LatestUpdate!.LatestTagName}"
            : string.Empty;

        partial void OnLatestUpdateChanged(UpdateInfo? value)
        {
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(UpdateBadgeText));
        }

        /// <summary>
        /// 앱 시작 시 best-effort 호출 — 결과만 LatestUpdate 에 저장. 다이얼로그 미표시.
        /// 실패해도 silent (배지가 안 뜰 뿐).
        /// </summary>
        public async Task CheckForUpdatesSilentAsync()
        {
            if (_updateService == null) return;
            try
            {
                LatestUpdate = await _updateService.CheckAsync();
                if (IsUpdateAvailable)
                {
                    LogService?.Log(
                        $"새 버전 감지: {LatestUpdate!.LatestTagName} (현재 {LatestUpdate.CurrentVersion})",
                        LogLevel.Info, "Update");
                }
            }
            catch
            {
                // 시작 시 자동 체크 — 절대 UI 차단 금지.
            }
        }

        /// <summary>
        /// 사용자가 사이드 패널 "Check for updates" 버튼 클릭 시 호출.
        /// 결과를 다이얼로그로 명시적으로 안내(최신/새 버전/실패 모두).
        /// </summary>
        [RelayCommand]
        private async Task CheckForUpdatesAsync()
        {
            if (_updateService == null)
            {
                _dialogService.ShowInformation("업데이트 체커가 비활성화되어 있습니다.", "업데이트 확인");
                return;
            }

            var info = await _updateService.CheckAsync();
            LatestUpdate = info;

            if (info == null)
            {
                _dialogService.ShowInformation(
                    "업데이트 정보를 가져올 수 없습니다.\n네트워크 상태 또는 GitHub 접근 가능 여부를 확인해 주세요.",
                    "업데이트 확인");
                return;
            }

            if (!info.IsUpdateAvailable)
            {
                _dialogService.ShowInformation(
                    $"이미 최신 버전입니다 (v{info.CurrentVersion}).",
                    "업데이트 확인");
                return;
            }

            ShowUpdateAvailableDialog(info);
        }

        /// <summary>
        /// 사이드 패널 배지 클릭 시 호출 — 이미 보유한 LatestUpdate 로 즉시 다이얼로그.
        /// 통신 없이 표시만 하므로 빠름.
        /// </summary>
        [RelayCommand]
        private void ShowUpdateDetails()
        {
            if (LatestUpdate?.IsUpdateAvailable == true)
                ShowUpdateAvailableDialog(LatestUpdate);
        }

        private void ShowUpdateAvailableDialog(UpdateInfo info)
        {
            // release notes 가 너무 길면 잘라서 표시 — 전체는 브라우저에서.
            const int MaxNotesLength = 600;
            var notes = string.IsNullOrWhiteSpace(info.ReleaseNotes)
                ? string.Empty
                : "\n\n" + (info.ReleaseNotes.Length <= MaxNotesLength
                    ? info.ReleaseNotes
                    : info.ReleaseNotes.Substring(0, MaxNotesLength) + "...");

            var message =
                $"새 버전 {info.LatestTagName} 이 출시되었습니다.\n" +
                $"현재 버전: v{info.CurrentVersion}\n" +
                $"최신 버전: v{info.LatestVersion}" +
                notes +
                "\n\n다운로드 페이지를 열까요?";

            if (!_dialogService.ShowConfirmation(message, "업데이트 가능"))
                return;

            try
            {
                _processService.LaunchProcess(info.ReleaseUrl);
                LogService?.Log($"업데이트 페이지 열림: {info.ReleaseUrl}", LogLevel.Info, "Update");
            }
            catch (Exception ex)
            {
                LogService?.Log($"업데이트 페이지 열기 실패: {ex.Message}", LogLevel.Error, "Update");
                _dialogService.ShowError(
                    $"브라우저로 페이지를 여는 데 실패했습니다.\n수동 접속 URL:\n{info.ReleaseUrl}",
                    "오류");
            }
        }

        #endregion

        // 전체 카메라 일괄 Grab 커맨드는 삭제 — 카메라 단위 Grab 은 각 카메라 카드의
        // 설정 창(컨트롤 박스)에서 수행한다 (Camera Control 그룹 제거).

        [RelayCommand]
        private async Task StartLiveAsync()
        {
            if (IsLiveMode || IsRunning) return;

            IsLiveMode = true;
            SystemStatus = "Live Starting...";
            LogService?.Log("Live grab starting...", LogLevel.Info, "System");

            foreach (var cam in Cameras.Where(c => c.IsEnabled))
            {
                await cam.StartLiveGrabAsync();
            }

            SystemStatus = "Live";
            LogService?.Log("Live grab started", LogLevel.Success, "System");
        }

        [RelayCommand]
        private async Task StopLiveAsync()
        {
            if (!IsLiveMode) return;

            SystemStatus = "Live Stopping...";
            LogService?.Log("Live grab stopping...", LogLevel.Info, "System");

            foreach (var cam in Cameras)
            {
                await cam.StopLiveGrabAsync();
            }

            IsLiveMode = false;
            SystemStatus = "Ready";
            LogService?.Log("Live grab stopped", LogLevel.Info, "System");
        }

        [RelayCommand]
        private async Task ToggleRollerInspectionAsync()
        {
            if (_rollerInspectionService == null) return;

            if (IsRollerInspecting)
            {
                // 중지
                _rollerInspectionService.Stop();

                // 카메라 이벤트 해제 및 Live 표시 복원
                foreach (var cam in Cameras)
                {
                    cam.LiveFrameReady -= _rollerInspectionService.ProcessFrame;
                    cam.SuppressLiveDisplay = false;
                }

                // Live 모드도 중지
                if (IsLiveMode)
                    await StopLiveAsync();

                IsRollerInspecting = false;
                SystemStatus = "Ready";
                LogService?.Log("Roller inspection stopped", LogLevel.Info, "Roller");
            }
            else
            {
                // 시작: Live 모드를 먼저 활성화
                if (!IsLiveMode)
                    await StartLiveAsync();

                // 카메라 이벤트 연결 및 Live 표시 억제
                foreach (var cam in Cameras.Where(c => c.IsEnabled))
                {
                    cam.SuppressLiveDisplay = true;
                    cam.LiveFrameReady += _rollerInspectionService.ProcessFrame;
                }

                _rollerInspectionService.Start();
                IsRollerInspecting = true;
                SystemStatus = "Roller Inspecting";
                LogService?.Log("Roller inspection started", LogLevel.Success, "Roller");
            }
        }

        [RelayCommand]
        private async Task StartInspectionAsync()
        {
            if (_autoProcessService != null && _autoProcessService.IsRunning)
                return; // 이전 Stop이 아직 진행 중

            // Live 모드 실행 중이면 자동 중지 (상호 배타적)
            if (IsLiveMode)
            {
                await StopLiveAsync();
            }

            IsRunning = true;
            SystemStatus = "Starting...";
            LogService?.Log("Inspection starting...", LogLevel.Info, "System");
            RefreshIoBoardStatus();   // 운전 직전 보드 상태를 헤더에 반영

            if (_autoProcessService != null)
            {
                try
                {
                    // PLC 연결/모니터링 등 I/O를 백그라운드 스레드에서 실행하여 UI 블로킹 방지
                    await Task.Run(() => _autoProcessService.StartAsync());
                    SystemStatus = "AutoProcess Running";
                    LogService?.Log("Inspection started", LogLevel.Success, "System");
                }
                catch (Exception ex)
                {
                    SystemStatus = $"AutoProcess Error: {ex.Message}";
                    LogService?.Log($"AutoProcess start error: {ex.Message}", LogLevel.Error, "System");
                    IsRunning = false;
                }
            }
        }

        [RelayCommand]
        private async Task StopInspectionAsync()
        {
            // 즉시 UI 반영 — Stop 버튼 숨김, Start 버튼 표시
            IsRunning = false;
            SystemStatus = "Stopping...";
            LogService?.Log("Inspection stopping...", LogLevel.Info, "System");

            if (_autoProcessService != null && _autoProcessService.IsRunning)
            {
                try
                {
                    // PLC 신호 클리어/해제 등 I/O를 백그라운드 스레드에서 실행
                    await Task.Run(() => _autoProcessService.StopAsync());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping AutoProcess: {ex.Message}");
                }
            }

            SystemStatus = "Stopped";
            LogService?.Log("Inspection stopped", LogLevel.Info, "System");

            // AUTO RUN 중 감지된 레시피 외부 변경을 안전한 시점(정지 후)에 반영
            if (IsRecipeReloadPending)
                ReloadCurrentRecipe();
        }

        [RelayCommand]
        private void OpenUserManagement()
        {
            if (_userService == null) return;

            var vm = new UserManagementViewModel(_userService, _dialogService);
            var window = new UserManagementWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        /// <summary>
        /// 감사 로그 조회 윈도우 — Admin 권한 전용. CanManageUsers 와 동일한 가시성 조건.
        /// GS 인증 이력 추적성 항목의 실제 시연 화면.
        /// </summary>
        [RelayCommand]
        private void OpenAuditLogViewer()
        {
            var vm = new AuditLogViewerViewModel();
            var window = new AuditLogViewerWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        /// <summary>
        /// 시작 헬스 체크 윈도우 — Admin 권한 전용 (Audit Viewer 와 동일).
        /// 5개 환경 검사 결과 + Refresh / Copy 액션. 실행 자체가 System 감사 이벤트.
        /// </summary>
        [RelayCommand]
        private void OpenHealthCheck()
        {
            var vm = new HealthCheckViewModel();
            var window = new HealthCheckWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        /// <summary>
        /// 백업 / 복원 윈도우 — Admin 권한 전용. system_config / users DB / recipes 의
        /// ZIP 백업 + manifest 검증 복원. 모든 작업 Configuration 감사 이벤트.
        /// </summary>
        [RelayCommand]
        private void OpenBackupRestore()
        {
            var vm = new BackupRestoreViewModel();
            var window = new BackupRestoreWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        /// <summary>
        /// 자동 백업 설정 윈도우 — Admin 권한 전용. system_config.json 의 autoBackup
        /// 객체만 격리 편집 — 다른 키는 JsonNode 로 보존. VMS 재시작 후 적용.
        /// </summary>
        [RelayCommand]
        private void OpenAutoBackupSettings()
        {
            var vm = new AutoBackupSettingsViewModel();
            var window = new AutoBackupSettingsWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        /// <summary>
        /// 지원 패키지 export 윈도우 — Admin 권한 전용. 원격 지원 / 엔지니어링 분석용
        /// ZIP 생성. 백업과 달리 BodaVision.db / recipes 는 미포함.
        /// </summary>
        [RelayCommand]
        private void OpenSupportPackage()
        {
            var vm = new SupportPackageViewModel();
            var window = new SupportPackageWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        /// <summary>
        /// 보존 정책 통합 설정 — Admin 권한 전용. 3개 보존 키 (audit / autoBackup /
        /// uploadQueue) 를 한 화면에서 편집. 다른 system_config.json 키는 보존.
        /// </summary>
        [RelayCommand]
        private void OpenRetentionSettings()
        {
            var vm = new RetentionSettingsViewModel();
            var window = new RetentionSettingsWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        /// <summary>
        /// 검사 판정 이미지 저장 설정 윈도우. system_config.json 의 imageSave 객체만
        /// 격리 편집 — 다른 키는 JsonNode 로 보존. 윈도우 종료 후 옵션을 즉시 재로드해
        /// 재시작 없이도 다음 검사부터 적용된다.
        /// </summary>
        [RelayCommand]
        private void OpenImageSaveSettings()
        {
            var vm = new ImageSaveSettingsViewModel();
            var window = new ImageSaveSettingsWindow
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();

            // 저장된 설정을 즉시 반영 — 다음 검사 판정부터 새 옵션 적용.
            // (이미지 보존 삭제는 인프로세스가 아니라 Windows 예약 작업이 담당 — 디자인 §7)
            _imageSaveOptions = ImageSaveOptions.LoadFromAppData();
        }

        [RelayCommand]
        private void SwitchUser()
        {
            if (_userService == null) return;

            _userService.Logout();
            if (_dialogService.ShowLoginDialog(_userService))
            {
                UpdateUserDisplay();
                LogService?.Log($"User switched to: {_userService.CurrentUser?.DisplayName}", LogLevel.Success, "Auth");
            }
            else
            {
                // Login cancelled — clear display
                UpdateUserDisplay();
            }
        }

        [RelayCommand]
        private void ClearLog()
        {
            LogService?.Clear();
        }

        [RelayCommand]
        private void ResetDashboard()
        {
            Dashboard.ResetStatisticsCommand.Execute(null);
            TotalInspections = 0;
            TotalPass = 0;
            TotalFail = 0;
        }

        [RelayCommand]
        private void OpenSyncParameters()
        {
            if (_parameterSyncService == null)
            {
                _dialogService.ShowWarning(
                    "Parameter Sync Service is not available.\nCheck WebServerUrl configuration in AppSetup.",
                    "Sync Parameters");
                return;
            }

            var dialog = new ParameterSyncDialog(_parameterSyncService);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        [RelayCommand]
        private void ExitApplication()
        {
            if (_dialogService.ShowConfirmation(
                "프로그램을 종료하시겠습니까?",
                "Exit"))
            {
                _shutdownAction();
            }
        }

        partial void OnSelectedNgImageChanged(NgImageItem? value)
        {
            if (value?.Thumbnail != null)
            {
                // Show the NG image in the selected camera if one is available
                if (SelectedCamera != null)
                {
                    SelectedCamera.CurrentImage = value.Thumbnail;
                }
            }
        }

        partial void OnTotalPassChanged(int value)
        {
            OnPropertyChanged(nameof(PassRate));
        }

        partial void OnTotalInspectionsChanged(int value)
        {
            OnPropertyChanged(nameof(PassRate));
        }

        private void OnWebConnectionStatusChanged(bool connected)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IsWebConnected = connected;
            });
        }

        #region Stage 1 — 작업자 로그인 + Phase 3 추적성 컨텍스트

        // 작업자 로그인 상태
        [ObservableProperty] private string _currentOperatorName = "";
        [ObservableProperty] private string _currentOperatorEmployeeNumber = "";
        [ObservableProperty] private string _currentOperatorRole = "Operator";  // D10
        [ObservableProperty] private bool _isOperatorLoggedIn;

        // D10 — Role 기반 메뉴 가시성. Web 통합 환경에서만 의미. Standalone 은 항상 true (제약 없음).
        // Lead = 반장 (Recipe 편집, Camera Control). Supervisor = + 외부 도구 (Vision Tool / System Setup) + 사용자 관리.
        // VMS 시스템 Admin (IUserService) 은 Web Operator 역할과 무관하게 통과 — 보안 모델상 최상위 우회 권한.
        public bool CanLeadOrAbove =>
            _operatorAuthService == null
            || _userService?.CurrentUser?.Grade == UserGrade.Admin
            || (IsOperatorLoggedIn && (CurrentOperatorRole == VMS.Core.Models.ParameterSync.OperatorRoles.Lead
                                       || CurrentOperatorRole == VMS.Core.Models.ParameterSync.OperatorRoles.Supervisor));
        public bool CanSupervisor =>
            _operatorAuthService == null
            || _userService?.CurrentUser?.Grade == UserGrade.Admin
            || (IsOperatorLoggedIn && CurrentOperatorRole == VMS.Core.Models.ParameterSync.OperatorRoles.Supervisor);

        partial void OnCurrentOperatorRoleChanged(string value)
        {
            OnPropertyChanged(nameof(CanLeadOrAbove));
            OnPropertyChanged(nameof(CanSupervisor));
        }

        // 검사 결과 업로드 시 자동 첨부될 추적성 컨텍스트 (Phase 3)
        // TextBox 호환을 위해 string. int.TryParse → ParameterSyncService 에 propagate.
        [ObservableProperty] private string _workOrderIdText = "";
        [ObservableProperty] private string _lotIdText = "";
        [ObservableProperty] private string _operatorIdText = "";
        [ObservableProperty] private string _serialNumberText = "";

        partial void OnWorkOrderIdTextChanged(string value)
        {
            if (_parameterSyncService != null)
                _parameterSyncService.WorkOrderId = int.TryParse(value, out var v) ? v : null;
        }
        partial void OnLotIdTextChanged(string value)
        {
            if (_parameterSyncService != null)
                _parameterSyncService.LotId = int.TryParse(value, out var v) ? v : null;
        }
        partial void OnOperatorIdTextChanged(string value)
        {
            if (_parameterSyncService != null)
                _parameterSyncService.OperatorId = int.TryParse(value, out var v) ? v : null;
        }
        partial void OnSerialNumberTextChanged(string value)
        {
            if (_parameterSyncService != null)
                _parameterSyncService.SerialNumber = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        partial void OnIsOperatorLoggedInChanged(bool value)
        {
            OpenWorkOrderListCommand.NotifyCanExecuteChanged();
            LoginOperatorCommand.NotifyCanExecuteChanged();
            LogoutOperatorCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanStartStop));
            OnPropertyChanged(nameof(CanOperateCamera));
            OnPropertyChanged(nameof(CanGrabCamera));
            OnPropertyChanged(nameof(CameraOperateBlockReason));
            OnPropertyChanged(nameof(CanToggleRollerInspection));
            OnPropertyChanged(nameof(CanLeadOrAbove));
            OnPropertyChanged(nameof(CanSupervisor));
        }

        private void OnOperatorSessionChanged(VMS.Core.Models.ParameterSync.OperatorSessionDto? session)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (session != null && session.IsActive)
                {
                    CurrentOperatorName = session.OperatorName;
                    CurrentOperatorEmployeeNumber = session.EmployeeNumber;
                    CurrentOperatorRole = string.IsNullOrEmpty(session.Role) ? "Operator" : session.Role;
                    IsOperatorLoggedIn = true;
                    OperatorIdText = session.OperatorId.ToString();
                }
                else
                {
                    CurrentOperatorName = "";
                    CurrentOperatorEmployeeNumber = "";
                    CurrentOperatorRole = "Operator";
                    IsOperatorLoggedIn = false;
                    OperatorIdText = "";
                }
            });
        }

        [RelayCommand]
        private void ClearInspectionContext()
        {
            WorkOrderIdText = "";
            LotIdText = "";
            OperatorIdText = "";
            SerialNumberText = "";
        }

        [RelayCommand(CanExecute = nameof(CanLoginOperator))]
        private void LoginOperator()
        {
            if (_operatorAuthService == null) return;
            var dlg = new VMS.VisionSetup.Views.OperatorLoginDialog(_operatorAuthService)
            {
                Owner = Application.Current?.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current?.MainWindow
            };
            dlg.ShowDialog();
        }
        private bool CanLoginOperator() => _operatorAuthService != null && !IsOperatorLoggedIn;

        [RelayCommand(CanExecute = nameof(CanLogoutOperator))]
        private async System.Threading.Tasks.Task LogoutOperatorAsync()
        {
            if (_operatorAuthService == null) return;
            await _operatorAuthService.LogoutAsync();
        }
        private bool CanLogoutOperator() => _operatorAuthService != null && IsOperatorLoggedIn;

        // Stage 2: 선택된 작업지시 (UI 표시 + ApplyContext)
        [ObservableProperty] private VMS.Core.Models.ParameterSync.WorkOrderDto? _selectedWorkOrder;
        public string SelectedWorkOrderText => SelectedWorkOrder == null
            ? ""
            : $"{SelectedWorkOrder.OrderNo} · {SelectedWorkOrder.ProductName} ({SelectedWorkOrder.ProgressText})";

        /// <summary>칩 툴팁 — 혼합 레시피 WO 는 라인별 진행을 줄바꿈으로 병기.</summary>
        public string SelectedWorkOrderToolTip
        {
            get
            {
                if (SelectedWorkOrder == null) return "";
                var summary = SelectedWorkOrder.ItemsSummaryText;
                return string.IsNullOrEmpty(summary)
                    ? SelectedWorkOrderText
                    : $"{SelectedWorkOrderText}\n{summary}";
            }
        }

        // B4: 헤더 WO 칩의 ProgressBar 시각화용 — 0~100 percent.
        // 완료 기준(CompletionBasis)에 따라 양품/총생산 진행 수량이 반영된다.
        public double SelectedWorkOrderProgressPercent =>
            SelectedWorkOrder?.PlannedQuantity > 0
                ? System.Math.Min(100.0, (double)SelectedWorkOrder.ProgressQuantity / SelectedWorkOrder.PlannedQuantity * 100.0)
                : 0;
        public bool HasSelectedWorkOrderProgress =>
            SelectedWorkOrder != null && SelectedWorkOrder.PlannedQuantity > 0;

        // D8: 최근 검사 히스토리 서비스 — XAML 바인딩용
        public VMS.Core.Services.RecentInspectionsService RecentInspections =>
            VMS.Core.Services.RecentInspectionsService.Instance;

        [RelayCommand]
        private void ClearRecentInspections()
        {
            RecentInspections.Clear();
            OnPropertyChanged(nameof(RecentInspections));
        }
        partial void OnSelectedWorkOrderChanged(VMS.Core.Models.ParameterSync.WorkOrderDto? value)
        {
            OnPropertyChanged(nameof(SelectedWorkOrderText));
            OnPropertyChanged(nameof(SelectedWorkOrderToolTip));
            OnPropertyChanged(nameof(CanStartStop));
            OnPropertyChanged(nameof(CanToggleRollerInspection));
            OnPropertyChanged(nameof(SelectedWorkOrderProgressPercent));
            OnPropertyChanged(nameof(HasSelectedWorkOrderProgress));

            // C5: WO 가 바뀌면 완료 알림 가드 리셋 (새 WO 가 완료될 때 다시 알림 가능하도록).
            if (value?.Id != _lastCompletedNotifiedWoId)
                _lastCompletedNotifiedWoId = null;
            if (value != null)
            {
                // 컨텍스트 자동 채움 — Phase 3 추적성 필드
                WorkOrderIdText = value.Id.ToString();
                LotIdText = ""; // 활성 Lot 비동기 조회 결과 대기 — 그 동안은 비움

                // WO 의 RecipeName 으로 레시피 자동 로드 (사이드 패널 로그인 우회)
                _ = LoadRecipeFromWorkOrderAsync(value);

                // B1: WO 의 활성 Lot 자동 채움
                _ = LoadActiveLotForWorkOrderAsync(value.Id);

                AuditLogger.Instance.Log(
                    AuditCategory.System, "WorkOrderSelected", AuditOutcome.Success,
                    userName: _userService?.CurrentUser?.Username,
                    source: nameof(MainViewModel),
                    details: $"Id={value.Id}, OrderNo={value.OrderNo}, Recipe={value.RecipeName}, Status={value.Status}");
            }
            else
            {
                LotIdText = "";
                AuditLogger.Instance.Log(
                    AuditCategory.System, "WorkOrderCleared", AuditOutcome.Success,
                    userName: _userService?.CurrentUser?.Username,
                    source: nameof(MainViewModel));
            }
        }

        /// <summary>B1 — Web 에서 WO 의 활성(Open) Lot 1개를 가져와 LotIdText 채움. 없으면 비워둠.</summary>
        private async Task LoadActiveLotForWorkOrderAsync(int workOrderId)
        {
            if (_lotClient == null) return;
            try
            {
                var lot = await _lotClient.GetActiveByWorkOrderAsync(workOrderId);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 사용자가 그 사이 다른 WO 로 바꿨으면 무시
                    if (SelectedWorkOrder == null || SelectedWorkOrder.Id != workOrderId) return;

                    if (lot != null)
                    {
                        LotIdText = lot.Id.ToString();
                        LogService?.Log($"WO {SelectedWorkOrder.OrderNo} → 활성 Lot '{lot.LotNumber}' 자동 채움", LogLevel.Info, "WorkOrder");
                    }
                    else
                    {
                        LogService?.Log($"WO {SelectedWorkOrder.OrderNo} 활성 Lot 없음 — LotIdText 비움", LogLevel.Info, "WorkOrder");
                    }
                });
            }
            catch (Exception ex)
            {
                LogService?.Log($"활성 Lot 조회 실패: {ex.Message}", LogLevel.Warning, "WorkOrder");
            }
        }

        /// <summary>
        /// 선택된 WO 의 RecipeName 으로 로컬 레시피를 찾아 자동 로드.
        /// 로컬에 없으면 Web 레시피를 동기화 후 재시도. Web 파라미터도 매칭되면 함께 로드.
        /// 사이드 패널의 별도 사용자 로그인을 거치지 않는다 — Operator 로그인 만으로 충분.
        /// </summary>
        private async Task LoadRecipeFromWorkOrderAsync(VMS.Core.Models.ParameterSync.WorkOrderDto wo)
        {
            if (string.IsNullOrWhiteSpace(wo.RecipeName)) return;

            try
            {
                var info = _recipeService.GetRecipeList()
                    .FirstOrDefault(r => string.Equals(r.Name, wo.RecipeName, StringComparison.OrdinalIgnoreCase));

                // 로컬에 없으면 Web 동기화 후 재시도
                if (info == null && _parameterSyncService != null)
                {
                    await SyncWebRecipesToLocalAsync();
                    info = _recipeService.GetRecipeList()
                        .FirstOrDefault(r => string.Equals(r.Name, wo.RecipeName, StringComparison.OrdinalIgnoreCase));
                }

                if (info == null)
                {
                    SystemStatus = $"Recipe '{wo.RecipeName}' not found (WO {wo.OrderNo})";
                    LogService?.Log($"WO {wo.OrderNo}: recipe '{wo.RecipeName}' not found locally or on Web", LogLevel.Warning, "WorkOrder");
                    return;
                }

                if (CurrentRecipe?.Id == info.Id) return; // 이미 로드됨 — 스킵

                var recipe = _recipeService.LoadRecipe(info.FilePath);
                if (recipe == null)
                {
                    SystemStatus = $"Failed to load recipe '{info.Name}'";
                    return;
                }

                CurrentRecipe = recipe;
                CurrentRecipeName = recipe.Name;
                _currentRecipeFilePath = info.FilePath;
                foreach (var cam in Cameras)
                    cam.SetRecipe(recipe);

                SystemStatus = $"WO {wo.OrderNo} → Recipe '{recipe.Name}' loaded";
                LogService?.Log($"WO {wo.OrderNo} auto-loaded recipe '{recipe.Name}'", LogLevel.Success, "WorkOrder");

                // Web 파라미터 동기화 — 이름 매칭
                if (_parameterSyncService != null)
                {
                    var webRecipe = _parameterSyncService.Recipes
                        .FirstOrDefault(r => string.Equals(r.Name, wo.RecipeName, StringComparison.OrdinalIgnoreCase));
                    if (webRecipe != null)
                        await _parameterSyncService.LoadRecipeAsync(webRecipe.Id);
                }
            }
            catch (Exception ex)
            {
                LogService?.Log($"WO recipe auto-load failed: {ex.Message}", LogLevel.Error, "WorkOrder");
            }
        }

        // Stage 3: 검사 결과 업로드 응답에서 WO 진행률 갱신
        private void OnWorkOrderProgressed(VMS.Core.Models.ParameterSync.WorkOrderProgressDto progress)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (SelectedWorkOrder == null || SelectedWorkOrder.Id != progress.Id) return;

                SelectedWorkOrder.ProducedQuantity = progress.ProducedQuantity;
                SelectedWorkOrder.PassQuantity = progress.PassQuantity;
                SelectedWorkOrder.NgQuantity = progress.NgQuantity;
                SelectedWorkOrder.Status = progress.Status;
                if (!string.IsNullOrEmpty(progress.CompletionBasis))
                    SelectedWorkOrder.CompletionBasis = progress.CompletionBasis;

                // 혼합 레시피 WO — 라인 스냅샷 반영 (기존 RecipeName 은 목록 API 것 유지)
                if (progress.Items.Count > 0)
                {
                    var names = SelectedWorkOrder.Items
                        .Where(i => !string.IsNullOrEmpty(i.RecipeName))
                        .ToDictionary(i => i.RecipeId, i => i.RecipeName);
                    foreach (var item in progress.Items)
                        if (item.RecipeName == null && names.TryGetValue(item.RecipeId, out var n))
                            item.RecipeName = n;
                    SelectedWorkOrder.Items = progress.Items;
                }

                // WO 에 없는 레시피로 검사됨 — 수량 미집계 (조용히 넘어가면 현장에서
                // "왜 수량이 안 오르지" 가 된다. 시스템 로그로 원인을 표면화.)
                if (progress.UnmatchedRecipe)
                {
                    LogService?.Log(
                        $"작업지시 {progress.OrderNo} 에 등록되지 않은 레시피로 검사되어 " +
                        "수량이 집계되지 않았습니다 — 작업지시의 레시피 라인을 확인하세요.",
                        LogLevel.Warning, "WorkOrder");
                }

                // 이미 완료/마감된 WO 로 업로드됨 (이번 검사는 수량 미집계) — Web 수동 완료를
                // SignalR 로 못 받은 경우의 폴백. 완료 흐름(운전 정지 + 다이얼로그 + 선택 해제)을
                // 그대로 태운다 — _lastCompletedNotifiedWoId 가드가 중복을 막으므로 SignalR 로
                // 이미 처리됐으면 no-op. progress.Completed(이번 업로드로 막 완료 = 정상 집계)는
                // ParameterSyncService 가 별도 WorkOrderCompleted 이벤트로 처리하므로 제외.
                var stale = progress.StaleWorkOrder
                    || (!progress.Completed && IsFinishedWorkOrderStatus(progress.Status));
                if (stale)
                {
                    LogService?.Log(
                        $"작업지시 {progress.OrderNo} 는 이미 완료/마감 상태입니다 — " +
                        "이후 검사 수량은 집계되지 않습니다 (Web 에서 완료 처리됨).",
                        LogLevel.Warning, "WorkOrder");
                    OnWorkOrderCompletedFromServer(progress);
                }

                // DTO 필드 변경은 INPC 를 발생시키지 않으므로 수동 알림.
                // CanStartStop 은 Status 도 보므로 함께 갱신.
                OnPropertyChanged(nameof(SelectedWorkOrderText));
                OnPropertyChanged(nameof(SelectedWorkOrderToolTip));
                OnPropertyChanged(nameof(CanStartStop));
                OnPropertyChanged(nameof(CanToggleRollerInspection));
                OnPropertyChanged(nameof(SelectedWorkOrderProgressPercent));
            });
        }

        /// <summary>WO 가 더 이상 수량을 받지 않는 종결 상태인지 (Completed/Closed).</summary>
        internal static bool IsFinishedWorkOrderStatus(string? status)
            => status is "Completed" or "Closed";

        // Stage 3 / B2 / C5: WO 완료 — 알람 + AUTO RUN 자동 정지 + 다음 WO 선택 흐름.
        // 계획 수량 도달(자동)과 Web 수동 완료 모두 이 경로로 들어온다.
        // 응답 파싱과 SignalR 둘 다에서 같은 이벤트가 올 수 있으므로 _lastCompletedNotifiedWoId 가드.
        private void OnWorkOrderCompletedFromServer(VMS.Core.Models.ParameterSync.WorkOrderProgressDto progress)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 같은 WO 에 대해 이미 알림을 띄웠으면 skip (응답 + SignalR 중복 차단)
                if (_lastCompletedNotifiedWoId == progress.Id) return;
                _lastCompletedNotifiedWoId = progress.Id;

                // 계획 수량 도달(자동) 외에 Web 수동 완료도 이 경로로 온다 — 중립 표현.
                LogService?.Log(
                    $"WO {progress.OrderNo} 완료 ({progress.ProducedQuantity}/{progress.PlannedQuantity}, Pass {progress.PassQuantity} / NG {progress.NgQuantity})",
                    LogLevel.Success, "WorkOrder");

                AuditLogger.Instance.Log(
                    AuditCategory.System, "WorkOrderCompleted", AuditOutcome.Success,
                    userName: _userService?.CurrentUser?.Username,
                    source: nameof(MainViewModel),
                    details: $"Id={progress.Id}, OrderNo={progress.OrderNo}, Produced={progress.ProducedQuantity}/{progress.PlannedQuantity}, Pass={progress.PassQuantity}, NG={progress.NgQuantity}");

                SystemStatus = $"✓ WO {progress.OrderNo} 완료 — {progress.ProducedQuantity}/{progress.PlannedQuantity}";

                // AUTO RUN 자동 정지 (실행 중일 때만)
                bool wasRunning = IsRunning;
                if (wasRunning)
                {
                    _ = StopInspectionAsync();
                }

                // B3: 큰 다이얼로그 + 사운드 + KPI 카드. 결과 = "다음 작업지시 선택" / "닫기".
                var dlg = new VMS.VisionSetup.Views.WorkOrderCompletedDialog(progress)
                {
                    Owner = Application.Current?.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive)
                           ?? Application.Current?.MainWindow
                };
                dlg.ShowDialog();
                bool pickNext = dlg.PickNext;

                // C6: 큐 retry 결과로 stale 한 WO 가 완료될 수 있음 — 현재 선택과 일치할 때만 unselect.
                if (SelectedWorkOrder?.Id == progress.Id)
                {
                    SelectedWorkOrder = null;
                }

                if (pickNext && OpenWorkOrderListCommand.CanExecute(null))
                {
                    OpenWorkOrderListCommand.Execute(null);
                }
            });
        }

        [RelayCommand(CanExecute = nameof(CanOpenWorkOrderList))]
        private void OpenWorkOrderList()
        {
            if (_workOrderClient == null)
            {
                _dialogService.ShowWarning("WorkOrder 클라이언트가 초기화되지 않았습니다.", "Work Orders");
                return;
            }
            var dlg = new VMS.VisionSetup.Views.WorkOrderListWindow(_workOrderClient)
            {
                Owner = Application.Current?.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current?.MainWindow
            };
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                SelectedWorkOrder = dlg.Result;
            }
        }
        private bool CanOpenWorkOrderList() => IsOperatorLoggedIn && _workOrderClient != null;

        #endregion
    }
}
