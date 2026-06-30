using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.Core.Interfaces;
using VMS.Core.Services;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.DeepLearning;
using ISLMChatService = VMS.VisionSetup.Interfaces.ISLMChatService;

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

            // ── Chromeless 윈도우용 SystemCommands 클래스 와이드 바인딩 ──
            RegisterChromelessWindowCommands();

            // ── 보안 정책 로드 (HttpClient / SignalR 초기화 전 필수) ──
            VMS.Core.Security.SecurityOptions.LoadFromAppData();

            // ── ONNX Execution Provider 설정 로드 ──
            LoadAIConfig();

            // ── SequenceEditor 디바이스 콤보 자동 채움 (standalone / 별도 프로세스 경로) ──
            // host(VMS App.xaml.cs) 가 띄운 경우 host 측이 이 holder 를 set 하지만, VMS.VisionSetup
            // 단독 실행 또는 별도 프로세스로 띄운 경우엔 host 가 없음 → 직접 system_config.json 을
            // 읽어 SequenceEditorContext.ExtraDevices 채움. (이미 host 가 채워뒀어도 idempotent)
            PopulateSequenceEditorDevices();

            // Create services
            IVisionService visionService = VisionService.Instance;
            IRecipeService recipeService = RecipeService.Instance;
            ICameraService cameraService = CameraService.Instance;
            // ── Web Parameter Sync Service ──
            IParameterSyncService? parameterSyncService = null;
            IParameterApplyService? parameterApplyService = null;
            try
            {
                var (webUrl, clientIdx, apiKey) = LoadWebSyncConfig();
                parameterSyncService = new ParameterSyncService(webUrl, clientIdx, apiKey);
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

            // ── SLM Chat Service ──
            ISLMChatService slmChatService = new SLMChatService();

            // ── Image Analysis Service (Phase 3) ──
            IImageAnalysisService imageAnalysisService = new ImageAnalysisService();

            // ── Recipe Retrieval Service (Step 2 RAG) ──
            IRecipeRetrievalService recipeRetrievalService = new RecipeRetrievalService(recipeService);

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
                    slmChatService?.Dispose();
                    robotService?.Dispose();
                    parameterSyncService?.Dispose();
                    Shutdown();
                },
                robotService,
                slmChatService,
                parameterApplyService,
                imageAnalysisService,
                recipeRetrievalService);

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

#if DEBUG
            // 매뉴얼/문서용 컨트롤 코드 캡처 — "--capture-controls [출력폴더]" 인자로 실행할 때만.
            // Release(배포) 빌드에는 #if DEBUG 로 인해 이 블록과 ControlCapturer 가 컴파일되지 않는다.
            if (TryGetCaptureOutputDir(e.Args, out string captureDir))
            {
                var dbgLog = Path.Combine(captureDir, "_capture.log");
                mainView.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(async () =>
                    {
                        try
                        {
                            await Capture.ControlCapturer.RunAsync(mainView, captureDir);
                        }
                        catch (Exception ex)
                        {
                            Directory.CreateDirectory(captureDir);
                            File.WriteAllText(dbgLog, "Capture failed: " + ex);
                        }
                        Shutdown();
                    }));
            }
#endif
        }

#if DEBUG
        private static bool TryGetCaptureOutputDir(string[] args, out string outputDir)
        {
            outputDir = Path.Combine(Path.GetTempPath(), "VMS.VisionSetup.Capture");
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--capture-controls", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    outputDir = args[i + 1];
                return true;
            }
            return false;
        }
#endif

        /// <summary>
        /// Chromeless Window가 SystemCommands.{Minimize,Maximize,Restore,Close}WindowCommand 를
        /// 그대로 사용할 수 있도록, Window 타입에 클래스 와이드 커맨드 바인딩을 한 번 등록한다.
        /// 윈도우별 code-behind 핸들러가 필요 없어진다.
        /// </summary>
        private static void RegisterChromelessWindowCommands()
        {
            CommandManager.RegisterClassCommandBinding(typeof(Window),
                new CommandBinding(SystemCommands.MinimizeWindowCommand,
                    (s, e) => { if (s is Window w) SystemCommands.MinimizeWindow(w); }));
            CommandManager.RegisterClassCommandBinding(typeof(Window),
                new CommandBinding(SystemCommands.MaximizeWindowCommand,
                    (s, e) =>
                    {
                        if (s is Window w)
                        {
                            if (w.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(w);
                            else SystemCommands.MaximizeWindow(w);
                        }
                    }));
            CommandManager.RegisterClassCommandBinding(typeof(Window),
                new CommandBinding(SystemCommands.RestoreWindowCommand,
                    (s, e) => { if (s is Window w) SystemCommands.RestoreWindow(w); }));
            CommandManager.RegisterClassCommandBinding(typeof(Window),
                new CommandBinding(SystemCommands.CloseWindowCommand,
                    (s, e) => { if (s is Window w) SystemCommands.CloseWindow(w); }));
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
        /// SequenceEditor 콤보(InputCheck/OutputAction 노드) 에 표시할 디바이스 entry 들을
        /// system_config.json 에서 읽어 SequenceEditorContext 에 주입.
        ///   • PLC: **항상** "MainPLC" entry 를 첫 항목으로 추가 (PlcVendor=None 이어도 표시 —
        ///     사용자가 PLC 옵션을 명시적으로 보고 선택할 수 있게. Runtime 연결 실패는 SequenceEngine 처리).
        ///   • IO 보드: IoBoardConfigLoader 로 IoBoards 각 항목을 entry 추가 (IsEnabled 만).
        /// </summary>
        private static void PopulateSequenceEditorDevices()
        {
            try
            {
                var entries = new System.Collections.Generic.List<SequenceDeviceEntry>
                {
                    // 항상 MainPLC 노출 — IO 보드만 설정된 시스템에서도 PLC 선택지 보장.
                    new SequenceDeviceEntry("MainPLC", VMS.PLC.Models.IoDeviceType.Plc)
                };

                // IO 보드 — IsEnabled=true 인 모든 보드 entry 추가.
                foreach (var ioCfg in IoBoardConfigLoader.LoadFromAppData())
                {
                    var type = IoBoardConfigLoader.ResolveDeviceType(ioCfg);
                    entries.Add(new SequenceDeviceEntry(ioCfg.DeviceId, type));
                }

                SequenceEditorContext.ExtraDevices = entries;
                Debug.WriteLine($"[App] SequenceEditor devices: {entries.Count} " +
                    $"({string.Join(", ", entries.Select(e => e.Display))})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] PopulateSequenceEditorDevices error: {ex.Message}");
            }
        }

        /// <summary>
        /// system_config.json의 AI(ONNX) 섹션에서 Execution Provider 설정을 읽어
        /// OnnxModelBase 정적 속성에 반영합니다.
        /// JSON 예:
        ///   "onnxExecutionProvider": "Auto" | "Cpu" | "Cuda" | "DirectML" | "TensorRT"
        ///   "tensorRTCachePath": "C:/trt_cache"
        ///   "tensorRTFp16": true
        /// </summary>
        private static void LoadAIConfig()
        {
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BODA VISION AI", "system_config.json");

                if (!File.Exists(configPath)) return;

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("onnxExecutionProvider", out var epProp) &&
                    Enum.TryParse<OnnxExecutionProvider>(epProp.GetString(), ignoreCase: true, out var ep))
                {
                    OnnxModelBase.PreferredProvider = ep;
                }

                if (root.TryGetProperty("tensorRTCachePath", out var cacheProp))
                {
                    var cachePath = cacheProp.GetString();
                    // 빈 문자열이면 OnnxModelBase의 합리적 기본값(LocalAppData/trt_cache)을 유지한다.
                    // 기본값 자체가 TRT 엔진 재빌드(30~60초)를 피하는 핵심이므로 명시적 빈 값으로 비활성화하지 않는다.
                    if (!string.IsNullOrWhiteSpace(cachePath))
                        OnnxModelBase.TensorRTCachePath = cachePath;
                }

                if (root.TryGetProperty("tensorRTFp16", out var fp16Prop))
                    OnnxModelBase.TensorRTFp16 = fp16Prop.GetBoolean();

                Debug.WriteLine($"[App] ONNX EP: {OnnxModelBase.PreferredProvider}, " +
                    $"TRT cache: '{OnnxModelBase.TensorRTCachePath}', FP16: {OnnxModelBase.TensorRTFp16}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] LoadAIConfig error: {ex.Message}");
            }
        }

        /// <summary>
        /// AppData의 시스템 설정에서 WebServerUrl/ClientIndex/ClientApiKey 를 읽어옴.
        /// VMS 메인 앱의 SystemConfiguration과 동일 경로 공유.
        /// </summary>
        private static (string webUrl, int clientIndex, string apiKey) LoadWebSyncConfig()
        {
            string defaultUrl = "http://localhost:5292";
            int defaultIndex = 1;

            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BODA VISION AI", "system_config.json");

                if (!File.Exists(configPath))
                    return (defaultUrl, defaultIndex, string.Empty);

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var url = root.TryGetProperty("webServerUrl", out var urlProp)
                    ? urlProp.GetString() ?? defaultUrl
                    : defaultUrl;
                var idx = root.TryGetProperty("clientIndex", out var idxProp)
                    ? idxProp.GetInt32()
                    : defaultIndex;
                // GS 인증: Web 서버 X-API-Key 인증 (BODA.VMS.Web PR #10). 빈 키면 서버
                // 호환 모드(ClientApiKey:Required=false)에서만 통과.
                var apiKey = root.TryGetProperty("clientApiKey", out var keyProp)
                    ? keyProp.GetString() ?? string.Empty
                    : string.Empty;

                return (url, idx, apiKey);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] LoadWebSyncConfig error: {ex.Message}");
                return (defaultUrl, defaultIndex, string.Empty);
            }
        }
    }
}
