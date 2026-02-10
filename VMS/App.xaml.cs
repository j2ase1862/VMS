using System.Windows;
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

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            await splash.FadeOutAsync();
            splash.Close();
        }
    }
}
