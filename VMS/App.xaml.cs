using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using VMS.Camera.Services;
using VMS.Interfaces;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Models.Sequence;
using VMS.PLC.Services;
using VMS.Services;
using VMS.ViewModels;
using VMS.Views;

namespace VMS
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
                    await Current.Dispatcher.InvokeAsync(() =>
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
                        }
                    });
                },
                processSequence: processSequence,
                logService: logService);

            // Re-create MainViewModel with AutoProcessService injected
            mainViewModel = new MainViewModel(
                configService,
                recipeService,
                dialogService,
                processService,
                inspectionService,
                () => Shutdown(),
                autoProcessService,
                userService,
                logService,
                sharedFrameWriter,
                plcConnection: plcConnection,
                plcVendorName: systemConfig.PlcVendor.ToString(),
                plcIpAddress: systemConfig.PlcIpAddress);

            var mainWindow = new MainWindow();
            mainWindow.DataContext = mainViewModel;
            MainWindow = mainWindow;
            mainWindow.Closed += (_, _) =>
            {
                sharedFrameWriter?.Dispose();
                Shutdown();
            };
            mainWindow.Show();

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
    }
}
