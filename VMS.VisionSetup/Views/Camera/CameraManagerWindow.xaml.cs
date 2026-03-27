using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Camera
{
    /// <summary>
    /// CameraManagerWindow.xaml 코드 비하인드
    /// </summary>
    public partial class CameraManagerWindow : Window
    {
        public CameraManagerWindow(ICameraService cameraService, IDialogService dialogService)
        {
            InitializeComponent();
            DataContext = new CameraManagerViewModel(cameraService, dialogService);
        }
    }
}
