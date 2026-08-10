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

        // Web 서버 구성 적용 / 서비스 시작 모드 — WebServerSetupService 가 UAC 상승으로
        // 자신을 재실행한 헤드리스 분기. UI 없이 적용 후 즉시 종료 (마법사 부팅 로직 진입 금지).
        if (e.Args.Length >= 3 && e.Args[0] == WebServerConfigApplier.ArgName)
        {
            Shutdown(WebServerConfigApplier.Run(e.Args[1], e.Args[2]));
            return;
        }
        if (e.Args.Length >= 3 && e.Args[0] == WebServerConfigApplier.StartArgName)
        {
            Shutdown(WebServerConfigApplier.RunStart(e.Args[1], e.Args[2]));
            return;
        }

        // 다중 인스턴스 해석 — "--instance <이름>" 인자 → BODA_VMS_INSTANCE 환경변수 → 기본.
        // VMS 에서 System Setup 으로 실행되면 환경변수로 인스턴스가 자동 상속된다.
        // ConfigurationService 가 경로를 잡기 전에 반드시 먼저 호출.
        try
        {
            VMS.Camera.Configuration.AppDataPaths.Initialize(e.Args);
        }
        catch (System.ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "BODA Vision Setup — 인스턴스 이름 오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(exitCode: 2);
            return;
        }

        IConfigurationService configService = ConfigurationService.Instance;
        IDialogService dialogService = new DialogService();
        IWebServerSetupService webServerSetupService = new WebServerSetupService();

        var mainWindow = new MainWindow();
        var setupVm = new SetupViewModel(configService, dialogService, () => Shutdown(), webServerSetupService);
        mainWindow.DataContext = setupVm;
        mainWindow.Show();

#if DEBUG
        // 매뉴얼/문서용 캡처 — Release(배포) 빌드에는 #if DEBUG 로 컴파일되지 않는다.
        //  "--capture-controls [폴더]"  : 컨트롤 개별 PNG 캡처
        //  "--capture-fullpage [폴더]"  : 페이지 2·5 의 스크롤 포함 전체 콘텐츠 캡처
        bool isControls = TryGetCaptureOutputDir(e.Args, "--capture-controls", out string captureDir);
        bool isFullPage = TryGetCaptureOutputDir(e.Args, "--capture-fullpage", out string fullPageDir);
        if (isControls || isFullPage)
        {
            string dir = isControls ? captureDir : fullPageDir;
            var dbgLog = System.IO.Path.Combine(dir, "_capture.log");
            // Loaded 에 의존하지 않고 디스패처가 idle 되는 즉시 실행 — headless/데스크톱 모두 동작.
            mainWindow.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new System.Action(async () =>
                {
                    try
                    {
                        if (isControls)
                            await Capture.ControlCapturer.RunAsync(mainWindow, setupVm, captureDir);
                        if (isFullPage)
                            await Capture.ControlCapturer.RunFullPageAsync(mainWindow, setupVm, fullPageDir);
                    }
                    catch (System.Exception ex)
                    {
                        System.IO.Directory.CreateDirectory(dir);
                        System.IO.File.WriteAllText(dbgLog, "Capture failed: " + ex);
                    }
                    Shutdown();
                }));
        }
#endif
    }

#if DEBUG
    private static bool TryGetCaptureOutputDir(string[] args, string flag, out string outputDir)
    {
        outputDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VMS.AppSetup.Capture");
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                outputDir = args[i + 1];
            return true;
        }
        return false;
    }
#endif
}
