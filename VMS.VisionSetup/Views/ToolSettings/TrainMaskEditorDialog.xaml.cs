using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace VMS.VisionSetup.Views.ToolSettings
{
    /// <summary>
    /// FeatureMatch 학습 마스크 편집기 — 템플릿(학습 이미지) 위에 don't-care 영역을
    /// 도구(브러시/사각형/다각형) × 동작(마스크/해제)으로 칠하고 되돌린다
    /// (VisionPro PatMax 마스크 페인팅 대응). 그림자처럼 넓은 영역은 사각형/다각형,
    /// 본체 윤곽에 붙는 반사·가변 각인은 브러시로 정밀 지정. 정밀 작업은 Ctrl+휠 줌.
    /// 마스크는 템플릿과 같은 크기의 8UC1 (255=학습 제외).
    /// 코드 비하인드는 마우스 페인팅·줌(직접 UI 상호작용) 전담 — 비즈니스 로직 없음.
    /// </summary>
    public partial class TrainMaskEditorDialog : System.Windows.Window
    {
        private const double MaxViewWidth = 640;
        private const double MaxViewHeight = 480;
        private const double MinZoom = 1.0;
        private const double MaxZoom = 8.0;
        private const double ZoomStep = 1.25;

        private readonly Mat _templateBgr;
        private readonly Mat _mask;          // 8UC1, 255 = 제외
        private readonly double _scale;      // 화면 px / 이미지 px (줌 1배 기준)
        private readonly ScaleTransform _zoomTransform = new(1, 1);
        private double _zoom = 1.0;

        private bool _isPainting;
        private bool _strokeOutside;          // 버튼을 누른 채 캔버스 밖으로 나간 상태
        private int _lastStrokeTick;          // 마지막 페인팅 시각 (Environment.TickCount)

        /// <summary>마지막 페인팅 후 이 시간(ms)을 넘겨 도착한 이동은 같은 스트로크로 잇지 않는다 —
        /// 터치 승격에서 MouseUp 유실 후 다음 탭이 "버튼 눌림 상태의 MouseMove"로 먼저 도착하는
        /// 경로 차단 (실제 드래그의 이동 이벤트는 수십 ms 간격으로 연속됨).</summary>
        private const int StrokeGapMs = 400;
        private System.Windows.Point _lastImagePt;
        private System.Windows.Point _rectStartImagePt;
        private Rectangle? _rubberBand;

        /// <summary>한 이벤트에 이 거리(이미지 px)를 넘는 점프는 이전 점과 선으로 잇지 않는다 —
        /// 이벤트 유실·터치 승격 좌표 이상 등 어떤 경로로든 "이어 그리기"가 생기는 것을 물리적으로 차단.
        /// 실제 브러시 드래그의 이벤트 간 이동량은 이미지 대각선의 수 % 수준이라 오탐 없음.</summary>
        private double MaxStrokeJump =>
            0.4 * Math.Sqrt(_templateBgr.Width * _templateBgr.Width + _templateBgr.Height * _templateBgr.Height);

        // 다각형 도구: 이미지 좌표 꼭지점 누적 + 커서 추종 미리보기
        private readonly List<System.Windows.Point> _polyImagePts = new();
        private Polyline? _polyPreview;

        /// <summary>저장 시 결과 마스크 (소유권 호출자) — 취소면 null 유지.</summary>
        public Mat? ResultMask { get; private set; }

        public TrainMaskEditorDialog(Mat templateImage, Mat? existingMask)
        {
            InitializeComponent();

            _templateBgr = templateImage.Channels() == 1
                ? templateImage.CvtColor(ColorConversionCodes.GRAY2BGR)
                : templateImage.Clone();

            _mask = existingMask != null && !existingMask.Empty()
                && existingMask.Width == _templateBgr.Width && existingMask.Height == _templateBgr.Height
                ? existingMask.Clone()
                : new Mat(_templateBgr.Height, _templateBgr.Width, MatType.CV_8UC1, Scalar.Black);

            // 템플릿을 편집 영역에 맞춰 확대/축소 — 작은 템플릿은 키워서 칠하기 쉽게
            _scale = Math.Min(Math.Min(MaxViewWidth / _templateBgr.Width, MaxViewHeight / _templateBgr.Height), 6.0);
            double viewW = _templateBgr.Width * _scale;
            double viewH = _templateBgr.Height * _scale;
            TemplateView.Width = viewW; TemplateView.Height = viewH;
            OverlayView.Width = viewW; OverlayView.Height = viewH;
            EditArea.Width = viewW; EditArea.Height = viewH;

            // 줌해도 창 크기가 변하지 않도록 뷰포트를 1배 크기로 고정 — 넘치면 스크롤
            EditScroll.Width = viewW; EditScroll.Height = viewH;
            EditArea.LayoutTransform = _zoomTransform;

            TemplateView.Source = _templateBgr.ToBitmapSource();
            RenderOverlay();

            Closed += (_, _) => { _templateBgr.Dispose(); _mask.Dispose(); };
        }

        private void RenderOverlay()
        {
            using var overlay = new Mat(_mask.Height, _mask.Width, MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
            overlay.SetTo(new Scalar(0, 0, 255, 110), _mask);
            OverlayView.Source = overlay.ToBitmapSource();
        }

        private int BrushRadius => Math.Max(2, (int)(BrushSizeSlider.Value / 2));

        private bool IsUnmask => UnmaskActionBtn.IsChecked == true;

        /// <summary>현재 동작의 마스크 픽셀 값 — 마스크=255(제외), 해제=0(복원).</summary>
        private byte PaintValue => IsUnmask ? (byte)0 : (byte)255;

        /// <summary>미리보기 선 색 — 마스크는 주황, 해제는 하늘색으로 구분.</summary>
        private System.Windows.Media.Brush PreviewBrush =>
            IsUnmask ? System.Windows.Media.Brushes.DeepSkyBlue : System.Windows.Media.Brushes.OrangeRed;

        private System.Windows.Point ToImagePoint(System.Windows.Point viewPt) =>
            new(Math.Clamp(viewPt.X / _scale, 0, _templateBgr.Width - 1),
                Math.Clamp(viewPt.Y / _scale, 0, _templateBgr.Height - 1));

        private void PaintStroke(System.Windows.Point fromImg, System.Windows.Point toImg, byte value)
        {
            var color = new Scalar(value);
            var p1 = new OpenCvSharp.Point((int)fromImg.X, (int)fromImg.Y);
            var p2 = new OpenCvSharp.Point((int)toImg.X, (int)toImg.Y);
            Cv2.Line(_mask, p1, p2, color, BrushRadius * 2, LineTypes.Link8);
            Cv2.Circle(_mask, p2, BrushRadius, color, -1);
            RenderOverlay();
        }

        private void EditArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var viewPt = e.GetPosition(EditArea);
            var imgPt = ToImagePoint(viewPt);

            if (PolyModeBtn.IsChecked == true)
            {
                if (e.ClickCount >= 2) { ClosePolygon(); return; }
                _polyImagePts.Add(imgPt);
                UpdatePolygonPreview(imgPt);
                return;
            }

            // 이전 드래그가 MouseUp 유실(캡처 상실·터치 전환 등)로 남아 있으면 초기화 —
            // 이전 스트로크 끝점에서 새 클릭 지점까지 이어 그려지는 현상 방지
            CancelDrag();

            _isPainting = true;
            _strokeOutside = false;
            EditArea.CaptureMouse();

            if (RectModeBtn.IsChecked == true)
            {
                _rectStartImagePt = imgPt;
                _rubberBand = new Rectangle
                {
                    Stroke = PreviewBrush,
                    StrokeThickness = 1.5 / _zoom,
                    StrokeDashArray = new DoubleCollection { 4, 3 }
                };
                System.Windows.Controls.Canvas.SetLeft(_rubberBand, viewPt.X);
                System.Windows.Controls.Canvas.SetTop(_rubberBand, viewPt.Y);
                RubberBandCanvas.Children.Add(_rubberBand);
            }
            else
            {
                _lastImagePt = imgPt;
                PaintStroke(imgPt, imgPt, PaintValue);
                _lastStrokeTick = Environment.TickCount;
            }
        }

        private void EditArea_MouseMove(object sender, MouseEventArgs e)
        {
            var viewPt = e.GetPosition(EditArea);

            if (PolyModeBtn.IsChecked == true)
            {
                if (_polyImagePts.Count > 0)
                    UpdatePolygonPreview(ToImagePoint(viewPt));
                return;
            }

            if (!_isPainting) return;

            // 버튼이 떼진 채 이동 중이면 MouseUp 을 놓친 상태 — 즉시 종료 (이어 그리기 방지)
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelDrag();
                return;
            }

            // 우리 MouseDown 이 잡은 캡처가 살아있을 때만 페인팅 — MouseDown 이 유실된 채
            // 잔여 상태로 진입한 이동(유령 스트로크)을 원천 차단
            if (!EditArea.IsMouseCaptured)
            {
                CancelDrag();
                return;
            }

            var imgPt = ToImagePoint(viewPt);

            if (_rubberBand != null)
            {
                double x1 = _rectStartImagePt.X * _scale, y1 = _rectStartImagePt.Y * _scale;
                System.Windows.Controls.Canvas.SetLeft(_rubberBand, Math.Min(x1, viewPt.X));
                System.Windows.Controls.Canvas.SetTop(_rubberBand, Math.Min(y1, viewPt.Y));
                _rubberBand.Width = Math.Abs(viewPt.X - x1);
                _rubberBand.Height = Math.Abs(viewPt.Y - y1);
            }
            else
            {
                // 캔버스 밖에서는 칠하지 않는다 — 밖으로 나갔다 돌아오면 재진입 지점부터 새 시작
                // (기존에는 가장자리로 클램프되어 이탈 경로가 테두리를 따라 칠해졌음)
                bool inside = viewPt.X >= 0 && viewPt.Y >= 0
                    && viewPt.X < EditArea.Width && viewPt.Y < EditArea.Height;
                if (!inside)
                {
                    _strokeOutside = true;
                    return;
                }

                // 비정상적으로 먼 점프 또는 시간 공백 후 도착한 이동은 선으로 잇지 않고 새 시작점 처리
                double jump = (imgPt - _lastImagePt).Length;
                bool gap = Environment.TickCount - _lastStrokeTick > StrokeGapMs;
                if (_strokeOutside || gap || jump > MaxStrokeJump)
                {
                    _strokeOutside = false;
                    _lastImagePt = imgPt;
                    PaintStroke(imgPt, imgPt, PaintValue);
                    _lastStrokeTick = Environment.TickCount;
                    return;
                }

                PaintStroke(_lastImagePt, imgPt, PaintValue);
                _lastImagePt = imgPt;
                _lastStrokeTick = Environment.TickCount;
            }
        }

        private void EditArea_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPainting) return;
            _isPainting = false;
            EditArea.ReleaseMouseCapture();

            if (_rubberBand != null)
            {
                var imgPt = ToImagePoint(e.GetPosition(EditArea));
                RubberBandCanvas.Children.Remove(_rubberBand);
                _rubberBand = null;

                int x = (int)Math.Min(_rectStartImagePt.X, imgPt.X);
                int y = (int)Math.Min(_rectStartImagePt.Y, imgPt.Y);
                int w = (int)Math.Abs(imgPt.X - _rectStartImagePt.X);
                int h = (int)Math.Abs(imgPt.Y - _rectStartImagePt.Y);
                if (w > 0 && h > 0)
                {
                    Cv2.Rectangle(_mask, new OpenCvSharp.Rect(x, y, w, h), new Scalar(PaintValue), -1);
                    RenderOverlay();
                }
            }
        }

        private void EditArea_MouseRightDown(object sender, MouseButtonEventArgs e)
        {
            if (PolyModeBtn.IsChecked == true)
                ClosePolygon();
        }

        private void EditArea_LostMouseCapture(object sender, MouseEventArgs e)
        {
            // 다른 요소가 캡처를 가져가면 MouseUp 이 오지 않는다 — 진행 중 드래그 폐기
            if (!_isPainting) return;
            _isPainting = false;
            if (_rubberBand != null)
            {
                RubberBandCanvas.Children.Remove(_rubberBand);
                _rubberBand = null;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _polyImagePts.Count > 0)
            {
                CancelPolygon();
                e.Handled = true;
            }
        }

        /// <summary>도구/동작 라디오 전환 — 진행 중이던 드래그·다각형은 폐기.</summary>
        private void OnToolModeChanged(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;   // InitializeComponent 중 기본 체크 발화 무시
            CancelDrag();
            CancelPolygon();
        }

        private void CancelDrag()
        {
            if (!_isPainting && _rubberBand == null) return;
            _isPainting = false;
            EditArea.ReleaseMouseCapture();
            if (_rubberBand != null)
            {
                RubberBandCanvas.Children.Remove(_rubberBand);
                _rubberBand = null;
            }
        }

        private void UpdatePolygonPreview(System.Windows.Point cursorImgPt)
        {
            if (_polyPreview == null)
            {
                _polyPreview = new Polyline
                {
                    Stroke = PreviewBrush,
                    StrokeThickness = 1.5 / _zoom,
                    StrokeDashArray = new DoubleCollection { 4, 3 }
                };
                RubberBandCanvas.Children.Add(_polyPreview);
            }

            var pts = new PointCollection();
            foreach (var p in _polyImagePts)
                pts.Add(new System.Windows.Point(p.X * _scale, p.Y * _scale));
            pts.Add(new System.Windows.Point(cursorImgPt.X * _scale, cursorImgPt.Y * _scale));
            _polyPreview.Points = pts;
        }

        private void ClosePolygon()
        {
            if (_polyImagePts.Count >= 3)
            {
                var cvPts = _polyImagePts
                    .Select(p => new OpenCvSharp.Point((int)p.X, (int)p.Y))
                    .ToArray();
                Cv2.FillPoly(_mask, new[] { cvPts }, new Scalar(PaintValue));
                RenderOverlay();
            }
            CancelPolygon();
        }

        private void CancelPolygon()
        {
            _polyImagePts.Clear();
            if (_polyPreview != null)
            {
                RubberBandCanvas.Children.Remove(_polyPreview);
                _polyPreview = null;
            }
        }

        private void SetZoom(double newZoom, System.Windows.Point? anchorViewportPt = null)
        {
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - _zoom) < 0.001) return;

            // 앵커(뷰포트 좌표) 아래 콘텐츠 지점이 줌 후에도 같은 화면 위치에 오도록 보정
            double anchorX = anchorViewportPt?.X ?? EditScroll.ViewportWidth / 2;
            double anchorY = anchorViewportPt?.Y ?? EditScroll.ViewportHeight / 2;
            double contentX = EditScroll.HorizontalOffset + anchorX;
            double contentY = EditScroll.VerticalOffset + anchorY;
            double factor = newZoom / _zoom;

            _zoom = newZoom;
            _zoomTransform.ScaleX = _zoom;
            _zoomTransform.ScaleY = _zoom;
            ZoomLabel.Text = $"{_zoom * 100:0}%";

            EditScroll.UpdateLayout();   // 새 extent 반영 후 오프셋 적용
            EditScroll.ScrollToHorizontalOffset(contentX * factor - anchorX);
            EditScroll.ScrollToVerticalOffset(contentY * factor - anchorY);

            // 미리보기 선은 화면상 굵기가 일정하도록 역보정
            if (_rubberBand != null) _rubberBand.StrokeThickness = 1.5 / _zoom;
            if (_polyPreview != null) _polyPreview.StrokeThickness = 1.5 / _zoom;
        }

        private void OnZoomInClick(object sender, RoutedEventArgs e) => SetZoom(_zoom * ZoomStep);

        private void OnZoomOutClick(object sender, RoutedEventArgs e) => SetZoom(_zoom / ZoomStep);

        private void EditScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;   // 일반 휠은 스크롤
            e.Handled = true;
            SetZoom(_zoom * (e.Delta > 0 ? ZoomStep : 1 / ZoomStep), e.GetPosition(EditScroll));
        }

        private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 값은 BrushRadius 가 즉시 참조 — 별도 상태 없음 (핸들러는 XAML 바인딩 대용)
        }

        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            _mask.SetTo(Scalar.Black);
            RenderOverlay();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            ResultMask = _mask.Clone();
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
