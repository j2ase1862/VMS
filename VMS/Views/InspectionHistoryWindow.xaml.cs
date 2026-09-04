using System.Windows;

namespace VMS.Views
{
    /// <summary>
    /// 로컬 생산(검사) 이력 조회 윈도우 — 모든 사용자 진입 가능. DataContext 는 InspectionHistoryViewModel.
    /// </summary>
    public partial class InspectionHistoryWindow : Window
    {
        public InspectionHistoryWindow()
        {
            InitializeComponent();
        }
    }
}
