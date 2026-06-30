#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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
            IReadOnlyList<(string name, Window win)> windows, string outputDir, Window owner)
        {
            Directory.CreateDirectory(outputDir);
            foreach (var (name, win) in windows)
            {
                try
                {
                    win.Owner = owner;
                    win.ShowInTaskbar = false;
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = -32000; win.Top = -32000;   // 사용자 화면에 깜빡이지 않게 오프스크린

                    // 표준 Window(WindowStyle != None): OS 제목표시줄이 비클라이언트라 Content 만 잡히고,
                    // 내부 ScrollViewer 가 있으면 공칭 높이에서 잘린다. → SizeToContent.Height 로 창을
                    // 콘텐츠에 맞춰 키워 스크롤 자체가 안 생기게 한 뒤 Content 전체를 렌더.
                    // chromeless(WindowStyle=None) 창은 공칭 크기 그대로 — 데이터그리드 등 과확장 방지.
                    bool standard = win.WindowStyle != WindowStyle.None;
                    if (standard) win.SizeToContent = SizeToContent.Height;
                    win.Show();

                    double w = double.IsNaN(win.Width) || win.Width < 1 ? 1280 : win.Width;
                    double hNom = double.IsNaN(win.Height) || win.Height < 1 ? 800 : win.Height;
                    for (int i = 0; i < 3; i++)
                    {
                        if (standard)
                        {
                            win.Measure(new Size(w, double.PositiveInfinity));
                            win.Arrange(new Rect(new Point(0, 0), new Size(w, win.DesiredSize.Height)));
                        }
                        else
                        {
                            win.Measure(new Size(w, hNom));
                            win.Arrange(new Rect(new Point(0, 0), new Size(w, hNom)));
                        }
                        win.UpdateLayout();
                        await win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }

                    var root = win.Content as FrameworkElement ?? (FrameworkElement)win;
                    double rw = root.ActualWidth > 1 ? root.ActualWidth : w;
                    double rh = root.ActualHeight > 1 ? root.ActualHeight : hNom;
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
                    using (var fs = File.Create(Path.Combine(outputDir, name + ".png"))) enc.Save(fs);
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(outputDir, "_dialog.log"), name + ": " + ex + "\n");
                }
                finally { try { win.Close(); } catch { } }
            }
        }
    }
}
#endif
