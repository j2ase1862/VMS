using System.Windows;

namespace VMS.Views
{
    /// <summary>
    /// 감사 로그 조회 윈도우 — Admin 사용자만 진입. DataContext 는 AuditLogViewerViewModel.
    /// </summary>
    public partial class AuditLogViewerWindow : Window
    {
        public AuditLogViewerWindow()
        {
            InitializeComponent();
        }
    }
}
