using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VMS.Views
{
    public partial class SplashWindow : Window
    {
        private int _currentProgress;
        private const double ArcCenterX = 60;
        private const double ArcCenterY = 60;
        private const double ArcRadius = 57.5;
        private const double ProgressBarWidth = 280;

        public SplashWindow()
        {
            InitializeComponent();
        }

        public async Task LoadAsync()
        {
            await AnimateProgress(10, "Initializing system...", 400);
            await AnimateProgress(25, "Loading configuration...", 500);
            await AnimateProgress(40, "Initializing camera service...", 500);
            await AnimateProgress(55, "Loading vision tools...", 450);
            await AnimateProgress(70, "Preparing inspection engine...", 500);
            await AnimateProgress(85, "Loading recipes...", 400);
            await AnimateProgress(95, "Preparing workspace...", 400);
            await AnimateProgress(100, "Ready!", 300);
            await Task.Delay(300);
        }

        public async Task FadeOutAsync()
        {
            var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var tcs = new TaskCompletionSource();
            animation.Completed += (_, _) => tcs.SetResult();
            BeginAnimation(OpacityProperty, animation);
            await tcs.Task;
        }

        private async Task AnimateProgress(int targetPercent, string status, int durationMs)
        {
            StatusText.Text = status;
            int startPercent = _currentProgress;
            int delta = targetPercent - startPercent;
            if (delta <= 0) return;

            int delayPerStep = Math.Max(8, durationMs / delta);
            for (int i = startPercent + 1; i <= targetPercent; i++)
            {
                _currentProgress = i;
                UpdateProgressUI(i);
                await Task.Delay(delayPerStep);
            }
        }

        private void UpdateProgressUI(int percent)
        {
            ProgressFill.Width = ProgressBarWidth * percent / 100.0;
            PercentText.Text = $"{percent}%";
            UpdateCircularProgress(percent);
        }

        private void UpdateCircularProgress(double percentage)
        {
            if (percentage <= 0)
            {
                ProgressArc.Data = Geometry.Empty;
                return;
            }

            if (percentage >= 100)
            {
                ProgressArc.Data = new EllipseGeometry(
                    new Point(ArcCenterX, ArcCenterY), ArcRadius, ArcRadius);
                return;
            }

            double angle = percentage / 100.0 * 360.0;
            double startRad = -90.0 * Math.PI / 180.0;
            double endRad = (angle - 90.0) * Math.PI / 180.0;

            var startPoint = new Point(
                ArcCenterX + ArcRadius * Math.Cos(startRad),
                ArcCenterY + ArcRadius * Math.Sin(startRad));
            var endPoint = new Point(
                ArcCenterX + ArcRadius * Math.Cos(endRad),
                ArcCenterY + ArcRadius * Math.Sin(endRad));

            var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(ArcRadius, ArcRadius),
                IsLargeArc = angle > 180,
                SweepDirection = SweepDirection.Clockwise,
                IsStroked = true
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            ProgressArc.Data = geometry;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
