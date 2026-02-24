using VMS.Camera.Models;
using VMS.Interfaces;
using VMS.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VMS.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "BODA Vision System";

        [ObservableProperty]
        private bool _isSidePanelOpen;

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

        private readonly IConfigurationService _configService;
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly IProcessService _processService;
        private readonly IInspectionService _inspectionService;
        private readonly IAutoProcessService? _autoProcessService;
        private readonly Action _shutdownAction;
        private SystemConfiguration _systemConfig;

        public MainViewModel(
            IConfigurationService configService,
            IRecipeService recipeService,
            IDialogService dialogService,
            IProcessService processService,
            IInspectionService inspectionService,
            Action shutdownAction,
            IAutoProcessService? autoProcessService = null)
        {
            _configService = configService;
            _recipeService = recipeService;
            _dialogService = dialogService;
            _processService = processService;
            _inspectionService = inspectionService;
            _autoProcessService = autoProcessService;
            _shutdownAction = shutdownAction;
            _systemConfig = new SystemConfiguration();
            LoadConfiguration();
            RefreshRecipeList();
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
        private async Task StartInspectionAsync()
        {
            if (CurrentRecipe == null)
            {
                _dialogService.ShowWarning(
                    "레시피를 먼저 로드해 주세요.",
                    "Warning");
                return;
            }

            IsRunning = true;
            SystemStatus = "Running...";

            if (_autoProcessService != null)
            {
                try
                {
                    await _autoProcessService.StartAsync();
                    SystemStatus = "AutoProcess Running";
                }
                catch (Exception ex)
                {
                    SystemStatus = $"AutoProcess Error: {ex.Message}";
                    IsRunning = false;
                }
            }
        }

        [RelayCommand]
        private async Task StopInspectionAsync()
        {
            if (_autoProcessService != null && _autoProcessService.IsRunning)
            {
                try
                {
                    await _autoProcessService.StopAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping AutoProcess: {ex.Message}");
                }
            }

            IsRunning = false;
            SystemStatus = "Stopped";
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
