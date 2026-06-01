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
