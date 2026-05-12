using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VMS.Core.Models.Annotation;
using VMS.DeepLearning.ViewModels;

namespace VMS.DeepLearning.Views
{
    /// <summary>
    /// null이 아니면 Visible, null이면 Collapsed
    /// </summary>
    public class NotNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class MainWindow : System.Windows.Window
    {
        private bool _isDrawing;
        private System.Windows.Point _drawStart;
        private System.Windows.Shapes.Rectangle? _drawingRect;
        private LabelingMainViewModel? ViewModel => DataContext as LabelingMainViewModel;

        // Zoom/Pan state
        private double _zoomLevel = 1.0;
        private const double ZoomMin = 0.5;
        private const double ZoomMax = 20.0;
        private const double ZoomStep = 1.2;
        private bool _isPanning;
        private System.Windows.Point _panStart;
        private double _panStartX, _panStartY;

        /// <summary>
        /// 캔버스 좌표를 이미지 좌표로 변환. null이면 이미지 밖.
        /// </summary>
        private (double imgX, double imgY)? CanvasToImageCoords(System.Windows.Point canvasPoint)
        {
            var mat = ViewModel?.CurrentMat;
            if (mat == null || mat.IsDisposed || mat.Empty()) return null;

            double canvasW = AnnotationCanvas.ActualWidth;
            double canvasH = AnnotationCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return null;

            double scaleX = canvasW / mat.Width;
            double scaleY = canvasH / mat.Height;
            double scale = Math.Min(scaleX, scaleY);
            double offsetX = (canvasW - mat.Width * scale) / 2;
            double offsetY = (canvasH - mat.Height * scale) / 2;

            double imgX = (canvasPoint.X - offsetX) / scale;
            double imgY = (canvasPoint.Y - offsetY) / scale;

            if (imgX < 0 || imgX >= mat.Width || imgY < 0 || imgY >= mat.Height)
                return null;

            return (imgX, imgY);
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is LabelingMainViewModel vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.CurrentMat))
                        UpdateImageDisplay();
                    else if (args.PropertyName == nameof(vm.CurrentImage))
                        RedrawAnnotations();
                    else if (args.PropertyName == nameof(vm.CurrentSamPreviewPolygon))
                        RedrawAnnotations();
                };

                // v1 Inference: 예측 결과 변경 시 박스 다시 그리기
                vm.LatestPredictions.CollectionChanged += (_, _) => RedrawAnnotations();
            }
        }

        private void UpdateImageDisplay()
        {
            ResetZoomPan();
            var mat = ViewModel?.CurrentMat;
            if (mat != null && !mat.IsDisposed && !mat.Empty())
            {
                MainImage.Source = mat.ToBitmapSource();
                MainImage.Visibility = Visibility.Visible;
            }
            else
            {
                MainImage.Source = null;
                MainImage.Visibility = Visibility.Collapsed;
            }
            RedrawAnnotations();
        }

        private void RedrawAnnotations()
        {
            AnnotationCanvas.Children.Clear();

            var image = ViewModel?.CurrentImage;
            var mat = ViewModel?.CurrentMat;
            if (image == null || mat == null || mat.IsDisposed || mat.Empty()) return;

            double canvasW = AnnotationCanvas.ActualWidth;
            double canvasH = AnnotationCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return;

            // Classification/Anomaly 모드: 이미지 위에 클래스 표시 오버레이
            if (ViewModel != null && !ViewModel.IsBoundingBoxMode && !ViewModel.IsSegmentationMode)
            {
                DrawClassIndicatorOverlay(image, canvasW, canvasH);
                return;
            }

            double scaleX = canvasW / mat.Width;
            double scaleY = canvasH / mat.Height;
            double scale = Math.Min(scaleX, scaleY);
            double offsetX = (canvasW - mat.Width * scale) / 2;
            double offsetY = (canvasH - mat.Height * scale) / 2;

            foreach (var label in image.Labels)
            {
                var color = LabelingMainViewModel.GetClassColor(label.ClassName);

                if (label.LabelType == LabelType.Polygon && label.Points.Count >= 3)
                {
                    // Polygon rendering
                    DrawPolygon(label.Points, scale, offsetX, offsetY, color, label.Id, label.ClassName);
                }
                else
                {
                    // BoundingBox rendering
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
                        Width = label.BoundingBox.Width * scale,
                        Height = label.BoundingBox.Height * scale,
                        Tag = label.Id
                    };

                    Canvas.SetLeft(rect, offsetX + label.BoundingBox.X * scale);
                    Canvas.SetTop(rect, offsetY + label.BoundingBox.Y * scale);
                    AnnotationCanvas.Children.Add(rect);

                    var textBlock = new TextBlock
                    {
                        Text = label.ClassName,
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)),
                        FontSize = 11,
                        Padding = new Thickness(3, 1, 3, 1)
                    };
                    Canvas.SetLeft(textBlock, offsetX + label.BoundingBox.X * scale);
                    Canvas.SetTop(textBlock, offsetY + label.BoundingBox.Y * scale - 18);
                    AnnotationCanvas.Children.Add(textBlock);
                }
            }

            // SAM preview polygon (green dashed)
            if (ViewModel?.CurrentSamPreviewPolygon != null && ViewModel.CurrentSamPreviewPolygon.Count >= 3)
            {
                DrawPolygon(ViewModel.CurrentSamPreviewPolygon, scale, offsetX, offsetY,
                    Color.FromRgb(0, 255, 0), null, null, isDashed: true);
            }

            // SAM click points visualization
            if (ViewModel?.SamClickPoints != null)
            {
                foreach (var pt in ViewModel.SamClickPoints)
                {
                    double cx = offsetX + pt.X * scale;
                    double cy = offsetY + pt.Y * scale;
                    var dotColor = pt.Label == 1 ? Colors.LimeGreen : Colors.Red;

                    var dot = new Ellipse
                    {
                        Width = 10, Height = 10,
                        Fill = new SolidColorBrush(dotColor),
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5
                    };
                    Canvas.SetLeft(dot, cx - 5);
                    Canvas.SetTop(dot, cy - 5);
                    AnnotationCanvas.Children.Add(dot);
                }
            }

            // v1 Inference: 예측 박스 (시안 색, 어노테이션과 시각 구분)
            if (ViewModel?.LatestPredictions != null && ViewModel.LatestPredictions.Count > 0)
            {
                var predColor = Color.FromRgb(0, 220, 255); // Cyan
                foreach (var pred in ViewModel.LatestPredictions)
                {
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = new SolidColorBrush(predColor),
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(Color.FromArgb(20, predColor.R, predColor.G, predColor.B)),
                        Width = pred.Width * scale,
                        Height = pred.Height * scale,
                    };
                    Canvas.SetLeft(rect, offsetX + pred.X * scale);
                    Canvas.SetTop(rect, offsetY + pred.Y * scale);
                    AnnotationCanvas.Children.Add(rect);

                    var label = new TextBlock
                    {
                        Text = $"{pred.ClassName} {pred.Confidence:F2}",
                        Foreground = Brushes.Black,
                        Background = new SolidColorBrush(predColor),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Padding = new Thickness(3, 1, 3, 1)
                    };
                    Canvas.SetLeft(label, offsetX + pred.X * scale);
                    Canvas.SetTop(label, offsetY + pred.Y * scale - 18);
                    AnnotationCanvas.Children.Add(label);
                }
            }
        }

        private void DrawPolygon(
            System.Collections.Generic.List<OpenCvSharp.Point2d> points,
            double scale, double offsetX, double offsetY,
            Color color, string? tag, string? className, bool isDashed = false)
        {
            var polygon = new System.Windows.Shapes.Polygon
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = isDashed ? 2 : 2,
                Fill = new SolidColorBrush(Color.FromArgb(isDashed ? (byte)50 : (byte)30, color.R, color.G, color.B)),
                Tag = tag
            };

            if (isDashed)
                polygon.StrokeDashArray = new DoubleCollection { 4, 2 };

            foreach (var pt in points)
                polygon.Points.Add(new System.Windows.Point(offsetX + pt.X * scale, offsetY + pt.Y * scale));

            AnnotationCanvas.Children.Add(polygon);

            if (className != null && points.Count > 0)
            {
                double minX = points.Min(p => p.X);
                double minY = points.Min(p => p.Y);

                var textBlock = new TextBlock
                {
                    Text = className,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(180, color.R, color.G, color.B)),
                    FontSize = 11,
                    Padding = new Thickness(3, 1, 3, 1)
                };
                Canvas.SetLeft(textBlock, offsetX + minX * scale);
                Canvas.SetTop(textBlock, offsetY + minY * scale - 18);
                AnnotationCanvas.Children.Add(textBlock);
            }
        }

        private void DrawClassIndicatorOverlay(AnnotationImage image, double canvasW, double canvasH)
        {
            if (image.Labels.Count == 0) return;

            var label = image.Labels[0];
            var color = LabelingMainViewModel.GetClassColor(label.ClassName);

            // 이미지 위에 반투명 테두리 표시
            var border = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 4,
                Fill = Brushes.Transparent,
                Width = canvasW,
                Height = canvasH
            };
            Canvas.SetLeft(border, 0);
            Canvas.SetTop(border, 0);
            AnnotationCanvas.Children.Add(border);

            // 클래스 이름 배지
            var badge = new TextBlock
            {
                Text = label.ClassName.ToUpper(),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(8, 4, 8, 4)
            };
            Canvas.SetLeft(badge, 8);
            Canvas.SetTop(badge, 8);
            AnnotationCanvas.Children.Add(badge);
        }

        #region Bounding Box Drawing

        private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.CurrentMat == null || ViewModel.CurrentImage == null) return;

            // Segmentation mode: SAM foreground click
            if (ViewModel.IsSegmentationMode)
            {
                var coords = CanvasToImageCoords(e.GetPosition(AnnotationCanvas));
                if (coords.HasValue && ViewModel.IsSamModelLoaded)
                    _ = ViewModel.OnSamClickAsync(coords.Value.imgX, coords.Value.imgY, isBackground: false);
                return;
            }

            if (!ViewModel.IsBoundingBoxMode) return;

            var clickPos = e.GetPosition(AnnotationCanvas);

            // 기존 바운딩 박스 클릭 확인 — 히트 테스트
            var hitLabel = HitTestLabel(clickPos);
            if (hitLabel != null)
            {
                ViewModel.SelectedLabel = hitLabel;
                HighlightSelectedBox(hitLabel.Id);
                return;
            }

            // 빈 곳 클릭 — 새 박스 그리기 시작
            _isDrawing = true;
            _drawStart = clickPos;

            _drawingRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 0))
            };
            Canvas.SetLeft(_drawingRect, _drawStart.X);
            Canvas.SetTop(_drawingRect, _drawStart.Y);
            AnnotationCanvas.Children.Add(_drawingRect);

            AnnotationCanvas.CaptureMouse();
        }

        /// <summary>
        /// 클릭 위치에 해당하는 라벨을 찾습니다.
        /// </summary>
        private LabelInfo? HitTestLabel(System.Windows.Point canvasPoint)
        {
            if (ViewModel?.CurrentMat == null || ViewModel.CurrentImage == null) return null;

            var mat = ViewModel.CurrentMat;
            double canvasW = AnnotationCanvas.ActualWidth;
            double canvasH = AnnotationCanvas.ActualHeight;
            double scaleX = canvasW / mat.Width;
            double scaleY = canvasH / mat.Height;
            double scale = Math.Min(scaleX, scaleY);
            double offsetX = (canvasW - mat.Width * scale) / 2;
            double offsetY = (canvasH - mat.Height * scale) / 2;

            // 역순 순회 (위에 그려진 것 먼저)
            foreach (var label in ViewModel.CurrentImage.Labels.AsEnumerable().Reverse())
            {
                double left = offsetX + label.BoundingBox.X * scale;
                double top = offsetY + label.BoundingBox.Y * scale;
                double width = label.BoundingBox.Width * scale;
                double height = label.BoundingBox.Height * scale;

                if (canvasPoint.X >= left && canvasPoint.X <= left + width &&
                    canvasPoint.Y >= top && canvasPoint.Y <= top + height)
                {
                    return label;
                }
            }
            return null;
        }

        /// <summary>
        /// 선택된 박스를 시각적으로 강조합니다.
        /// </summary>
        private void HighlightSelectedBox(string labelId)
        {
            foreach (var child in AnnotationCanvas.Children.OfType<System.Windows.Shapes.Rectangle>())
            {
                if (child.Tag is string id && id == labelId)
                {
                    child.StrokeThickness = 3;
                    child.StrokeDashArray = new DoubleCollection { 4, 2 };
                }
                else if (child.Tag is string)
                {
                    child.StrokeThickness = 2;
                    child.StrokeDashArray = null;
                }
            }
        }

        private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || _drawingRect == null) return;

            var pos = e.GetPosition(AnnotationCanvas);
            double x = Math.Min(_drawStart.X, pos.X);
            double y = Math.Min(_drawStart.Y, pos.Y);
            double w = Math.Abs(pos.X - _drawStart.X);
            double h = Math.Abs(pos.Y - _drawStart.Y);

            Canvas.SetLeft(_drawingRect, x);
            Canvas.SetTop(_drawingRect, y);
            _drawingRect.Width = w;
            _drawingRect.Height = h;
        }

        private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing || _drawingRect == null) return;

            _isDrawing = false;
            AnnotationCanvas.ReleaseMouseCapture();

            double w = _drawingRect.Width;
            double h = _drawingRect.Height;

            // 최소 크기 체크 — 너무 작은 박스는 클릭으로 간주하여 선택 시도
            if (w < 10 || h < 10)
            {
                AnnotationCanvas.Children.Remove(_drawingRect);
                _drawingRect = null;
                var hitLabel = HitTestLabel(_drawStart);
                if (hitLabel != null && ViewModel != null)
                {
                    ViewModel.SelectedLabel = hitLabel;
                    HighlightSelectedBox(hitLabel.Id);
                }
                return;
            }

            // 캔버스 좌표 → 이미지 좌표 변환
            var mat = ViewModel?.CurrentMat;
            if (mat == null || mat.IsDisposed || mat.Empty())
            {
                AnnotationCanvas.Children.Remove(_drawingRect);
                _drawingRect = null;
                return;
            }

            double canvasW = AnnotationCanvas.ActualWidth;
            double canvasH = AnnotationCanvas.ActualHeight;
            double scaleX = canvasW / mat.Width;
            double scaleY = canvasH / mat.Height;
            double scale = Math.Min(scaleX, scaleY);
            double offsetX = (canvasW - mat.Width * scale) / 2;
            double offsetY = (canvasH - mat.Height * scale) / 2;

            double canvasLeft = Canvas.GetLeft(_drawingRect);
            double canvasTop = Canvas.GetTop(_drawingRect);

            int imgX = (int)((canvasLeft - offsetX) / scale);
            int imgY = (int)((canvasTop - offsetY) / scale);
            int imgW = (int)(w / scale);
            int imgH = (int)(h / scale);

            // 이미지 범위 클램프
            imgX = Math.Max(0, imgX);
            imgY = Math.Max(0, imgY);
            imgW = Math.Min(imgW, mat.Width - imgX);
            imgH = Math.Min(imgH, mat.Height - imgY);

            if (imgW > 2 && imgH > 2)
            {
                var boundingBox = new OpenCvSharp.Rect(imgX, imgY, imgW, imgH);
                ViewModel?.OnBoundingBoxCreated(boundingBox);
            }

            AnnotationCanvas.Children.Remove(_drawingRect);
            _drawingRect = null;
            RedrawAnnotations();
        }

        #endregion

        #region Zoom / Pan

        private void ImageBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Zoom at cursor position
            var cursorPos = e.GetPosition(ImageContainer);

            double oldZoom = _zoomLevel;
            if (e.Delta > 0)
                _zoomLevel = Math.Min(ZoomMax, _zoomLevel * ZoomStep);
            else
                _zoomLevel = Math.Max(ZoomMin, _zoomLevel / ZoomStep);

            double factor = _zoomLevel / oldZoom;

            ZoomTransform.ScaleX = _zoomLevel;
            ZoomTransform.ScaleY = _zoomLevel;

            // Adjust pan to keep cursor position stable
            PanTransform.X = cursorPos.X - factor * (cursorPos.X - PanTransform.X);
            PanTransform.Y = cursorPos.Y - factor * (cursorPos.Y - PanTransform.Y);

            e.Handled = true;
        }

        private void ImageBorder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (e.ClickCount == 2)
                {
                    // Double middle-click: reset zoom/pan
                    ResetZoomPan();
                    e.Handled = true;
                    return;
                }
                _isPanning = true;
                _panStart = e.GetPosition(ImageBorder);
                _panStartX = PanTransform.X;
                _panStartY = PanTransform.Y;
                ImageBorder.CaptureMouse();
                ImageBorder.Cursor = System.Windows.Input.Cursors.Hand;
                e.Handled = true;
            }
        }

        private void ImageBorder_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                ImageBorder.ReleaseMouseCapture();
                ImageBorder.Cursor = null;
                e.Handled = true;
            }
        }

        private void ImageBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;
            var pos = e.GetPosition(ImageBorder);
            PanTransform.X = _panStartX + (pos.X - _panStart.X);
            PanTransform.Y = _panStartY + (pos.Y - _panStart.Y);
        }

        private void ResetZoomPan()
        {
            _zoomLevel = 1.0;
            ZoomTransform.ScaleX = 1;
            ZoomTransform.ScaleY = 1;
            PanTransform.X = 0;
            PanTransform.Y = 0;
        }

        #endregion

        #region SAM Right-Click (Background)

        private void AnnotationCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || !ViewModel.IsSegmentationMode) return;
            if (!ViewModel.IsSamModelLoaded || ViewModel.CurrentMat == null) return;

            var coords = CanvasToImageCoords(e.GetPosition(AnnotationCanvas));
            if (coords.HasValue)
                _ = ViewModel.OnSamClickAsync(coords.Value.imgX, coords.Value.imgY, isBackground: true);
        }

        #endregion

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RedrawAnnotations();
        }
    }
}
