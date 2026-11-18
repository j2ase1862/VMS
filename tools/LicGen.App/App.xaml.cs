using System.Windows;
using Boda.LicGen.App.Services;
using Boda.LicGen.App.ViewModels;
using Boda.LicGen.App.Views;

namespace Boda.LicGen.App
{
    /// <summary>수동 DI 배선 — 솔루션 관례 (DI 컨테이너 미사용, App.xaml.cs 에서 구성).</summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

#if DEBUG
            // 매뉴얼 그림용 — "--capture <폴더>" 이면 실제 키 폴더 대신 임시 폴더(버리는 키 · 예시 대장)로 띄워
            // 두 탭을 PNG 로 남기고 끝낸다. 실제 키 · 대장(%USERPROFILE%\.boda-licgen)은 읽지 않는다.
            int ci = Array.IndexOf(e.Args, "--capture");
            if (ci >= 0 && ci + 1 < e.Args.Length)
            {
                Capture.WindowCapture.Run(this, e.Args[ci + 1]);
                return;
            }
#endif

            var dialogService = new DialogService();
            var notesStore = new LedgerNotesStore(MainViewModel.DefaultKeyDir);
            var viewModel = new MainViewModel(dialogService, notesStore);

            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();
        }
    }
}
