using VMS.Models;
using VMS.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

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

        private readonly ConfigurationService _configService;
        private readonly RecipeService _recipeService;
        private SystemConfiguration _systemConfig;

        public MainViewModel()
        {
            _configService = ConfigurationService.Instance;
            _recipeService = RecipeService.Instance;
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
                var camVm = CameraViewModel.FromConfiguration(camConfig);
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
                Cameras.Add(new CameraViewModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"Camera {i}",
                    IpAddress = $"192.168.0.{100 + i}",
                    Manufacturer = CameraManufacturer.Virtual,
                    X = 10 + (i - 1) * 420,
                    Y = 10,
                    Width = 400,
                    Height = 300
                });
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
                MessageBox.Show(
                    "레이아웃이 저장되었습니다.",
                    "Save Layout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "레이아웃 저장에 실패했습니다.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            }
            else
            {
                MessageBox.Show(
                    "레시피를 불러올 수 없습니다.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
                MessageBox.Show(
                    "저장할 레시피가 없습니다.",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_recipeService.SaveRecipe(CurrentRecipe))
            {
                RefreshRecipeList();
                SystemStatus = $"Recipe saved: {CurrentRecipe.Name}";
            }
            else
            {
                MessageBox.Show(
                    "레시피 저장에 실패했습니다.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void DeleteRecipe()
        {
            if (SelectedRecipeInfo == null) return;

            var result = MessageBox.Show(
                $"레시피 '{SelectedRecipeInfo.Name}'을(를) 삭제하시겠습니까?",
                "Delete Recipe",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
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
                MessageBox.Show(
                    "내보낼 레시피가 없습니다.",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Recipe Files (*.json)|*.json",
                FileName = $"{CurrentRecipe.Name}.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                if (_recipeService.ExportRecipe(CurrentRecipe, dialog.FileName))
                {
                    SystemStatus = $"Recipe exported: {dialog.FileName}";
                }
                else
                {
                    MessageBox.Show(
                        "레시피 내보내기에 실패했습니다.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void ImportRecipe()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Recipe Files (*.json)|*.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                var recipe = _recipeService.ImportRecipe(dialog.FileName);
                if (recipe != null)
                {
                    RefreshRecipeList();
                    CurrentRecipe = recipe;
                    CurrentRecipeName = recipe.Name;
                    SystemStatus = $"Recipe imported: {recipe.Name}";
                }
                else
                {
                    MessageBox.Show(
                        "레시피 가져오기에 실패했습니다.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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
                string? foundPath = null;

                // 1. Check path file written by VMS.VisionSetup build
                var pathFile = Path.Combine(currentDir, "VisionSetup.path");
                if (File.Exists(pathFile))
                {
                    var savedPath = File.ReadAllText(pathFile).Trim();
                    if (File.Exists(savedPath))
                        foundPath = savedPath;
                }

                // 2. Fallback: search known relative paths
                if (foundPath == null)
                {
                    var solutionDir = Path.GetFullPath(Path.Combine(currentDir, "..", "..", ".."));
                    var fallbackPaths = new[]
                    {
                        Path.Combine(solutionDir, "VMS.VisionSetup", "bin", "Debug", "net8.0-windows7.0", "VMS.VisionSetup.exe"),
                        Path.Combine(solutionDir, "VMS.VisionSetup", "bin", "Release", "net8.0-windows7.0", "VMS.VisionSetup.exe"),
                    };

                    foreach (var path in fallbackPaths)
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (File.Exists(fullPath))
                        {
                            foundPath = fullPath;
                            break;
                        }
                    }
                }

                if (foundPath != null)
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = foundPath,
                        UseShellExecute = true
                    };

                    // Pass current recipe file path as argument
                    if (!string.IsNullOrEmpty(_currentRecipeFilePath) && File.Exists(_currentRecipeFilePath))
                    {
                        startInfo.Arguments = $"\"{_currentRecipeFilePath}\"";
                    }

                    Process.Start(startInfo);
                }
                else
                {
                    MessageBox.Show(
                        "VMS.VisionSetup 프로그램을 찾을 수 없습니다.\n" +
                        "프로젝트를 먼저 빌드해 주세요.",
                        "Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"프로그램 실행 오류: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LaunchSetup()
        {
            try
            {
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var solutionDir = Path.GetFullPath(Path.Combine(currentDir, "..", "..", ".."));

                var setupPaths = new[]
                {
                    Path.Combine(solutionDir, "BODA.Setup", "bin", "Debug", "net8.0-windows", "BODA.Setup.exe"),
                    Path.Combine(solutionDir, "BODA.Setup", "bin", "Release", "net8.0-windows", "BODA.Setup.exe"),
                    Path.Combine(currentDir, "..", "BODA.Setup", "BODA.Setup.exe")
                };

                string? foundPath = null;
                foreach (var path in setupPaths)
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        foundPath = fullPath;
                        break;
                    }
                }

                if (foundPath != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = foundPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(
                        "BODA.Setup 프로그램을 찾을 수 없습니다.\n" +
                        "프로젝트를 먼저 빌드해 주세요.",
                        "Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"프로그램 실행 오류: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void StartInspection()
        {
            IsRunning = true;
            SystemStatus = "Running...";
        }

        [RelayCommand]
        private void StopInspection()
        {
            IsRunning = false;
            SystemStatus = "Stopped";
        }

        [RelayCommand]
        private void ExitApplication()
        {
            var result = MessageBox.Show(
                "프로그램을 종료하시겠습니까?",
                "Exit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        [RelayCommand]
        private void SelectCamera(CameraViewModel? camera)
        {
            if (camera != null && ReferenceEquals(camera, SelectedCamera) && IsSidePanelOpen)
            {
                // Same camera clicked again while panel is open → close panel
                IsSidePanelOpen = false;
                return;
            }

            SelectedCamera = camera;
            if (camera != null)
            {
                IsSidePanelOpen = true;
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
