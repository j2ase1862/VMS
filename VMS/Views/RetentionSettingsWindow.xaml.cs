using System.Windows;

namespace VMS.Views
{
    /// <summary>
    /// 보존 정책 통합 설정 — Admin 권한 전용.
    /// 3개 보존 키 (audit / autoBackup.retentionDays / uploadQueue) 한 화면 편집.
    /// DataContext 는 RetentionSettingsViewModel.
    /// </summary>
    public partial class RetentionSettingsWindow : Window
    {
        public RetentionSettingsWindow()
        {
            InitializeComponent();
        }
    }
}
