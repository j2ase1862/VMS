using System.Windows;
using VMS.AppSetup.Interfaces;
using VMS.AppSetup.Services;
using VMS.AppSetup.ViewModels;

namespace VMS.AppSetup;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IConfigurationService configService = ConfigurationService.Instance;
        IDialogService dialogService = new DialogService();

        var mainWindow = new MainWindow();
        var setupVm = new SetupViewModel(configService, dialogService, () => Shutdown());
        mainWindow.DataContext = setupVm;
        mainWindow.Show();

#if DEBUG
        // 매뉴얼/문서용 컨트롤 코드 캡처 — "--capture-controls [출력폴더]" 인자로 실행할 때만.
        // Release(배포) 빌드에는 #if DEBUG 로 인해 이 블록과 ControlCapturer 가 컴파일되지 않는다.
        if (TryGetCaptureOutputDir(e.Args, out string captureDir))
        {
            var dbgLog = System.IO.Path.Combine(captureDir, "_capture.log");
            // Loaded 에 의존하지 않고 디스패처가 idle 되는 즉시 실행 — headless/데스크톱 모두 동작.
            mainWindow.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new System.Action(async () =>
                {
                    try
                    {
                        await Capture.ControlCapturer.RunAsync(mainWindow, setupVm, captureDir);
                    }
                    catch (System.Exception ex)
                    {
                        System.IO.Directory.CreateDirectory(captureDir);
                        System.IO.File.WriteAllText(dbgLog, "Capture failed: " + ex);
                    }
                    Shutdown();
                }));
        }
#endif
    }

#if DEBUG
    private static bool TryGetCaptureOutputDir(string[] args, out string outputDir)
    {
        outputDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VMS.AppSetup.Capture");
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--capture-controls", System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                outputDir = args[i + 1];
            return true;
        }
        return false;
    }
#endif
}
