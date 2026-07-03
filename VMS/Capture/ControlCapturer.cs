#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VMS.Capture
{
    /// <summary>
    /// DEBUG 전용 — VMS 클라이언트/관리자 다이얼로그를 잘림 없이 전체 캡처하는 매뉴얼/문서용 유틸.
    ///
    /// 수동 영역 캡처가 다이얼로그보다 작게 잡혀 우/하단이 잘리던 매뉴얼 스샷 문제를, 창을 코드로
    /// 화면 밖에서 Show 하여 레이아웃을 확정한 뒤 루트를 통째로 RenderTargetBitmap 으로 렌더해 해결.
    /// VMS.VisionSetup 의 동명 유틸과 동일 원리(다이얼로그 부분).
    ///
    /// 전체 #if DEBUG 가드 → Release(배포) 빌드에서는 컴파일되지 않으며, App 이 "--capture-dialogs"
    /// 인자로 실행될 때만 호출된다. → 일반 실행/배포 동작에 일절 영향 없음.
    /// </summary>
    internal static class ControlCapturer
    {
        private const double Scale = 2.0;   // 2x (192 DPI)

        /// <summary>
        /// 다이얼로그 창들을 각각 전체(잘림 없이) 캡처한다. 화면 밖으로 Show 하여 레이아웃을 확정한
        /// 뒤 창 루트를 통째로 렌더(Left/Top 정렬)하고 닫는다.
        /// </summary>
        public static async Task RunWindowsFullAsync(
            IReadOnlyList<(string name, Window win, bool fitScroll)> windows, string outputDir, Window owner)
        {
            Directory.CreateDirectory(outputDir);
            foreach (var (name, win, fitScroll) in windows)
            {
                try
                {
                    win.Owner = owner;
                    win.ShowInTaskbar = false;
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = -32000; win.Top = -32000;   // 사용자 화면에 깜빡이지 않게 오프스크린

                    // 공칭 크기 — SizeToContent 로 win.Height 가 바뀌기 전에 먼저 읽는다.
                    double w = double.IsNaN(win.Width) || win.Width < 1 ? 1280 : win.Width;
                    double h = double.IsNaN(win.Height) || win.Height < 1 ? 800 : win.Height;

                    // 표준 Window(WindowStyle != None): OS 제목표시줄이 비클라이언트라 내부 ScrollViewer 가
                    // 공칭 높이에서 잘린다. → SizeToContent.Height 로 콘텐츠에 맞춰 키운다.
                    bool standard = win.WindowStyle != WindowStyle.None;
                    if (standard) win.SizeToContent = SizeToContent.Height;
                    win.Show();

                    await LayoutWindow(win, standard, w, h);

                    // Web 비동기 조회(WorkOrders / SyncParameters 등)가 렌더 전에 채워지도록 settle 대기.
                    // 로컬 서비스만 쓰는 창은 이미 채워져 있어 무해(대기만 추가).
                    await Task.Delay(900);
                    await LayoutWindow(win, standard, w, h);

                    // fitScroll: chromeless 창에서 메인 콘텐츠 ScrollViewer 가 넘치면 창을 키워 전체 담기.
                    if (fitScroll && !standard)
                    {
                        var sv = FindContentScrollViewer(win);
                        if (sv?.Content is FrameworkElement sc)
                        {
                            double cw = sc.ActualWidth > 1 ? sc.ActualWidth : w;
                            sc.Measure(new Size(cw, double.PositiveInfinity));
                            double extra = sc.DesiredSize.Height - sv.ActualHeight;
                            if (extra > 2) { h += extra + 10; await LayoutWindow(win, false, w, h); }
                        }
                    }

                    var root = win.Content as FrameworkElement ?? (FrameworkElement)win;
                    // 표준 창: 콘텐츠가 공칭보다 작으면 공칭 프레임(여백 포함)으로, 크면 콘텐츠 전체로.
                    double rw = standard ? System.Math.Max(root.ActualWidth, w) : (root.ActualWidth > 1 ? root.ActualWidth : w);
                    double rh = standard ? System.Math.Max(root.ActualHeight, h) : (root.ActualHeight > 1 ? root.ActualHeight : h);
                    RenderRootToFile(win, root, rw, rh, Path.Combine(outputDir, name + ".png"));
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(outputDir, "_dialog.log"), name + ": " + ex + "\n");
                }
                finally { try { win.Close(); } catch { } }
            }
        }

        /// <summary>이미 보여진 윈도우(MainWindow 등)를 현재 상태 그대로 전체 렌더 (Show/Close 안 함).</summary>
        public static async Task CaptureLiveWindowAsync(Window win, string outputDir, string name)
        {
            Directory.CreateDirectory(outputDir);
            try
            {
                win.UpdateLayout();
                await win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                var root = win.Content as FrameworkElement ?? (FrameworkElement)win;
                double rw = root.ActualWidth > 1 ? root.ActualWidth : (double.IsNaN(win.Width) ? 1920 : win.Width);
                double rh = root.ActualHeight > 1 ? root.ActualHeight : (double.IsNaN(win.Height) ? 1080 : win.Height);
                RenderRootToFile(win, root, rw, rh, Path.Combine(outputDir, name + ".png"));
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(outputDir, "_dialog.log"), name + ": " + ex + "\n");
            }
        }

        private static async Task LayoutWindow(Window win, bool standard, double w, double h)
        {
            for (int i = 0; i < 3; i++)
            {
                if (standard)
                {
                    win.Measure(new Size(w, double.PositiveInfinity));
                    win.Arrange(new Rect(new Point(0, 0), new Size(w, win.DesiredSize.Height)));
                }
                else
                {
                    win.Measure(new Size(w, h));
                    win.Arrange(new Rect(new Point(0, 0), new Size(w, h)));
                }
                win.UpdateLayout();
                await win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
        }

        /// <summary>Content 가 Panel 인 ScrollViewer 중 콘텐츠가 가장 큰 것(=설정 패널 스크롤). 데이터그리드 제외.</summary>
        private static ScrollViewer? FindContentScrollViewer(DependencyObject root)
        {
            ScrollViewer? best = null;
            double bestH = 0;
            void Walk(DependencyObject n)
            {
                int c = VisualTreeHelper.GetChildrenCount(n);
                for (int i = 0; i < c; i++)
                {
                    var ch = VisualTreeHelper.GetChild(n, i);
                    if (ch is ScrollViewer sv && sv.Content is Panel p && p.ActualHeight > bestH)
                    {
                        bestH = p.ActualHeight; best = sv;
                    }
                    Walk(ch);
                }
            }
            Walk(root);
            return best;
        }

        private static void RenderRootToFile(Window win, FrameworkElement root, double rw, double rh, string path)
        {
            Brush backdrop = win.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            var bounds = new Rect(new Point(0, 0), new Size(rw, rh));

            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                ctx.DrawRectangle(backdrop, null, bounds);
                ctx.DrawRectangle(new VisualBrush(root)
                { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, bounds);
            }
            var rtb = new RenderTargetBitmap(
                (int)(rw * Scale), (int)(rh * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }
    }
}
#endif
