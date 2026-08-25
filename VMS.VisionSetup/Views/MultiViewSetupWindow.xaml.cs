using System.Windows;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views
{
    public partial class MultiViewSetupWindow : Window
    {
        public MultiViewSetupWindow()
        {
            InitializeComponent();
        }

        private void HandEyeCalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel mainVm)
                return;

            if (mainVm.RobotService == null || !mainVm.RobotService.IsConnected)
            {
                Common.MessageDialog.Show(this, "로봇이 연결되어 있지 않습니다.\n먼저 로봇을 연결하세요.",
                    "Hand-Eye Calibration", Common.MessageDialogKind.Warning);
                return;
            }

            if (mainVm.CameraAcquisition == null)
            {
                Common.MessageDialog.Show(this, "카메라가 연결되어 있지 않습니다.\n먼저 카메라를 연결하세요.",
                    "Hand-Eye Calibration", Common.MessageDialogKind.Warning);
                return;
            }

            var vm = new HandEyeCalibrationViewModel(
                mainVm.RobotService,
                mainVm.CameraAcquisition,
                mainVm.DialogServiceAccessor);

            var window = new HandEyeCalibrationWindow
            {
                DataContext = vm,
                Owner = this
            };

            window.ShowDialog();

            // 저장 완료 시 캘리브레이션 경로 자동 설정
            if (vm.SavedFilePath != null)
            {
                mainVm.HandEyeCalibrationPath = vm.SavedFilePath;
            }
        }
    }
}
