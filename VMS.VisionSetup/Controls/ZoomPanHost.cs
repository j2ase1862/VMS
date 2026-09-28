using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VMS.VisionSetup.Controls
{
    /// <summary>
    /// 도구 워크스페이스 확대/축소·이동 호스트.
    /// 자식(연결선·도구·임시선 캔버스를 담은 Grid)에 배율·이동 변환을 걸어, 폭이 고정된 워크스페이스에서도
    /// 넘친 도구와 연결선을 볼 수 있게 한다. 자식 좌표계는 그대로라 도구 X/Y·드래그·연결선 계산은 바뀌지 않는다
    /// (도구 드래그는 Canvas 기준 GetPosition 이라 변환이 자동으로 풀린다).
    ///
    /// 조작: 휠 = 마우스 위치 기준 확대/축소 · 빈 곳 왼쪽 드래그 또는 휠 버튼 드래그 = 이동 ·
    /// 명령 <see cref="ZoomInCommand"/>/<see cref="ZoomOutCommand"/>/<see cref="FitCommand"/>/<see cref="ResetCommand"/>.
    /// 배경(Background)이 빈 곳의 클릭·드롭을 받으므로 반드시 지정할 것.
    /// </summary>
    public class ZoomPanHost : Border
    {
        public const double MinZoom = 0.3;
        public const double MaxZoom = 2.0;
        private const double WheelFactor = 1.15;
        private const double FitMargin = 16;

        public static readonly RoutedUICommand ZoomInCommand = new("확대", nameof(ZoomInCommand), typeof(ZoomPanHost));
        public static readonly RoutedUICommand ZoomOutCommand = new("축소", nameof(ZoomOutCommand), typeof(ZoomPanHost));
        public static readonly RoutedUICommand FitCommand = new("전체 보기", nameof(FitCommand), typeof(ZoomPanHost));
        public static readonly RoutedUICommand ResetCommand = new("100%", nameof(ResetCommand), typeof(ZoomPanHost));

        private static readonly DependencyPropertyKey ZoomPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(Zoom), typeof(double), typeof(ZoomPanHost), new PropertyMetadata(1.0));

        /// <summary>현재 배율 (1 = 100%).</summary>
        public static readonly DependencyProperty ZoomProperty = ZoomPropertyKey.DependencyProperty;

        public double Zoom => (double)GetValue(ZoomProperty);

        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _translate = new();
        private readonly TransformGroup _transform = new();

        private bool _isPanning;
        private Point _panStartMouse;
        private Point _panStartOffset;
        private Cursor? _cursorBeforePan;

        public ZoomPanHost()
        {
            ClipToBounds = true;
            _transform.Children.Add(_scale);
            _transform.Children.Add(_translate);

            CommandBindings.Add(new CommandBinding(ZoomInCommand, (_, _) => ZoomAround(ViewCenter, WheelFactor)));
            CommandBindings.Add(new CommandBinding(ZoomOutCommand, (_, _) => ZoomAround(ViewCenter, 1 / WheelFactor)));
            CommandBindings.Add(new CommandBinding(FitCommand, (_, _) => FitToContent(onlyIfNeeded: false)));
            CommandBindings.Add(new CommandBinding(ResetCommand, (_, _) => SetView(1.0, new Vector())));
        }

        private Point ViewCenter => new(ActualWidth / 2, ActualHeight / 2);

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            if (visualRemoved is UIElement removed && ReferenceEquals(removed.RenderTransform, _transform))
                removed.RenderTransform = Transform.Identity;
            if (visualAdded is UIElement added)
            {
                added.RenderTransformOrigin = new Point(0, 0);
                added.RenderTransform = _transform;
            }
        }

        /// <summary>
        /// 도구·연결선이 모두 보이게 맞춘다. 100% 를 넘겨 키우지는 않는다(작은 체인이 거대해지지 않게).
        /// </summary>
        /// <param name="onlyIfNeeded">true 면 100%·원점에서 이미 다 보일 때 100%·원점으로 둔다.</param>
        public void FitToContent(bool onlyIfNeeded)
        {
            if (Child is not UIElement child || ActualWidth <= 0 || ActualHeight <= 0) return;

            var bounds = VisualTreeHelper.GetDescendantBounds(child);
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                SetView(1.0, new Vector());
                return;
            }

            bool fitsAtOrigin = bounds.Left >= 0 && bounds.Top >= 0
                                && bounds.Right <= ActualWidth && bounds.Bottom <= ActualHeight;
            if (onlyIfNeeded && fitsAtOrigin)
            {
                SetView(1.0, new Vector());
                return;
            }

            double availW = Math.Max(1, ActualWidth - 2 * FitMargin);
            double availH = Math.Max(1, ActualHeight - 2 * FitMargin);
            double zoom = Clamp(Math.Min(1.0, Math.Min(availW / bounds.Width, availH / bounds.Height)));

            // 가로는 가운데, 세로는 위쪽 여백에 붙인다 (층 배치가 위→아래로 흐르므로).
            double offsetX = (ActualWidth - bounds.Width * zoom) / 2 - bounds.Left * zoom;
            double offsetY = FitMargin - bounds.Top * zoom;
            SetView(zoom, new Vector(offsetX, offsetY));
        }

        /// <summary>호스트 좌표의 한 점을 고정한 채 배율을 factor 배 한다.</summary>
        public void ZoomAround(Point anchor, double factor)
        {
            double oldZoom = Zoom;
            double newZoom = Clamp(oldZoom * factor);
            if (Math.Abs(newZoom - oldZoom) < 1e-9) return;

            // anchor 아래의 콘텐츠 점이 그대로 anchor 에 남도록 이동량 보정
            var content = new Point((anchor.X - _translate.X) / oldZoom, (anchor.Y - _translate.Y) / oldZoom);
            SetView(newZoom, new Vector(anchor.X - content.X * newZoom, anchor.Y - content.Y * newZoom));
        }

        private void SetView(double zoom, Vector offset)
        {
            _scale.ScaleX = _scale.ScaleY = zoom;
            _translate.X = offset.X;
            _translate.Y = offset.Y;
            SetValue(ZoomPropertyKey, zoom);
        }

        private static double Clamp(double zoom) => Math.Clamp(zoom, MinZoom, MaxZoom);

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Handled) return;
            ZoomAround(e.GetPosition(this), e.Delta > 0 ? WheelFactor : 1 / WheelFactor);
            e.Handled = true;
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            // 휠 버튼 드래그는 도구 위에서도 이동
            if (e.ChangedButton == MouseButton.Middle && BeginPan(e))
                e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            // 왼쪽 드래그는 빈 곳(호스트 배경)에서만 — 도구 위는 도구 드래그가 가져간다
            if (!e.Handled && ReferenceEquals(e.OriginalSource, this) && BeginPan(e))
                e.Handled = true;
        }

        private bool BeginPan(MouseButtonEventArgs e)
        {
            if (_isPanning || !CaptureMouse()) return false;
            _isPanning = true;
            _panStartMouse = e.GetPosition(this);
            _panStartOffset = new Point(_translate.X, _translate.Y);
            _cursorBeforePan = Cursor;
            Cursor = Cursors.SizeAll;
            return true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isPanning) return;
            var delta = e.GetPosition(this) - _panStartMouse;
            SetView(Zoom, new Vector(_panStartOffset.X + delta.X, _panStartOffset.Y + delta.Y));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isPanning && e.ChangedButton is MouseButton.Left or MouseButton.Middle)
                EndPan();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_isPanning) EndPan();
        }

        private void EndPan()
        {
            _isPanning = false;
            Cursor = _cursorBeforePan;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }
    }
}
