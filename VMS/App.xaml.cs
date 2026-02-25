using System.Diagnostics;
using System.Linq;
using System.Windows;
using VMS.Interfaces;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
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

            IConfigurationService configService = ConfigurationService.Instance;
            IRecipeService recipeService = RecipeService.Instance;
            IDialogService dialogService = new DialogService();
            IProcessService processService = new ProcessService();
            IInspectionService inspectionService = InspectionService.Instance;

            // PLC connection setup
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
                () => Shutdown());

            // Wire up AutoProcessService with camera delegates
            IAutoProcessService autoProcessService = new AutoProcessService(
                plcConnection,
                signalConfig,
                systemConfig.PlcVendor,
                grabFunc: async (cameraId) =>
                {
                    var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                    if (cam == null) return false;
                    await cam.GrabCommand.ExecuteAsync(null);
                    return true;
                },
                inspectFunc: async (cameraId) =>
                {
                    var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                    if (cam == null) return false;
                    await cam.ManualInspectCommand.ExecuteAsync(null);
                    return cam.InspectionOk;
                },
                setResultFunc: (cameraId, ok) =>
                {
                    var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                    cam?.SetInspectionResult(ok);
                },
                resetFunc: (cameraId) =>
                {
                    var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                    cam?.ResetInspection();
                });

            // Re-create MainViewModel with AutoProcessService injected
            mainViewModel = new MainViewModel(
                configService,
                recipeService,
                dialogService,
                processService,
                inspectionService,
                () => Shutdown(),
                autoProcessService);

            var mainWindow = new MainWindow();
            mainWindow.DataContext = mainViewModel;
            MainWindow = mainWindow;
            mainWindow.Show();

            await splash.FadeOutAsync();
            splash.Close();
        }
    }
}
