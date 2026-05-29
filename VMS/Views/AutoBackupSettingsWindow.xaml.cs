using System.Windows;

namespace VMS.Views
{
    /// <summary>
    /// 자동 백업 설정 — Admin 권한 전용. DataContext 는 AutoBackupSettingsViewModel.
    /// 저장된 값은 VMS 재시작 후 AutoBackupScheduler 가 적용.
    /// </summary>
    public partial class AutoBackupSettingsWindow : Window
    {
        public AutoBackupSettingsWindow()
        {
            InitializeComponent();
        }
    }
}
