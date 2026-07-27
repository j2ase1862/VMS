using System.Windows.Controls;
using VMS.VisionSetup.ViewModels.ToolSettings;

namespace VMS.VisionSetup.Views.ToolSettings.Tools
{
    public partial class Geometry3DToolSettings : UserControl
    {
        public Geometry3DToolSettings()
        {
            InitializeComponent();
        }

        // 드롭다운 열 때 직전 Run 결과로 클러스터 목록 최신화 (UI 이벤트 → VM 갱신 위임)
        private void ClusterCombo_DropDownOpened(object sender, System.EventArgs e)
        {
            (DataContext as Geometry3DToolSettingsViewModel)?.RefreshClusterOptions();
        }

        // 드롭다운 닫힘 = 선택 확정 — 같은 항목을 다시 골라도(값 불변) 강조 표시
        private void ClusterComboA_DropDownClosed(object sender, System.EventArgs e)
        {
            (DataContext as Geometry3DToolSettingsViewModel)?.HighlightClusterA();
        }

        private void ClusterComboB_DropDownClosed(object sender, System.EventArgs e)
        {
            (DataContext as Geometry3DToolSettingsViewModel)?.HighlightClusterB();
        }
    }
}
