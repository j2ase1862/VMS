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
        // OnExit 정리 대상 — 창 X 버튼(Closing→ShutdownMode) 경로에서도 메뉴 Exit 경로와
        // 동일하게 정리되도록 App 필드로 보관.
        private MainViewModel? _mainViewModel;
        private ISLMChatService? _slmChatService;
        private IRobotService? _robotService;
        private IParameterSyncService? _parameterSyncService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── 다중 인스턴스 해석 — "--instance <이름>" → BODA_VMS_INSTANCE 환경변수 → 기본 ──
            // VMS(External Tools)에서 실행되면 환경변수로 인스턴스가 자동 상속된다.
            // 모든 AppData 경로가 여기서 파생되므로 보안 정책 로드보다 먼저.
            try
            {
                VMS.Camera.Configuration.AppDataPaths.Initialize(e.Args);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "BODA Vision Tool Setup — 인스턴스 이름 오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(exitCode: 2);
                return;
            }

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
            catch (InvalidOperationException ex)
            {
                // InsecureUrlGuard 차단 (Production + 비-loopback HTTP webServerUrl) —
                // 조용히 삼키면 Web 레시피/파라미터 동기화가 무증상으로 꺼진다 (2026-08-10)
                Debug.WriteLine($"[App] ParameterSyncService init failed: {ex.Message}");
                MessageBox.Show(
                    "Web 서버 연동이 비활성화되었습니다 — 레시피/파라미터 동기화가 동작하지 않습니다.\n\n"
                    + ex.Message,
                    "BODA VisionSetup — Web 연동 경고", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                () => Shutdown(),   // 서비스 정리는 OnExit 에서 일괄 수행

                robotService,
                slmChatService,
                parameterApplyService,
                imageAnalysisService,
                recipeRetrievalService);

            _mainViewModel = viewModel;
            _slmChatService = slmChatService;
            _robotService = robotService;
            _parameterSyncService = parameterSyncService;

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
            // 매뉴얼/문서용 캡처 — Release(배포) 빌드에는 #if DEBUG 로 컴파일되지 않는다.
            //  "--capture-controls [폴더]"   : 컨트롤 개별 PNG (Expander 펼침 + 탭 순회 포함)
            //  "--capture-fullpage [폴더]"   : MainView 전체 화면 1장
            //  "--capture-toolpanels [폴더]" : 전 비전 툴의 우측 Tool Settings 파라미터 패널
            //  "--capture-dialogs [폴더]"    : VisionSetup 다이얼로그 전체 캡처(Sequence/BatchTest …)
            bool isControls = TryGetCaptureOutputDir(e.Args, "--capture-controls", out string captureDir);
            bool isFullPage = TryGetCaptureOutputDir(e.Args, "--capture-fullpage", out string fullPageDir);
            bool isToolPanels = TryGetCaptureOutputDir(e.Args, "--capture-toolpanels", out string toolPanelsDir);
            bool isDialogs = TryGetCaptureOutputDir(e.Args, "--capture-dialogs", out string dialogsDir);
            if (isControls || isFullPage || isToolPanels || isDialogs)
            {
                string dir = isControls ? captureDir : isFullPage ? fullPageDir
                           : isToolPanels ? toolPanelsDir : dialogsDir;
                var dbgLog = Path.Combine(dir, "_capture.log");
                mainView.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(async () =>
                    {
                        try
                        {
                            if (isControls)
                                await Capture.ControlCapturer.RunAsync(mainView, captureDir);
                            if (isFullPage)
                                await Capture.ControlCapturer.RunFullPageAsync(mainView, fullPageDir);
                            if (isToolPanels)
                                await Capture.ControlCapturer.RunToolPanelsAsync(mainView, toolPanelsDir);
                            if (isDialogs)
                            {
                                // fitScroll=true: 내부 ScrollViewer 콘텐츠에 맞춰 창을 키워 전체 스크롤 담기.
                                var wins = new System.Collections.Generic.List<(string, Window, bool)>();
                                try { wins.Add(("Sequence", new Views.Sequence.SequenceEditorWindow(
                                    recipeService, cameraService, dialogService, SequenceEditorContext.ExtraDevices), false)); }
                                catch (Exception ex) { System.IO.File.AppendAllText(dbgLog, "Sequence ctor: " + ex + "\n"); }
                                try { wins.Add(("BatchTest", new Views.BatchTest.BatchTestWindow(
                                    visionService, recipeService, cameraService), false)); }
                                catch (Exception ex) { System.IO.File.AppendAllText(dbgLog, "BatchTest ctor: " + ex + "\n"); }
                                try { wins.Add(("CameraManager", new Views.Camera.CameraManagerWindow(
                                    cameraService, dialogService), false)); }
                                catch (Exception ex) { System.IO.File.AppendAllText(dbgLog, "CameraManager ctor: " + ex + "\n"); }
                                try { wins.Add(("RecipeManager", new Views.Recipe.RecipeManagerWindow(
                                    recipeService, cameraService, dialogService, parameterSyncService), true)); }
                                catch (Exception ex) { System.IO.File.AppendAllText(dbgLog, "RecipeManager ctor: " + ex + "\n"); }
                                try { wins.Add(("InferenceSettings", new Views.OnnxSettingsDialog(), false)); }
                                catch (Exception ex) { System.IO.File.AppendAllText(dbgLog, "InferenceSettings ctor: " + ex + "\n"); }
                                try { wins.Add(("SynthData", new Views.SynthData.SynthDataWindow(), true)); }
                                catch (Exception ex) { System.IO.File.AppendAllText(dbgLog, "SynthData ctor: " + ex + "\n"); }
                                await Capture.ControlCapturer.RunWindowsFullAsync(wins, dialogsDir, mainView);
                            }
                        }
                        catch (Exception ex)
                        {
                            Directory.CreateDirectory(dir);
                            File.WriteAllText(dbgLog, "Capture failed: " + ex);
                        }
                        Shutdown();
                    }));
            }
#endif
        }

        /// <summary>
        /// 종료 시 프로세스 잔존 차단. ShutdownMode=OnMainWindowClose + MainView.Closing 의
        /// Cleanup 만으로는 카메라 SDK 네이티브 스레드 등이 정상 종료(ExitProcess)를 붙잡아
        /// 창을 닫아도 프로세스가 남는 사례가 재발 (현장 검증 2026-07-22, v1.4.18) —
        /// VMS 본체 ForceShutdown 과 동일하게 정리 후 즉시 강제 종료한다.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            // ① 워치독: 아래 정리 코드가 블로킹돼도 3초 후 무조건 종료
            new Thread(() =>
            {
                Thread.Sleep(3000);
                try { Process.GetCurrentProcess().Kill(); } catch { }
            })
            { IsBackground = false, Name = "ExitWatchdog" }.Start();

            // ② 어느 종료 경로(X 버튼 / 메뉴 Exit / Shutdown 직접 호출)로 와도 정리 보장 (idempotent)
            try { _mainViewModel?.Cleanup(); } catch { }
            try { _slmChatService?.Dispose(); } catch { }
            try { _robotService?.Dispose(); } catch { }
            try { _parameterSyncService?.Dispose(); } catch { }

            base.OnExit(e);

            // ③ 즉시 강제 종료 — TerminateProcess 는 CLR 의 포그라운드 스레드 대기와
            // 네이티브 DLL detach 를 모두 건너뛰므로 SDK 스레드가 붙잡을 지점이 없다.
            Process.GetCurrentProcess().Kill();
        }

#if DEBUG
        private static bool TryGetCaptureOutputDir(string[] args, string flag, out string outputDir)
        {
            outputDir = Path.Combine(Path.GetTempPath(), "VMS.VisionSetup.Capture");
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
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
                var configPath = VMS.Camera.Configuration.AppDataPaths.SystemConfigFile;

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
                var configPath = VMS.Camera.Configuration.AppDataPaths.SystemConfigFile;

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
                var configPath = VMS.Camera.Configuration.AppDataPaths.SystemConfigFile;

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
