using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.AppSetup.Models;

namespace VMS.AppSetup;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // chromeless 윈도우 — SystemCommands.MinimizeWindow / CloseWindow 활성화
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,
            (s, e) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,
            (s, e) => SystemCommands.CloseWindow(this)));
    }

    private void BrowseDcfFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        var camera = button.DataContext as CameraConfiguration;
        if (camera == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Select DCF File",
            Filter = "DCF Files (*.dcf)|*.dcf|All Files (*.*)|*.*",
            DefaultExt = ".dcf"
        };

        if (dialog.ShowDialog() == true)
        {
            camera.DcfFilePath = dialog.FileName;
        }
    }
}