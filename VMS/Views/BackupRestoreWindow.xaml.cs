using System.Windows;

namespace VMS.Views
{
    /// <summary>
    /// 백업 / 복원 윈도우 — Admin 권한 전용. DataContext 는 BackupRestoreViewModel.
    /// </summary>
    public partial class BackupRestoreWindow : Window
    {
        public BackupRestoreWindow()
        {
            InitializeComponent();
        }
    }
}
