using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace VMS.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 컴팩트 헤더 임계 폭 — 이보다 좁으면 헤더 chips 가 축약 표시로 전환된다
        /// (사번/Role 뱃지·진행률 바·보조 라벨 숨김). 풀 chips + 고정 열(로고/AUTO RUN/
        /// User/툴바) 합이 ~1650px 라 그 이하에서 가로 스크롤이 생기던 것을 축약으로 흡수.
        /// </summary>
        private const double CompactHeaderThreshold = 1600;

        /// <summary>
        /// 헤더 축약 모드 — 순수 뷰 레이아웃 상태라 ViewModel 이 아닌 윈도우 DP 로 관리.
        /// 헤더 chips 의 Style DataTrigger 가 RelativeSource 로 바인딩.
        /// </summary>
        public static readonly DependencyProperty IsCompactHeaderProperty =
            DependencyProperty.Register(nameof(IsCompactHeader), typeof(bool), typeof(MainWindow),
                new PropertyMetadata(false));

        public bool IsCompactHeader
        {
            get => (bool)GetValue(IsCompactHeaderProperty);
            set => SetValue(IsCompactHeaderProperty, value);
        }

        public MainWindow()
        {
            InitializeComponent();

            SizeChanged += (_, e) => IsCompactHeader = e.NewSize.Width < CompactHeaderThreshold;
        }

        /// <summary>
        /// WindowStyle=None + WindowState=Maximized 조합 시 WPF 기본 동작은 모니터의 *전체*
        /// 영역(taskbar 포함) 까지 윈도우를 확장하여 작업 표시줄을 가린다. WM_GETMINMAXINFO
        /// 를 후킹해 maximized 크기를 모니터의 work area (taskbar 제외) 로 강제 제한.
        ///
        /// 표준 chromeless WPF 패턴 — DPI 변화 / 다중 모니터 / taskbar 위치(상/하/좌/우) 모두 대응.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                AdjustMaximizedClientRect(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void AdjustMaximizedClientRect(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var work = info.rcWork;
                    var screen = info.rcMonitor;
                    mmi.ptMaxPosition.x = Math.Abs(work.left   - screen.left);
                    mmi.ptMaxPosition.y = Math.Abs(work.top    - screen.top);
                    mmi.ptMaxSize.x     = Math.Abs(work.right  - work.left);
                    mmi.ptMaxSize.y     = Math.Abs(work.bottom - work.top);
                    Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
        }

        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
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
