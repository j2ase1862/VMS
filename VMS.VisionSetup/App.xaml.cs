using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using VMS.Core.Interfaces;
using VMS.Core.Services;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.ViewModels.ToolSettings;

namespace VMS.VisionSetup
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create services
            IVisionService visionService = VisionService.Instance;
            IRecipeService recipeService = RecipeService.Instance;
            ICameraService cameraService = CameraService.Instance;
            IDialogService dialogService = new DialogService(cameraService, recipeService);

            // ── Web Parameter Sync Service ──
            IParameterSyncService? parameterSyncService = null;
            IParameterApplyService? parameterApplyService = null;
            try
            {
                var (webUrl, clientIdx) = LoadWebSyncConfig();
                parameterSyncService = new ParameterSyncService(webUrl, clientIdx);
                parameterApplyService = new ParameterApplyService(parameterSyncService);

                // ToolSettings ParamCode ComboBox 용 정적 참조 설정
                ToolSettingsViewModelBase.SyncService = parameterSyncService;

                // 시작 시 레시피 목록 + 첫 번째 레시피 파라미터 로드 (백그라운드)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await parameterSyncService.SyncRecipesAsync();
                        var recipes = parameterSyncService.Recipes;
                        if (recipes.Count > 0)
                        {
                            await parameterSyncService.LoadRecipeAsync(recipes[0].Id);
                            Debug.WriteLine($"[App] ParameterSync: Loaded {parameterSyncService.CachedItemCount} params from recipe '{recipes[0].Name}'");
                        }
                        parameterSyncService.StartPeriodicSync(60);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] ParameterSync initial load failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] ParameterSyncService init failed: {ex.Message}");
            }

            // If recipe file path is passed as argument, pre-load it
            if (e.Args.Length > 0)
            {
                var recipePath = e.Args[0];
                if (File.Exists(recipePath))
                {
                    recipeService.LoadRecipe(recipePath);
                }
            }

            // Create MainViewModel with DI
            var viewModel = new MainViewModel(
                visionService,
                recipeService,
                cameraService,
                dialogService,
                () =>
                {
                    parameterSyncService?.Dispose();
                    Shutdown();
                });

            // Create and show main window
            var mainView = new MainView();
            mainView.DataContext = viewModel;
            mainView.Show();
        }

        /// <summary>
        /// AppData의 시스템 설정에서 WebServerUrl/ClientIndex를 읽어옴.
        /// VMS 메인 앱의 SystemConfiguration과 동일 경로 공유.
        /// </summary>
        private static (string webUrl, int clientIndex) LoadWebSyncConfig()
        {
            string defaultUrl = "http://localhost:5292";
            int defaultIndex = 1;

            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BODA VISION AI", "system_config.json");

                if (!File.Exists(configPath))
                    return (defaultUrl, defaultIndex);

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var url = root.TryGetProperty("webServerUrl", out var urlProp)
                    ? urlProp.GetString() ?? defaultUrl
                    : defaultUrl;
                var idx = root.TryGetProperty("clientIndex", out var idxProp)
                    ? idxProp.GetInt32()
                    : defaultIndex;

                return (url, idx);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] LoadWebSyncConfig error: {ex.Message}");
                return (defaultUrl, defaultIndex);
            }
        }
    }
}
