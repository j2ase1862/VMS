using VMS.Camera.Models;
using VMS.Camera.Services;
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
using System.Windows.Media.Imaging;

namespace VMS.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "BODA Vision System";

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
        public bool CanStartStop => HasConnectedCamera && (_userService?.HasPermission(UserPermission.StartStop) ?? true);

        // ── Equipment status (StatusBar) ──
        public string PlcVendorName { get; }
        public string PlcIpAddress { get; }
        public bool IsPlcConfigured => PlcVendorName != "None";
        public bool IsPlcConnected => _plcConnection?.IsConnected ?? false;

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
        private readonly Action _shutdownAction;
        private SystemConfiguration _systemConfig;

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
            string plcIpAddress = "")
        {
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
            _shutdownAction = shutdownAction;
            _systemConfig = new SystemConfiguration();

            // Subscribe to PLC connection state changes
            if (_plcConnection != null)
            {
                _plcConnection.ConnectionStateChanged += (_, _) =>
                    OnPropertyChanged(nameof(IsPlcConnected));
            }

            // Initialize user display
            UpdateUserDisplay();

            LoadConfiguration();
            RefreshRecipeList();

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

            // Apply saved layout if exists
            var layoutConfig = _configService.LoadLayoutConfiguration();
            foreach (var layout in layoutConfig.CameraLayouts)
            {
                var camera = Cameras.FirstOrDefault(c => c.Id == layout.CameraId);
                camera?.ApplyLayout(layout);
            }

            // If no cameras configured, add default virtual cameras
            if (Cameras.Count == 0)
            {
                AddDefaultCameras();
            }

            // Update canvas size based on loaded camera positions
            UpdateCanvasSize();
        }

        private void WireCameraEvents(CameraViewModel cam)
        {
            cam.FrameAcquired += result => _sharedFrameWriter?.WriteFrame(result);

            cam.InspectionCompleted += (cameraName, ok, image) =>
            {
                TotalInspections++;
                if (ok) TotalPass++;
                else TotalFail++;

                Dashboard.RecordInspectionResult(ok, cameraName, image);
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
                    Id = Guid.NewGuid().ToString(),
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
            RecipeList.Clear();
            foreach (var info in _recipeService.GetRecipeList())
            {
                RecipeList.Add(info);
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

        [RelayCommand]
        private async Task GrabAsync()
        {
            if (IsRunning || IsLiveMode) return;

            SystemStatus = "Grabbing...";
            foreach (var cam in Cameras.Where(c => c.IsEnabled))
            {
                await cam.GrabCommand.ExecuteAsync(null);
            }
            SystemStatus = "Ready";
        }

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
    }
}
