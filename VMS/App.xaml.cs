using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows;
using VMS.Camera.Services;
using VMS.Core.Backup;
using VMS.Core.Health;
using VMS.Core.Interfaces;
using VMS.Core.Retention;
using VMS.Core.Security;
using VMS.Core.Services;
using VMS.Interfaces;
using HeartbeatService = VMS.Core.Services.HeartbeatService;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services;
using VMS.ViewModels;
using VMS.Views;
using VsParameterApplyService = VMS.VisionSetup.Services.ParameterApplyService;

namespace VMS
{
    public partial class App : Application
    {
        // 자동 백업 스케줄러 — system_config.json 의 autoBackup.enabled=true 일 때만 활성.
        // OnExit 에서 Dispose 로 타이머 종료.
        private AutoBackupScheduler? _autoBackupScheduler;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Chromeless Admin 윈도우들이 SystemCommands.{Minimize,Maximize,Restore,Close}
            // WindowCommand 를 그대로 사용하도록 Window 클래스 와이드 커맨드 바인딩 등록.
            // ChromelessTitleBar (VMS.VisionSetup.Views.Common) 가 이 커맨드들을 호출함.
            RegisterChromelessWindowCommands();

            // 최초 실행 감지 — system_config.json 부재 시 AppSetup 자동 실행 후 VMS 종료.
            // MSI 설치 직후 빈 AppData 환경 또는 사용자가 AppData 초기화 시 트리거.
            // AppSetup 완료(저장) → system_config.json 생성 → 사용자가 VMS 재실행 시 정상 시작.
            if (TryLaunchInitialSetup())
            {
                Shutdown();
                return;
            }

            // 보안 정책 로드 — system_config.json 의 "securityMode" 키에서 결정.
            // 누락 시 Development 폴백 (기존 dev 동작 호환). 모든 HttpClient / SignalR 이
            // 이 정책을 참조하므로 다른 서비스 초기화 전에 반드시 호출.
            SecurityOptions.LoadFromAppData();

            // 감사 로그 보존 정책 — system_config.json 의 "auditRetentionDays" 키 (기본 365).
            // 오늘 파일은 보존 기간 무관 유지. 정리 작업 자체도 AuditCategory.System 으로 기록.
            try
            {
                var retentionDays = AuditLogRetention.LoadRetentionDaysFromAppData();
                AuditLogRetention.CleanupOldFiles(AuditLogger.Instance.AuditDirectory, retentionDays);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] AuditLogRetention 실패 (best-effort): {ex.Message}");
            }

            // 카테고리별 차등 보존 (GS 권장) — Whole-file 정리 후 남은 파일에서
            // 카테고리별 만료 라인 필터. system_config.json 의 "auditCategoryRetentionDays".
            try
            {
                var perCat = AuditCategoryRetention.LoadDaysFromAppData();
                AuditCategoryRetention.FilterFilesByCategory(AuditLogger.Instance.AuditDirectory, perCat);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] AuditCategoryRetention 실패 (best-effort): {ex.Message}");
            }

            // 시작 헬스 체크 — 디렉토리 쓰기 가능, system_config 존재, 보안 옵션 로드,
            // 디스크 여유 ≥ 1 GB. 종합 결과는 AuditCategory.System 의 단일 이벤트로 기록.
            // best-effort — Fail 상태여도 UI 차단 없음 (감사 흔적 남기는 데 의의).
            try
            {
                StartupHealthCheck.Run();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] StartupHealthCheck 실패 (best-effort): {ex.Message}");
            }

            // upload_queue 보존 정책 — 실패한 검사 결과 업로드 JSON 누적 방지.
            // system_config.json 의 "uploadQueueRetentionDays" (기본 30, clamp [1, 365]).
            // 파일명 timestamp 패턴 미일치 시 절대 미터치 — 사용자 임의 파일 보호.
            try
            {
                var queueDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BODA VISION AI", "upload_queue");
                var days = UploadQueueRetention.LoadRetentionDaysFromAppData();
                UploadQueueRetention.CleanupOldFiles(queueDir, days);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] UploadQueueRetention 실패 (best-effort): {ex.Message}");
            }

            // 자동 백업 스케줄러 — autoBackup.enabled=true 일 때만 시작.
            // last_backup_at.txt 기반으로 잔여 시간 계산 → 즉시 또는 잔여 시간 후 첫 백업.
            // 운영 시작을 막지 않도록 best-effort.
            try
            {
                var autoBackupOptions = AutoBackupOptions.LoadFromAppData();
                if (autoBackupOptions.Enabled)
                {
                    var appDataPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "BODA VISION AI");
                    _autoBackupScheduler = new AutoBackupScheduler(appDataPath, autoBackupOptions);
                    _autoBackupScheduler.Start();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] AutoBackupScheduler 시작 실패 (best-effort): {ex.Message}");
            }

            var splash = new SplashWindow();
            splash.Show();

            await splash.LoadAsync();

            // ── Services ──
            IConfigurationService configService = ConfigurationService.Instance;
            IRecipeService recipeService = RecipeService.Instance;
            IDialogService dialogService = new DialogService();
            IProcessService processService = new ProcessService();
            IInspectionService inspectionService = InspectionService.Instance;
            IUserService userService = UserService.Instance;
            ISystemLogService logService = SystemLogService.Instance;

            // 업데이트 알림 체커(Phase B) — public repo j2ase1862/VMS 의 releases/latest 조회.
            // 시작 시 1회 best-effort 호출(아래) + 사이드 패널 "Check for updates" 버튼으로 수동 재호출.
            VMS.Core.Interfaces.IUpdateService updateService =
                new VMS.Core.Services.GitHubUpdateService("j2ase1862", "VMS");

            await splash.FadeOutAsync();
            splash.Close();

            // ── SharedFrameWriter (MMF 프레임 공유) ──
            SharedFrameWriter? sharedFrameWriter = null;
            try
            {
                sharedFrameWriter = new SharedFrameWriter();
                sharedFrameWriter.Initialize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] SharedFrameWriter init failed: {ex.Message}");
                sharedFrameWriter?.Dispose();
                sharedFrameWriter = null;
            }

            // ── PLC connection setup ──
            var systemConfig = configService.LoadSystemConfiguration();
            var plcConfig = new PlcConnectionConfig
            {
                Vendor = systemConfig.PlcVendor,
                CommunicationType = systemConfig.CommunicationType,
                IpAddress = systemConfig.PlcIpAddress,
                Port = systemConfig.PlcPort > 0
                    ? systemConfig.PlcPort
                    : PlcConnectionFactory.GetDefaultPort(systemConfig.PlcVendor),

                // Modbus
                UnitId = systemConfig.ModbusUnitId,

                // Serial
                SerialPortName = systemConfig.SerialPortName,
                BaudRate = systemConfig.BaudRate,
                DataBits = systemConfig.DataBits,
                Parity = systemConfig.Parity,
                StopBits = systemConfig.StopBits,

                // Performance & Stability
                PollingIntervalMs = systemConfig.PollingIntervalMs,
                UseHeartbeat = systemConfig.UseHeartbeat,
                HeartbeatAddress = systemConfig.HeartbeatAddress,
                AutoReconnect = systemConfig.AutoReconnect,

                // Data Synchronization
                WriteMode = systemConfig.WriteMode,
                EndianMode = systemConfig.EndianMode
            };
            var plcConnection = PlcConnectionFactory.Create(plcConfig, resilient: systemConfig.AutoReconnect);

            // Wire up PLC log callback if using ResilientPlcConnection
            if (plcConnection is ResilientPlcConnection resilientPlc)
            {
                resilientPlc.LogCallback = entry =>
                    Debug.WriteLine($"[PLC] {entry}");
            }

            // ── IO 디바이스 레지스트리 (Phase 2 — PLC + IO 보드 통합) ──
            // 기본 PLC 를 "MainPLC" 로 등록 + SystemConfiguration.IoBoards 의 각 보드 생성/연결/등록.
            // SequenceEngine 이 SequenceNodeConfig.DeviceId 로 lookup 하여 적절한 디바이스에 read/write.
            var ioDeviceRegistry = new IoDeviceRegistry();
            ioDeviceRegistry.Register(plcConnection);  // DeviceId 기본값 = "MainPLC"

            var ioBoardConnections = new List<VMS.PLC.Interfaces.IIoBoardConnection>();
            foreach (var ioConfig in systemConfig.IoBoards ?? new List<VMS.PLC.Models.IoDeviceConfig>())
            {
                try
                {
                    var board = IoBoardConnectionFactory.Create(ioConfig);
                    if (board is null) continue;
                    // ConnectAsync 실패는 한 보드만 격리 — 다른 보드/PLC 는 계속 진행
                    _ = board.ConnectAsync();
                    ioDeviceRegistry.Register(board);
                    ioBoardConnections.Add(board);
                    Debug.WriteLine($"[App] IO board registered: {ioConfig.DeviceId} ({ioConfig.Vendor} {ioConfig.Model})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] IO board init failed ({ioConfig.DeviceId}): {ex.Message}");
                }
            }

            // SequenceEditor 콤보 자동 채움 — PLC + IO 보드 entry 들을 VMS.VisionSetup holder 에 주입.
            // **항상 MainPLC 가 첫 항목** — PlcVendor=None 이어도 콤보에서 PLC 옵션 노출.
            var sequenceDevices = new List<VMS.VisionSetup.Services.SequenceDeviceEntry>
            {
                new VMS.VisionSetup.Services.SequenceDeviceEntry(
                    "MainPLC", VMS.PLC.Models.IoDeviceType.Plc)
            };
            foreach (var board in ioBoardConnections)
            {
                sequenceDevices.Add(new VMS.VisionSetup.Services.SequenceDeviceEntry(
                    board.DeviceId, board.DeviceType));
            }
            VMS.VisionSetup.Services.SequenceEditorContext.ExtraDevices = sequenceDevices;

            var signalConfig = configService.LoadPlcSignalConfiguration();

            var mainViewModel = new MainViewModel(
                configService,
                recipeService,
                dialogService,
                processService,
                inspectionService,
                () => Shutdown(),
                userService: userService,
                logService: logService);

            // ── Web Parameter Sync Service ──
            IParameterSyncService? parameterSyncService = null;
            try
            {
                parameterSyncService = new ParameterSyncService(
                    systemConfig.WebServerUrl, systemConfig.ClientIndex);
                parameterSyncService.StartPeriodicSync(60);

                // 앱 시작 시 Web 레시피 목록 동기화
                _ = parameterSyncService.SyncRecipesAsync();

                // InspectionService에 주입
                InspectionService.ParameterSyncService = parameterSyncService;
                InspectionService.ParameterApplyService =
                    new VsParameterApplyService(parameterSyncService);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] ParameterSyncService init failed: {ex.Message}");
            }

            // ── Predictive Polling Service (Plan §5.3 — V5 위젯) ──
            IPredictionPollingService? predictionPollingService = null;
            try
            {
                predictionPollingService = new PredictionPollingService(
                    systemConfig.WebServerUrl, systemConfig.ClientIndex);
                predictionPollingService.StartPeriodicPolling(60);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] PredictionPollingService init failed: {ex.Message}");
            }

            // ── Sensor Polling Service (Plan §5.2 — V4 환경 센서) ──
            // Reader 는 Mock(모든 null)이 기본 — "센서 미연결" 안전 기본값. 실제 PLC reader 가
            // 준비되면 IEnvironmentSensorReader 구현을 교체하기만 하면 즉시 송신 시작.
            // Reader 가 모든 null 반환 시 SensorPollingService 가 송신 자체를 skip → 무부하.
            ISensorPollingService? sensorPollingService = null;
            try
            {
                IEnvironmentSensorReader sensorReader = new MockEnvironmentSensorReader();
                sensorPollingService = new SensorPollingService(
                    systemConfig.WebServerUrl, systemConfig.ClientIndex, sensorReader);
                sensorPollingService.StartPeriodicPolling(5);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] SensorPollingService init failed: {ex.Message}");
            }

            // ── Operator Auth Service (Stage 1: 작업자 로그인) ──
            VMS.Core.Services.OperatorAuthService? operatorAuthService = null;
            try
            {
                operatorAuthService = new VMS.Core.Services.OperatorAuthService(
                    systemConfig.WebServerUrl, systemConfig.ClientIndex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] OperatorAuthService init failed: {ex.Message}");
            }

            // ── Work Order Client (Stage 2: 작업지시 목록) ──
            VMS.Core.Services.WorkOrderClient? workOrderClient = null;
            try
            {
                workOrderClient = new VMS.Core.Services.WorkOrderClient(
                    systemConfig.WebServerUrl, systemConfig.ClientIndex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] WorkOrderClient init failed: {ex.Message}");
            }

            // ── Lot Client (B1: WO 선택 시 활성 Lot 자동 채움) ──
            VMS.Core.Services.LotClient? lotClient = null;
            try
            {
                lotClient = new VMS.Core.Services.LotClient(systemConfig.WebServerUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] LotClient init failed: {ex.Message}");
            }

            // ── VMS Hub Client (C5: SignalR 실시간 푸시) ──
            VMS.Core.Services.VmsHubClient? vmsHubClient = null;
            try
            {
                vmsHubClient = new VMS.Core.Services.VmsHubClient(systemConfig.WebServerUrl);
                _ = vmsHubClient.StartAsync(); // fire & forget — 실패해도 응답 기반 fallback 으로 동작
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] VmsHubClient init failed: {ex.Message}");
            }

            // ── Web Heartbeat Service ──
            HeartbeatService? heartbeatService = null;
            try
            {
                heartbeatService = new HeartbeatService(
                    systemConfig.WebServerUrl,
                    systemConfig.VisionServerUrl,
                    systemConfig.ClientIndex,
                    systemConfig.SystemIpAddress,
                    systemConfig.ApplicationName);
                heartbeatService.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] HeartbeatService init failed: {ex.Message}");
            }


            // Load system-level process sequence
            var processSequence = LoadSystemSequence();

            // Wire up AutoProcessService with camera delegates
            // 모든 카메라 접근 델리게이트는 Dispatcher를 통해 UI 스레드에서 실행
            // (프로세스 루프가 ThreadPool에서 실행되므로 필수)
            IAutoProcessService autoProcessService = new AutoProcessService(
                plcConnection,
                signalConfig,
                systemConfig.PlcVendor,
                grabFunc: async (cameraId) =>
                {
                    var innerTask = await Current.Dispatcher.InvokeAsync(async () =>
                    {
                        var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                        if (cam == null) return false;
                        await cam.GrabCommand.ExecuteAsync(null);
                        return true;
                    });
                    return await innerTask;
                },
                inspectFunc: async (cameraId) =>
                {
                    var innerTask = await Current.Dispatcher.InvokeAsync(async () =>
                    {
                        var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                        if (cam == null) return false;
                        await cam.ManualInspectCommand.ExecuteAsync(null);
                        return cam.InspectionOk;
                    });
                    return await innerTask;
                },
                setResultFunc: (cameraId, ok) =>
                {
                    Current.Dispatcher.Invoke(() =>
                    {
                        var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                        cam?.SetInspectionResult(ok);
                    });
                },
                resetFunc: (cameraId) =>
                {
                    Current.Dispatcher.Invoke(() =>
                    {
                        var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                        cam?.ResetInspection();
                    });
                },
                getToolResultsFunc: (cameraId) =>
                {
                    return Current.Dispatcher.Invoke(() =>
                    {
                        var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                        return cam?.LastToolResults;
                    });
                },
                stepChangeFunc: (stepIndex) =>
                {
                    Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var cam in mainViewModel.Cameras)
                            cam.CurrentStepIndex = stepIndex;
                        logService.Log($"Step changed to index {stepIndex}", LogLevel.Info, "StepChange");
                    });
                },
                recipeChangeByIndexFunc: async (recipeIndex) =>
                {
                    await Current.Dispatcher.InvokeAsync(async () =>
                    {
                        var recipes = recipeService.GetRecipeList();
                        if (recipeIndex < 0 || recipeIndex >= recipes.Count)
                        {
                            logService.Log($"Recipe index {recipeIndex} out of range (0~{recipes.Count - 1})", LogLevel.Warning, "RecipeChange");
                            return;
                        }

                        var target = recipes[recipeIndex];
                        if (recipeService.CurrentRecipe?.Id == target.Id)
                            return; // 현재 레시피와 동일 — 스킵

                        var recipe = recipeService.LoadRecipe(target.FilePath);
                        if (recipe != null)
                        {
                            recipeService.SetCurrentRecipe(recipe);
                            mainViewModel.CurrentRecipe = recipe;
                            mainViewModel.CurrentRecipeName = recipe.Name;
                            foreach (var cam in mainViewModel.Cameras)
                                cam.SetRecipe(recipe);
                            logService.Log($"Recipe changed to [{recipeIndex}] {recipe.Name}", LogLevel.Success, "RecipeChange");

                            // Web 파라미터 동기화 — Web 레시피 목록을 먼저 갱신 후 올바른 ID로 로드
                            if (parameterSyncService != null)
                            {
                                await parameterSyncService.SyncRecipesAsync();
                                var webRecipes = parameterSyncService.Recipes;
                                if (recipeIndex >= 0 && recipeIndex < webRecipes.Count)
                                {
                                    var webRecipeId = webRecipes[recipeIndex].Id;
                                    await parameterSyncService.LoadRecipeAsync(webRecipeId);
                                }
                                else
                                {
                                    logService.Log($"Web recipe index {recipeIndex} out of range (0~{webRecipes.Count - 1})", LogLevel.Warning, "RecipeChange");
                                }
                            }
                        }
                    });
                },
                processSequence: processSequence,
                logService: logService,
                ioRegistry: ioDeviceRegistry);

            // ── Roller Inspection Service ──
            IRollerInspectionService rollerInspectionService = new RollerInspectionService(logService);

            // Re-create MainViewModel with AutoProcessService injected
            mainViewModel = new MainViewModel(
                configService,
                recipeService,
                dialogService,
                processService,
                inspectionService,
                () =>
                {
                    operatorAuthService?.Dispose();
                    workOrderClient?.Dispose();
                    lotClient?.Dispose();
                    if (vmsHubClient != null) _ = vmsHubClient.DisposeAsync();
                    predictionPollingService?.Dispose();
                    sensorPollingService?.Dispose();
                    foreach (var board in ioBoardConnections) board.Dispose();
                    ForceShutdown(mainViewModel, heartbeatService, parameterSyncService, sharedFrameWriter, plcConnection, autoProcessService);
                },
                autoProcessService,
                userService,
                logService,
                sharedFrameWriter,
                plcConnection: plcConnection,
                plcVendorName: systemConfig.PlcVendor.ToString(),
                plcIpAddress: systemConfig.PlcIpAddress,
                rollerInspectionService: rollerInspectionService,
                heartbeatService: heartbeatService,
                parameterSyncService: parameterSyncService,
                operatorAuthService: operatorAuthService,
                workOrderClient: workOrderClient,
                lotClient: lotClient,
                vmsHubClient: vmsHubClient,
                predictionPollingService: predictionPollingService,
                updateService: updateService);

            var mainWindow = new MainWindow();
            mainWindow.DataContext = mainViewModel;
            MainWindow = mainWindow;
            mainWindow.Closed += (_, _) =>
            {
                operatorAuthService?.Dispose();
                workOrderClient?.Dispose();
                lotClient?.Dispose();
                if (vmsHubClient != null) _ = vmsHubClient.DisposeAsync();
                predictionPollingService?.Dispose();
                sensorPollingService?.Dispose();
                updateService?.Dispose();
                foreach (var board in ioBoardConnections) board.Dispose();
                ForceShutdown(mainViewModel, heartbeatService, parameterSyncService, sharedFrameWriter, plcConnection, autoProcessService);
            };
            mainWindow.Show();

            // 업데이트 체크 — best-effort fire-and-forget. UI 차단 금지. 실패해도 silent.
            // 결과는 MainViewModel.LatestUpdate 에 저장되어 사이드 패널 배지가 자동 노출.
            _ = mainViewModel.CheckForUpdatesSilentAsync();

            // ── 카메라 자동 연결 (UI 표시 후 백그라운드) ──
            _ = Task.Run(async () =>
            {
                foreach (var cam in mainViewModel.Cameras.Where(c => c.IsEnabled))
                {
                    try
                    {
                        await Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await cam.InitializeConnectionAsync();
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] Camera auto-connect failed ({cam.Name}): {ex.Message}");
                    }
                }
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    logService.Log(
                        $"카메라 자동 연결 완료 ({mainViewModel.Cameras.Count(c => c.IsConnected)}/{mainViewModel.Cameras.Count(c => c.IsEnabled)})",
                        VMS.Interfaces.LogLevel.Info, "System");
                });
            });
        }

        /// <summary>
        /// 시스템 레벨 프로세스 시퀀스 로드 (AppData process_sequence.json).
        /// VisionSetup 시퀀스 에디터와 동일 경로 공유.
        /// </summary>
        private static SequenceConfig? LoadSystemSequence()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BODA VISION AI", "process_sequence.json");

                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                };
                return JsonSerializer.Deserialize<SequenceConfig>(json, options);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] System sequence load error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 프로세스를 확실히 종료.
        /// UI 스레드에서 Dispose를 호출하지 않음 — 모든 정리는 백그라운드에서.
        /// Kill 타이머(별도 스레드)가 Dispose 블로킹과 무관하게 프로세스를 종료.
        /// </summary>
        /// <summary>
        /// Chromeless Window 가 SystemCommands.{Minimize,Maximize,Restore,Close}WindowCommand 를
        /// 그대로 사용할 수 있도록 Window 타입에 클래스 와이드 커맨드 바인딩 등록.
        /// VMS.VisionSetup 의 App.RegisterChromelessWindowCommands 와 동일 패턴.
        /// </summary>
        /// <summary>
        /// system_config.json 부재 시 VMS.AppSetup.exe 를 실행. 트리거되면 true 반환 → 호출 측에서 Shutdown.
        /// AppSetup.exe 부재 또는 실행 실패 시 false → VMS 정상 진행(기본 config 사용).
        /// </summary>
        private static bool TryLaunchInitialSetup()
        {
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BODA VISION AI", "system_config.json");
                if (File.Exists(configPath)) return false;

                var setupExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VMS.AppSetup.exe");
                if (!File.Exists(setupExe))
                {
                    Debug.WriteLine($"[App] TryLaunchInitialSetup: VMS.AppSetup.exe not found at {setupExe}");
                    return false;
                }

                MessageBox.Show(
                    "초기 시스템 설정이 필요합니다.\n시스템 설정 마법사를 실행합니다.\n\n설정 완료 후 VMS를 다시 실행해 주세요.",
                    "BODA Vision System - 최초 실행",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Process.Start(new ProcessStartInfo
                {
                    FileName = setupExe,
                    UseShellExecute = true,
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] TryLaunchInitialSetup 실패: {ex.Message}");
                return false;
            }
        }

        private static void RegisterChromelessWindowCommands()
        {
            System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
                new System.Windows.Input.CommandBinding(SystemCommands.MinimizeWindowCommand,
                    (s, e) => { if (s is Window w) SystemCommands.MinimizeWindow(w); }));
            System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
                new System.Windows.Input.CommandBinding(SystemCommands.MaximizeWindowCommand,
                    (s, e) =>
                    {
                        if (s is Window w)
                        {
                            if (w.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(w);
                            else SystemCommands.MaximizeWindow(w);
                        }
                    }));
            System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
                new System.Windows.Input.CommandBinding(SystemCommands.RestoreWindowCommand,
                    (s, e) => { if (s is Window w) SystemCommands.RestoreWindow(w); }));
            System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
                new System.Windows.Input.CommandBinding(SystemCommands.CloseWindowCommand,
                    (s, e) => { if (s is Window w) SystemCommands.CloseWindow(w); }));
        }

        private static int _shutdownRequested;
        private void ForceShutdown(
            MainViewModel? viewModel,
            HeartbeatService? heartbeat,
            IParameterSyncService? paramSync,
            SharedFrameWriter? frameWriter,
            IPlcConnection? plcConnection,
            IAutoProcessService? autoProcess)
        {
            if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1)
                return;

            // ① Kill 타이머: 3초 후 무조건 프로세스 종료 (Dispose 블로킹과 독립)
            new Thread(() =>
            {
                Thread.Sleep(3000);
                Process.GetCurrentProcess().Kill();
            })
            { IsBackground = false, Name = "KillTimer" }.Start();

            // ② 정리 스레드: 카메라/PLC/서비스 정리 시도 (블로킹되면 ①이 종료시킴)
            new Thread(() =>
            {
                // 카메라 Live 중지 + 연결 해제
                if (viewModel != null)
                {
                    foreach (var cam in viewModel.Cameras)
                    {
                        try { cam.StopLiveGrabAsync().Wait(500); } catch { }
                    }
                }

                // AutoProcess 정지
                try { autoProcess?.StopAsync().Wait(1000); } catch { }

                // PLC 연결 해제
                try { plcConnection?.DisconnectAsync().Wait(1000); } catch { }

                // Web 서비스 정리
                try { heartbeat?.Dispose(); } catch { }
                try { paramSync?.Dispose(); } catch { }
                try { frameWriter?.Dispose(); } catch { }

                // 자동 백업 스케줄러 정지 — 타이머 콜백이 진행 중이면 미완료 백업 한 건 손실 가능,
                // 다음 시작 시 last_backup_at.txt 기반으로 자동 재개.
                try { _autoBackupScheduler?.Dispose(); } catch { }

                // 정리 완료 → 즉시 종료
                Process.GetCurrentProcess().Kill();
            })
            { IsBackground = true, Name = "CleanupThread" }.Start();

            // UI 스레드: WPF 종료 시작 (non-blocking, 즉시 반환)
            try { Shutdown(); } catch { }
        }
    }
}
