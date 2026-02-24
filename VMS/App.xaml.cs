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
                IpAddress = systemConfig.PlcIpAddress,
                Port = systemConfig.PlcPort > 0
                    ? systemConfig.PlcPort
                    : PlcConnectionFactory.GetDefaultPort(systemConfig.PlcVendor)
            };
            IPlcConnection plcConnection = PlcConnectionFactory.Create(plcConfig);
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
                    cam.GrabCommand.Execute(null);
                    return true;
                },
                inspectFunc: async (cameraId) =>
                {
                    var cam = mainViewModel.Cameras.FirstOrDefault(c => c.Id == cameraId);
                    if (cam == null) return false;
                    cam.ManualInspectCommand.Execute(null);
                    // Wait briefly for async command to complete
                    await Task.Delay(100);
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
