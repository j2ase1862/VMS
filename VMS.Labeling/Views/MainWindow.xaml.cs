using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VMS.Core.Models.Annotation;
using VMS.Labeling.ViewModels;

namespace VMS.Labeling.Views
{
    public partial class MainWindow : System.Windows.Window
    {
        private bool _isDrawing;
        private System.Windows.Point _drawStart;
        private System.Windows.Shapes.Rectangle? _drawingRect;
        private LabelingMainViewModel? ViewModel => DataContext as LabelingMainViewModel;

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
                };
            }
        }

        private void UpdateImageDisplay()
        {
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

            // 이미지 좌표 → 캔버스 좌표 변환을 위한 스케일 계산
            double canvasW = AnnotationCanvas.ActualWidth;
            double canvasH = AnnotationCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return;

            double scaleX = canvasW / mat.Width;
            double scaleY = canvasH / mat.Height;
            double scale = Math.Min(scaleX, scaleY);
            double offsetX = (canvasW - mat.Width * scale) / 2;
            double offsetY = (canvasH - mat.Height * scale) / 2;

            foreach (var label in image.Labels)
            {
                var color = LabelingMainViewModel.GetClassColor(label.ClassName);
                var brush = new SolidColorBrush(color);
                brush.Opacity = 0.6;

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

                // Class name label
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

        #region Bounding Box Drawing

        private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.CurrentMat == null || ViewModel.CurrentImage == null) return;

            _isDrawing = true;
            _drawStart = e.GetPosition(AnnotationCanvas);

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

            // 최소 크기 체크 — 너무 작은 박스는 클릭으로 간주
            if (w < 10 || h < 10)
            {
                AnnotationCanvas.Children.Remove(_drawingRect);
                _drawingRect = null;
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

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RedrawAnnotations();
        }
    }
}
