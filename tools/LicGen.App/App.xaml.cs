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

            var dialogService = new DialogService();
            var notesStore = new LedgerNotesStore(MainViewModel.DefaultKeyDir);
            var viewModel = new MainViewModel(dialogService, notesStore);

            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();
        }
    }
}
