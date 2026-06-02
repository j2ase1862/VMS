using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VMS.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 헤더 바 마우스 핸들러 — WindowChrome.CaptionHeight=0 이라 캡션 영역이 없으므로
        /// 헤더 Border 에 직접 드래그 / 더블클릭(최대화 토글) 동작 구현.
        /// 단일 클릭 + 드래그 → DragMove. 더블 클릭 → Maximized ↔ Normal 토글.
        /// 자식 클릭 가능한 컨트롤(버튼 / ToggleButton 등) 은 이벤트가 핸들 처리되어 도달하지 않음.
        /// </summary>
        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                return;
            }

            try { DragMove(); }
            catch { /* 마우스가 이미 떼졌거나 등 — 무시 */ }
        }

        /// <summary>
        /// 최대화/복원 토글 — WindowState 에 따라 Maximized ↔ Normal.
        /// 아이콘은 XAML 의 DataTrigger 가 WindowState 바인딩으로 자동 전환.
        /// </summary>
        private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        /// <summary>
        /// Admin Tools 드롭다운 — 헤더 버튼 클릭 시 첨부된 ContextMenu 를 버튼 아래에 표시.
        /// 메뉴 항목은 5개 Admin 윈도우 (Audit / Health / Backup / AutoBackup / Support) 진입점.
        /// </summary>
        private void AdminToolsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            if (PanelToggle.IsChecked == true && SidePanel.Width > 0)
            {
                // 클릭 위치가 SidePanel 또는 PanelToggle 내부인지 확인
                var clickPos = e.GetPosition(this);
                if (!IsPointInsideElement(SidePanel, clickPos) &&
                    !IsPointInsideElement(PanelToggle, clickPos))
                {
                    PanelToggle.IsChecked = false;
                }
            }
        }

        private bool IsPointInsideElement(FrameworkElement element, Point windowPoint)
        {
            if (element.Visibility != Visibility.Visible || element.ActualWidth == 0)
                return false;

            var elementPos = element.TransformToAncestor(this).Transform(new Point(0, 0));
            var rect = new Rect(elementPos, new Size(element.ActualWidth, element.ActualHeight));
            return rect.Contains(windowPoint);
        }
    }
}
