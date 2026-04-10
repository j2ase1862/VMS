using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services;
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

            // ── Robot Service ──
            IRobotService? robotService = null;
            var robotConfig = LoadRobotConfig();
            if (robotConfig.isEnabled)
            {
                try
                {
                    var config = new RobotConnectionConfig
                    {
                        Convention = robotConfig.convention,
                        IpAddress = robotConfig.ipAddress,
                        Port = robotConfig.port,
                        ProtocolMode = robotConfig.protocolMode,
                        ModbusUnitId = robotConfig.modbusUnitId,
                        ModbusPoseStartRegister = robotConfig.modbusPoseRegister
                    };
                    robotService = RobotServiceFactory.Create(config);
                    Debug.WriteLine($"[App] RobotService: {robotService.GetType().Name} created (target: {robotConfig.ipAddress}:{robotConfig.port})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] RobotService init failed: {ex.Message}");
                    robotService = new SimulatedRobotService { Convention = robotConfig.convention };
                }

                // RecipeService에 기본 로봇 설정 적용
                RecipeService.Instance.SetRobotDefaults(
                    robotConfig.ipAddress, robotConfig.port, robotConfig.convention);
            }

            // DialogService에 parameterSyncService 주입 (Web 레시피 동기화용)
            IDialogService dialogService = new DialogService(cameraService, recipeService, parameterSyncService);

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
                    robotService?.Dispose();
                    parameterSyncService?.Dispose();
                    Shutdown();
                },
                robotService);

            // AppSetup 기본 로봇 설정을 ViewModel에 적용 (레시피 미로드 시 기본값)
            if (robotConfig.isEnabled)
            {
                viewModel.RobotIpAddress = robotConfig.ipAddress;
                viewModel.RobotPort = robotConfig.port;
                viewModel.SelectedEulerConvention = robotConfig.convention;
                viewModel.RobotProtocolMode = robotConfig.protocolMode;
            }

            // Create and show main window
            var mainView = new MainView();
            mainView.DataContext = viewModel;
            mainView.Show();
        }

        /// <summary>
        /// AppData의 시스템 설정에서 로봇 구성을 읽어옴.
        /// </summary>
        private static (bool isEnabled, string ipAddress, int port, EulerConvention convention,
            RobotProtocolMode protocolMode, byte modbusUnitId, ushort modbusPoseRegister) LoadRobotConfig()
        {
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BODA VISION AI", "system_config.json");

                if (!File.Exists(configPath))
                    return (false, "192.168.0.200", 30003, EulerConvention.UR_RotationVector,
                        RobotProtocolMode.VendorNative, 1, 270);

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var isEnabled = root.TryGetProperty("isRobotEnabled", out var enabledProp)
                    && enabledProp.GetBoolean();
                var ip = root.TryGetProperty("robotIpAddress", out var ipProp)
                    ? ipProp.GetString() ?? "192.168.0.200"
                    : "192.168.0.200";
                var port = root.TryGetProperty("robotPort", out var portProp)
                    ? portProp.GetInt32()
                    : 30003;
                var convention = EulerConvention.UR_RotationVector;
                if (root.TryGetProperty("eulerConvention", out var convProp))
                {
                    Enum.TryParse<EulerConvention>(convProp.GetString(), out convention);
                }
                var protocolMode = RobotProtocolMode.VendorNative;
                if (root.TryGetProperty("robotProtocolMode", out var protoProp))
                {
                    Enum.TryParse<RobotProtocolMode>(protoProp.GetString(), out protocolMode);
                }
                var modbusUnitId = root.TryGetProperty("robotModbusUnitId", out var unitProp)
                    ? (byte)unitProp.GetInt32()
                    : (byte)1;
                var modbusPoseRegister = root.TryGetProperty("robotModbusPoseRegister", out var regProp)
                    ? (ushort)regProp.GetInt32()
                    : (ushort)270;

                return (isEnabled, ip, port, convention, protocolMode, modbusUnitId, modbusPoseRegister);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] LoadRobotConfig error: {ex.Message}");
                return (false, "192.168.0.200", 30003, EulerConvention.UR_RotationVector,
                    RobotProtocolMode.VendorNative, 1, 270);
            }
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
