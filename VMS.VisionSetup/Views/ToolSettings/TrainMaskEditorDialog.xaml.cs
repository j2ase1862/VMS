using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace VMS.VisionSetup.Views.ToolSettings
{
    /// <summary>
    /// FeatureMatch 학습 마스크 편집기 — 템플릿(학습 이미지) 위에 don't-care 영역을
    /// 브러시/사각형으로 칠하고 지우개로 되돌린다 (VisionPro PatMax 마스크 페인팅 대응).
    /// 그림자처럼 넓은 영역은 사각형, 본체 윤곽에 붙는 반사·가변 각인은 브러시로 정밀 지정.
    /// 마스크는 템플릿과 같은 크기의 8UC1 (255=학습 제외).
    /// 코드 비하인드는 마우스 페인팅(직접 UI 상호작용) 전담 — 비즈니스 로직 없음.
    /// </summary>
    public partial class TrainMaskEditorDialog : System.Windows.Window
    {
        private const double MaxViewWidth = 640;
        private const double MaxViewHeight = 480;

        private readonly Mat _templateBgr;
        private readonly Mat _mask;          // 8UC1, 255 = 제외
        private readonly double _scale;      // 화면 px / 이미지 px

        private bool _isPainting;
        private System.Windows.Point _lastImagePt;
        private System.Windows.Point _rectStartImagePt;
        private Rectangle? _rubberBand;

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
            _isPainting = true;
            EditArea.CaptureMouse();

            if (RectModeBtn.IsChecked == true)
            {
                _rectStartImagePt = imgPt;
                _rubberBand = new Rectangle
                {
                    Stroke = System.Windows.Media.Brushes.OrangeRed,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 3 }
                };
                System.Windows.Controls.Canvas.SetLeft(_rubberBand, viewPt.X);
                System.Windows.Controls.Canvas.SetTop(_rubberBand, viewPt.Y);
                RubberBandCanvas.Children.Add(_rubberBand);
            }
            else
            {
                _lastImagePt = imgPt;
                PaintStroke(imgPt, imgPt, EraserModeBtn.IsChecked == true ? (byte)0 : (byte)255);
            }
        }

        private void EditArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPainting) return;
            var viewPt = e.GetPosition(EditArea);
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
                PaintStroke(_lastImagePt, imgPt, EraserModeBtn.IsChecked == true ? (byte)0 : (byte)255);
                _lastImagePt = imgPt;
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
                    Cv2.Rectangle(_mask, new OpenCvSharp.Rect(x, y, w, h), new Scalar(255), -1);
                    RenderOverlay();
                }
            }
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
