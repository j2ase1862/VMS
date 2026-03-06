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
        mainWindow.DataContext = new SetupViewModel(configService, dialogService, () => Shutdown());
        mainWindow.Show();
    }
}
