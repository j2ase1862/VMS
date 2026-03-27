using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
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