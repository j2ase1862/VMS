using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VMS.VisionSetup.Controls;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 도구 워크스페이스 확대/축소 호스트 — 맞춤(Fit)·휠 확대 기준점·배율 범위.
    /// 실제 WPF 배치(Measure/Arrange)가 필요해 STA 스레드에서 돈다.
    /// </summary>
    public class ZoomPanHostTests
    {
        /// <summary>700×600 호스트 안에 (x,y,w,h) 사각형 하나를 가진 콘텐츠.</summary>
        private static (ZoomPanHost host, Grid content) Build(double x, double y, double w, double h)
        {
            var canvas = new Canvas();
            var rect = new Rectangle { Width = w, Height = h, Fill = Brushes.Gray };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);
            var content = new Grid();
            content.Children.Add(canvas);

            var host = new ZoomPanHost { Background = Brushes.Black, Child = content };
            host.Measure(new Size(700, 600));
            host.Arrange(new Rect(0, 0, 700, 600));
            host.UpdateLayout();
            return (host, content);
        }

        /// <summary>콘텐츠 좌표 p 가 호스트 화면 좌표 어디에 그려지는가.</summary>
        private static Point OnScreen(Grid content, Point p) => content.RenderTransform.Transform(p);

        [Fact]
        public void FitToContent_OnlyIfNeeded_KeepsHundredPercent_WhenContentFits()
        {
            RunSta(() =>
            {
                var (host, content) = Build(30, 30, 300, 200);
                host.FitToContent(onlyIfNeeded: true);

                Assert.Equal(1.0, host.Zoom, 3);
                Assert.Equal(new Point(30, 30), OnScreen(content, new Point(30, 30)));
            });
        }

        [Fact]
        public void FitToContent_ShrinksOverflowingContent_IntoView()
        {
            RunSta(() =>
            {
                // 종전 한 줄 템플릿 5개 폭(30 + 190×4 + 154 = 944)
                var (host, content) = Build(30, 30, 914, 60);
                host.FitToContent(onlyIfNeeded: true);

                Assert.True(host.Zoom < 1.0);
                var topLeft = OnScreen(content, new Point(30, 30));
                var bottomRight = OnScreen(content, new Point(944, 90));
                Assert.True(topLeft.X >= 0 && topLeft.Y >= 0, $"왼쪽 위 {topLeft}");
                Assert.True(bottomRight.X <= 700 && bottomRight.Y <= 600, $"오른쪽 아래 {bottomRight}");
            });
        }

        [Fact]
        public void FitToContent_NeverEnlargesBeyondHundredPercent()
        {
            RunSta(() =>
            {
                var (host, _) = Build(500, 500, 40, 20);   // 작은 콘텐츠 — [전체 보기] 로 거대해지면 안 된다
                host.FitToContent(onlyIfNeeded: false);
                Assert.Equal(1.0, host.Zoom, 3);
            });
        }

        [Fact]
        public void ZoomAround_KeepsAnchorPointFixed_AndClampsRange()
        {
            RunSta(() =>
            {
                var (host, content) = Build(0, 0, 400, 300);
                var anchor = new Point(200, 150);
                var before = content.RenderTransform.Inverse!.Transform(anchor);

                host.ZoomAround(anchor, 1.5);
                Assert.Equal(1.5, host.Zoom, 3);
                var after = OnScreen(content, before);
                Assert.Equal(anchor.X, after.X, 3);
                Assert.Equal(anchor.Y, after.Y, 3);

                for (int i = 0; i < 20; i++) host.ZoomAround(anchor, 2);
                Assert.Equal(ZoomPanHost.MaxZoom, host.Zoom, 3);
                for (int i = 0; i < 40; i++) host.ZoomAround(anchor, 0.5);
                Assert.Equal(ZoomPanHost.MinZoom, host.Zoom, 3);
            });
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
