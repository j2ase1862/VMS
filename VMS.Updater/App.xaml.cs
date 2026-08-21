using System.IO;
using System.Windows;
using VMS.Updater.ViewModels;

namespace VMS.Updater
{
    /// <summary>
    /// MSI 업데이트 진행률 표시기. 상승된 부트스트랩 스크립트가
    /// "VMS.Updater.exe &lt;msi 경로&gt; [--current &lt;현재 버전&gt;]" 으로 실행하고,
    /// 종료 코드로 msiexec 호환 값(0/3010 성공)을 돌려받아 후속 단계를 진행한다.
    ///
    /// 창을 닫아도(Alt+F4) 설치 스레드는 계속 진행된다 — ShutdownMode 가
    /// OnExplicitShutdown 이라 프로세스는 설치 완료 시점의 Shutdown(코드)까지 유지.
    /// </summary>
    public partial class App : Application
    {
        private const int ExitCodeInvalidArgs = 87;      // ERROR_INVALID_PARAMETER
        private const int ExitCodePackageMissing = 1619; // ERROR_INSTALL_PACKAGE_OPEN_FAILED

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string? msiPath = null;
            string? currentVersion = null;
            for (var i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--current" && i + 1 < e.Args.Length)
                    currentVersion = e.Args[++i];
#if DEBUG
                else if (e.Args[i] == "--preview")
                {
                    ShowPreview();
                    return;
                }
#endif
                else
                    msiPath ??= e.Args[i];
            }

            if (string.IsNullOrWhiteSpace(msiPath))
            {
                MessageBox.Show(
                    "사용법: VMS.Updater.exe <MSI 경로> [--current <현재 버전>]\n" +
                    "이 프로그램은 VMS 인앱 업데이트가 자동으로 실행합니다.",
                    "BODA VMS Updater", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(ExitCodeInvalidArgs);
                return;
            }

            if (!File.Exists(msiPath))
            {
                MessageBox.Show(
                    $"MSI 파일을 찾을 수 없습니다:\n{msiPath}",
                    "BODA VMS Updater", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(ExitCodePackageMissing);
                return;
            }

            var viewModel = new UpdaterViewModel();
            viewModel.SetVersions(currentVersion, msiPath);

            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();

            _ = viewModel.RunAsync(msiPath);
        }

#if DEBUG
        /// <summary>디자인 확인용 데모 상태 (설치 미실행) — DEBUG 빌드 전용.</summary>
        private void ShowPreview()
        {
            var viewModel = new UpdaterViewModel
            {
                Percent = 43,
                StatusText = "새 파일을 복사하는 중...",
                VersionText = "v1.11.0  →  v1.12.0",
            };
            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();
        }
#endif
    }
}
