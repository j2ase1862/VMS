using VMS.VisionSetup.Models;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VMS.VisionSetup.Controls
{
    /// <summary>
    /// ROI 편집 모드
    /// </summary>
    public enum EditMode
    {
        Select,
        DrawRectangle,
        DrawRectangleAffine,
        DrawCircle,
        DrawEllipse,
        DrawPolygon
    }

    /// <summary>
    /// 이미지 캔버스 컨트롤 - ROI 그리기 및 편집 지원
    /// </summary>
    public partial class ImageCanvas : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty SourceImageProperty =
            DependencyProperty.Register(nameof(SourceImage), typeof(Mat), typeof(ImageCanvas),
                new PropertyMetadata(null, OnSourceImageChanged));

        public Mat? SourceImage
        {
            get => (Mat?)GetValue(SourceImageProperty);
            set => SetValue(SourceImageProperty, value);
        }

        public static readonly DependencyProperty ROICollectionProperty =
            DependencyProperty.Register(nameof(ROICollection), typeof(ObservableCollection<ROIShape>), typeof(ImageCanvas),
                new PropertyMetadata(null, OnROICollectionChanged));

        public ObservableCollection<ROIShape>? ROICollection
        {
            get => (ObservableCollection<ROIShape>?)GetValue(ROICollectionProperty);
            set => SetValue(ROICollectionProperty, value);
        }

        public static readonly DependencyProperty SelectedROIProperty =
            DependencyProperty.Register(nameof(SelectedROI), typeof(ROIShape), typeof(ImageCanvas),
                new PropertyMetadata(null, OnSelectedROIChanged));

        public ROIShape? SelectedROI
        {
            get => (ROIShape?)GetValue(SelectedROIProperty);
            set => SetValue(SelectedROIProperty, value);
        }

        public static readonly DependencyProperty IsToolbarVisibleProperty =
            DependencyProperty.Register(nameof(IsToolbarVisible), typeof(bool), typeof(ImageCanvas),
                new PropertyMetadata(true));

        public bool IsToolbarVisible
        {
            get => (bool)GetValue(IsToolbarVisibleProperty);
            set => SetValue(IsToolbarVisibleProperty, value);
        }

        public static readonly DependencyProperty IsROIEditingEnabledProperty =
            DependencyProperty.Register(nameof(IsROIEditingEnabled), typeof(bool), typeof(ImageCanvas),
                new PropertyMetadata(true, OnIsROIEditingEnabledChanged));

        public bool IsROIEditingEnabled
        {
            get => (bool)GetValue(IsROIEditingEnabledProperty);
            set => SetValue(IsROIEditingEnabledProperty, value);
        }

        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(ImageCanvas),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomLevelChanged));

        public double ZoomLevel
        {
            get => (double)GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, value);
        }

        #endregion

        #region Events

        public event EventHandler<ROIShape>? ROICreated;
        public event EventHandler<ROIShape>? ROIModified;
        public event EventHandler<ROIShape>? ROIDeleted;
        public event EventHandler<ROIShape?>? ROISelectionChanged;

        #endregion

        #region Fields

        private EditMode _currentMode = EditMode.Select;
        private bool _isDrawing;
        private bool _isDragging;
        private bool _isResizing;
        private System.Windows.Point _startPoint;
        private System.Windows.Point _lastMousePos;
        private HandleType _activeHandle = HandleType.None;
        private double _zoomLevel = 1.0;
        private bool _suppressZoomSync;

        // 임시 그리기용
        private Shape? _tempShape;
        private List<System.Windows.Point> _polygonPoints = new();

        // ROI별 시각적 요소 매핑
        private Dictionary<ROIShape, List<UIElement>> _roiVisuals = new();

        #endregion

        public ImageCanvas()
        {
            InitializeComponent();
            ROICollection = new ObservableCollection<ROIShape>();
        }

        #region Property Changed Handlers

        private static void OnSourceImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageCanvas canvas && e.NewValue is Mat mat)
            {
                canvas.UpdateDisplayImage(mat);
            }
        }

        private static void OnROICollectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageCanvas canvas)
            {
                canvas.RefreshAllROIs();
            }
        }

        private static void OnIsROIEditingEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageCanvas canvas && e.NewValue is bool enabled && !enabled)
            {
                // Disable: force Select mode, clear temp drawings, cancel any in-progress drawing
                canvas._currentMode = EditMode.Select;
                canvas._isDrawing = false;
                canvas._isDragging = false;
                canvas._isResizing = false;
                canvas.ClearTempDrawings();
                canvas._polygonPoints.Clear();
                if (canvas.IsLoaded && canvas.SelectToolBtn != null)
                    canvas.SelectToolBtn.IsChecked = true;
            }
        }

        private static void OnSelectedROIChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageCanvas canvas)
            {
                canvas.UpdateSelection(e.OldValue as ROIShape, e.NewValue as ROIShape);
            }
        }

        #endregion

        #region Image Display

        private void UpdateDisplayImage(Mat mat)
        {
            try
            {
                DisplayImage.Source = mat.ToBitmapSource();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"이미지 표시 오류: {ex.Message}";
            }
        }

        public void SetImage(Mat mat)
        {
            SourceImage = mat;
        }

        #endregion

        #region Mouse Event Handlers

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsROIEditingEnabled) return;

            var pos = e.GetPosition(DrawingCanvas);
            _startPoint = pos;
            _lastMousePos = pos;

            DrawingCanvas.CaptureMouse();

            switch (_currentMode)
            {
                case EditMode.Select:
                    HandleSelectMouseDown(pos);
                    break;

                case EditMode.DrawRectangle:
                case EditMode.DrawRectangleAffine:
                case EditMode.DrawCircle:
                case EditMode.DrawEllipse:
                    StartDrawing(pos);
                    break;

                case EditMode.DrawPolygon:
                    AddPolygonPoint(pos);
                    break;
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsROIEditingEnabled) return;

            var pos = e.GetPosition(DrawingCanvas);

            DrawingCanvas.ReleaseMouseCapture();

            if (_isDrawing && _currentMode != EditMode.DrawPolygon)
            {
                FinishDrawing(pos);
            }

            _isDragging = false;
            _isResizing = false;
            _activeHandle = HandleType.None;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(DrawingCanvas);
            CoordinateText.Text = $"X: {(int)pos.X}, Y: {(int)pos.Y}";

            if (!IsROIEditingEnabled) return;

            if (_isDrawing)
            {
                UpdateDrawingPreview(pos);
            }
            else if (_isDragging && SelectedROI != null)
            {
                double deltaX = pos.X - _lastMousePos.X;
                double deltaY = pos.Y - _lastMousePos.Y;
                SelectedROI.Move(deltaX, deltaY);
                RefreshROI(SelectedROI);
                ROIModified?.Invoke(this, SelectedROI);
            }
            else if (_isResizing && SelectedROI != null)
            {
                SelectedROI.ResizeByHandle(_activeHandle, pos);
                RefreshROI(SelectedROI);
                ROIModified?.Invoke(this, SelectedROI);
            }

            _lastMousePos = pos;
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsROIEditingEnabled) return;

            // 폴리곤 그리기 완료
            if (_currentMode == EditMode.DrawPolygon && _polygonPoints.Count >= 3)
            {
                FinishPolygonDrawing();
            }
            else
            {
                // 선택 해제
                SelectedROI = null;
                ROISelectionChanged?.Invoke(this, null);
            }
        }

        private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 마우스 커서 위치 기준 줌 (LayoutTransform이 적용되므로 이미지 좌표 반환)
            var imagePos = e.GetPosition(ZoomContainer);
            var viewportPos = e.GetPosition(ImageScrollViewer);

            // 배율 계산 (1.1배씩 확대/축소)
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newZoom = Math.Clamp(_zoomLevel * factor, 0.1, 5.0);

            SetZoom(newZoom);

            // 레이아웃 갱신 후 스크롤 오프셋 조정 (커서 위치 유지)
            ImageScrollViewer.UpdateLayout();
            ImageScrollViewer.ScrollToHorizontalOffset(imagePos.X * _zoomLevel - viewportPos.X);
            ImageScrollViewer.ScrollToVerticalOffset(imagePos.Y * _zoomLevel - viewportPos.Y);

            e.Handled = true;
        }

        #endregion

        #region Selection Handling

        private void HandleSelectMouseDown(System.Windows.Point pos)
        {
            // 핸들 클릭 확인
            if (SelectedROI != null)
            {
                var handles = SelectedROI.GetHandles();
                foreach (var kvp in handles)
                {
                    if (IsPointNearHandle(pos, kvp.Value))
                    {
                        _activeHandle = kvp.Key;
                        _isResizing = true;
                        return;
                    }
                }
            }

            // ROI 클릭 확인
            ROIShape? clickedROI = null;
            if (ROICollection != null)
            {
                // 역순으로 검사 (위에 있는 것 먼저)
                for (int i = ROICollection.Count - 1; i >= 0; i--)
                {
                    if (ROICollection[i].ContainsPoint(pos))
                    {
                        clickedROI = ROICollection[i];
                        break;
                    }
                }
            }

            if (clickedROI != null)
            {
                SelectedROI = clickedROI;
                _isDragging = true;
                ROISelectionChanged?.Invoke(this, clickedROI);
            }
            else
            {
                SelectedROI = null;
                ROISelectionChanged?.Invoke(this, null);
            }
        }

        private bool IsPointNearHandle(System.Windows.Point point, System.Windows.Point handle, double tolerance = 8)
        {
            return Math.Abs(point.X - handle.X) <= tolerance && Math.Abs(point.Y - handle.Y) <= tolerance;
        }

        private void UpdateSelection(ROIShape? oldROI, ROIShape? newROI)
        {
            if (oldROI != null)
            {
                oldROI.IsSelected = false;
                RefreshROI(oldROI);
            }

            if (newROI != null)
            {
                newROI.IsSelected = true;
                RefreshROI(newROI);
            }
        }

        #endregion

        #region Drawing

        private void StartDrawing(System.Windows.Point startPos)
        {
            _isDrawing = true;
            _startPoint = startPos;

            // 임시 도형 생성
            switch (_currentMode)
            {
                case EditMode.DrawRectangle:
                case EditMode.DrawRectangleAffine:
                    _tempShape = new Rectangle
                    {
                        Stroke = Brushes.Lime,
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 255, 0))
                    };
                    break;

                case EditMode.DrawCircle:
                    _tempShape = new Ellipse
                    {
                        Stroke = Brushes.Lime,
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 255, 0))
                    };
                    break;

                case EditMode.DrawEllipse:
                    _tempShape = new Ellipse
                    {
                        Stroke = Brushes.Cyan,
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 255, 255))
                    };
                    break;
            }

            if (_tempShape != null)
            {
                Canvas.SetLeft(_tempShape, startPos.X);
                Canvas.SetTop(_tempShape, startPos.Y);
                DrawingCanvas.Children.Add(_tempShape);
            }

            StatusText.Text = "드래그하여 ROI를 그리세요...";
        }

        private void UpdateDrawingPreview(System.Windows.Point currentPos)
        {
            if (_tempShape == null) return;

            double x = Math.Min(_startPoint.X, currentPos.X);
            double y = Math.Min(_startPoint.Y, currentPos.Y);
            double width = Math.Abs(currentPos.X - _startPoint.X);
            double height = Math.Abs(currentPos.Y - _startPoint.Y);

            if (_currentMode == EditMode.DrawCircle)
            {
                // 원은 중심에서 드래그
                double radius = Math.Sqrt(width * width + height * height);
                width = height = radius * 2;
                x = _startPoint.X - radius;
                y = _startPoint.Y - radius;
            }

            Canvas.SetLeft(_tempShape, x);
            Canvas.SetTop(_tempShape, y);
            _tempShape.Width = Math.Max(1, width);
            _tempShape.Height = Math.Max(1, height);
        }

        private void FinishDrawing(System.Windows.Point endPos)
        {
            _isDrawing = false;

            // 임시 도형 제거
            if (_tempShape != null)
            {
                DrawingCanvas.Children.Remove(_tempShape);
                _tempShape = null;
            }

            // 최소 크기 확인
            double width = Math.Abs(endPos.X - _startPoint.X);
            double height = Math.Abs(endPos.Y - _startPoint.Y);

            if (width < 10 && height < 10 && _currentMode != EditMode.DrawCircle)
            {
                StatusText.Text = "ROI가 너무 작습니다. 다시 그려주세요.";
                return;
            }

            // ROI 생성
            ROIShape? newROI = null;

            switch (_currentMode)
            {
                case EditMode.DrawRectangle:
                    newROI = new RectangleROI(
                        Math.Min(_startPoint.X, endPos.X),
                        Math.Min(_startPoint.Y, endPos.Y),
                        width, height);
                    newROI.Name = $"Rectangle_{ROICollection?.Count ?? 0 + 1}";
                    break;

                case EditMode.DrawRectangleAffine:
                    newROI = new RectangleAffineROI(
                        (_startPoint.X + endPos.X) / 2,
                        (_startPoint.Y + endPos.Y) / 2,
                        width, height, 0);
                    newROI.Name = $"RectAffine_{ROICollection?.Count ?? 0 + 1}";
                    break;

                case EditMode.DrawCircle:
                    double radius = Math.Sqrt(width * width + height * height);
                    if (radius < 10)
                    {
                        StatusText.Text = "원이 너무 작습니다. 다시 그려주세요.";
                        return;
                    }
                    newROI = new CircleROI(_startPoint.X, _startPoint.Y, radius);
                    newROI.Name = $"Circle_{ROICollection?.Count ?? 0 + 1}";
                    break;

                case EditMode.DrawEllipse:
                    newROI = new EllipseROI(
                        (_startPoint.X + endPos.X) / 2,
                        (_startPoint.Y + endPos.Y) / 2,
                        width / 2, height / 2, 0);
                    newROI.Name = $"Ellipse_{ROICollection?.Count ?? 0 + 1}";
                    break;
            }

            if (newROI != null)
            {
                AddROI(newROI);
                StatusText.Text = $"{newROI.Name} 생성됨";
            }

            // Select 모드로 복귀
            _currentMode = EditMode.Select;
            SelectToolBtn.IsChecked = true;
        }

        private void AddPolygonPoint(System.Windows.Point pos)
        {
            _polygonPoints.Add(pos);

            // 점 표시
            var ellipse = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.Yellow,
                Stroke = Brushes.Orange,
                StrokeThickness = 1
            };
            Canvas.SetLeft(ellipse, pos.X - 4);
            Canvas.SetTop(ellipse, pos.Y - 4);
            DrawingCanvas.Children.Add(ellipse);

            // 선 그리기
            if (_polygonPoints.Count > 1)
            {
                var prevPoint = _polygonPoints[_polygonPoints.Count - 2];
                var line = new Line
                {
                    X1 = prevPoint.X,
                    Y1 = prevPoint.Y,
                    X2 = pos.X,
                    Y2 = pos.Y,
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                DrawingCanvas.Children.Add(line);
            }

            StatusText.Text = $"폴리곤: {_polygonPoints.Count}개 점 (우클릭으로 완료)";
        }

        private void FinishPolygonDrawing()
        {
            if (_polygonPoints.Count >= 3)
            {
                var polygonROI = new PolygonROI(_polygonPoints);
                polygonROI.Name = $"Polygon_{ROICollection?.Count ?? 0 + 1}";
                AddROI(polygonROI);
                StatusText.Text = $"{polygonROI.Name} 생성됨";
            }

            // 임시 요소 제거
            ClearTempDrawings();
            _polygonPoints.Clear();

            // Select 모드로 복귀
            _currentMode = EditMode.Select;
            SelectToolBtn.IsChecked = true;
        }

        private void ClearTempDrawings()
        {
            var toRemove = DrawingCanvas.Children.OfType<UIElement>()
                .Where(e => e is Line || (e is Ellipse el && el.Fill == Brushes.Yellow))
                .ToList();

            foreach (var element in toRemove)
            {
                DrawingCanvas.Children.Remove(element);
            }
        }

        #endregion

        #region ROI Management

        public void AddROI(ROIShape roi)
        {
            ROICollection ??= new ObservableCollection<ROIShape>();
            ROICollection.Add(roi);
            DrawROI(roi);
            SelectedROI = roi;
            ROICreated?.Invoke(this, roi);
            ROISelectionChanged?.Invoke(this, roi);
        }

        public void RemoveROI(ROIShape roi)
        {
            if (ROICollection?.Contains(roi) == true)
            {
                ClearROIVisuals(roi);
                ROICollection.Remove(roi);
                ROIDeleted?.Invoke(this, roi);

                if (SelectedROI == roi)
                {
                    SelectedROI = null;
                    ROISelectionChanged?.Invoke(this, null);
                }
            }
        }

        private void DrawROI(ROIShape roi)
        {
            ClearROIVisuals(roi);

            var visuals = new List<UIElement>();
            var strokeBrush = new SolidColorBrush(roi.Color);
            var fillBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, roi.Color.R, roi.Color.G, roi.Color.B));

            Shape? shape = null;

            switch (roi)
            {
                case RectangleROI rect:
                    shape = new Rectangle
                    {
                        Width = rect.Width,
                        Height = rect.Height,
                        Stroke = strokeBrush,
                        StrokeThickness = roi.StrokeThickness,
                        Fill = fillBrush
                    };
                    Canvas.SetLeft(shape, rect.X);
                    Canvas.SetTop(shape, rect.Y);
                    break;

                case RectangleAffineROI rectAffine:
                    var polygon = new Polygon
                    {
                        Stroke = strokeBrush,
                        StrokeThickness = roi.StrokeThickness,
                        Fill = fillBrush
                    };
                    var corners = rectAffine.GetCorners();
                    polygon.Points = new PointCollection(corners);
                    visuals.Add(polygon);
                    DrawingCanvas.Children.Add(polygon);
                    break;

                case CircleROI circle:
                    shape = new Ellipse
                    {
                        Width = circle.Radius * 2,
                        Height = circle.Radius * 2,
                        Stroke = strokeBrush,
                        StrokeThickness = roi.StrokeThickness,
                        Fill = fillBrush
                    };
                    Canvas.SetLeft(shape, circle.CenterX - circle.Radius);
                    Canvas.SetTop(shape, circle.CenterY - circle.Radius);
                    break;

                case EllipseROI ellipse:
                    var ellipseShape = new Ellipse
                    {
                        Width = ellipse.RadiusX * 2,
                        Height = ellipse.RadiusY * 2,
                        Stroke = strokeBrush,
                        StrokeThickness = roi.StrokeThickness,
                        Fill = fillBrush,
                        RenderTransform = new RotateTransform(ellipse.Angle, ellipse.RadiusX, ellipse.RadiusY)
                    };
                    Canvas.SetLeft(ellipseShape, ellipse.CenterX - ellipse.RadiusX);
                    Canvas.SetTop(ellipseShape, ellipse.CenterY - ellipse.RadiusY);
                    shape = ellipseShape;
                    break;

                case PolygonROI polygonROI:
                    var poly = new Polygon
                    {
                        Stroke = strokeBrush,
                        StrokeThickness = roi.StrokeThickness,
                        Fill = fillBrush,
                        Points = new PointCollection(polygonROI.Points)
                    };
                    visuals.Add(poly);
                    DrawingCanvas.Children.Add(poly);
                    break;
            }

            if (shape != null)
            {
                visuals.Add(shape);
                DrawingCanvas.Children.Add(shape);
            }

            // 선택된 ROI면 핸들 그리기
            if (roi.IsSelected)
            {
                DrawHandles(roi, visuals);
            }

            _roiVisuals[roi] = visuals;
        }

        private void DrawHandles(ROIShape roi, List<UIElement> visuals)
        {
            var handles = roi.GetHandles();

            foreach (var kvp in handles)
            {
                var handle = new Ellipse
                {
                    Width = kvp.Key == HandleType.Rotate ? 12 : 10,
                    Height = kvp.Key == HandleType.Rotate ? 12 : 10,
                    Fill = kvp.Key == HandleType.Rotate ? Brushes.Gold : Brushes.White,
                    Stroke = kvp.Key == HandleType.Rotate ? Brushes.DarkOrange : Brushes.Lime,
                    StrokeThickness = 2,
                    Cursor = GetHandleCursor(kvp.Key)
                };

                Canvas.SetLeft(handle, kvp.Value.X - handle.Width / 2);
                Canvas.SetTop(handle, kvp.Value.Y - handle.Height / 2);

                visuals.Add(handle);
                DrawingCanvas.Children.Add(handle);
            }

            // 회전 핸들 연결선 (RectangleAffine, Ellipse)
            if (handles.ContainsKey(HandleType.Rotate) && handles.ContainsKey(HandleType.Center))
            {
                var line = new Line
                {
                    X1 = handles[HandleType.Center].X,
                    Y1 = handles[HandleType.Center].Y,
                    X2 = handles[HandleType.Rotate].X,
                    Y2 = handles[HandleType.Rotate].Y,
                    Stroke = Brushes.Gold,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                visuals.Add(line);
                DrawingCanvas.Children.Insert(DrawingCanvas.Children.Count - 1, line);
            }
        }

        private Cursor GetHandleCursor(HandleType handle)
        {
            return handle switch
            {
                HandleType.TopLeft or HandleType.BottomRight => Cursors.SizeNWSE,
                HandleType.TopRight or HandleType.BottomLeft => Cursors.SizeNESW,
                HandleType.Top or HandleType.Bottom => Cursors.SizeNS,
                HandleType.Left or HandleType.Right => Cursors.SizeWE,
                HandleType.Center => Cursors.SizeAll,
                HandleType.Rotate => Cursors.Hand,
                _ => Cursors.Arrow
            };
        }

        private void ClearROIVisuals(ROIShape roi)
        {
            if (_roiVisuals.TryGetValue(roi, out var visuals))
            {
                foreach (var visual in visuals)
                {
                    DrawingCanvas.Children.Remove(visual);
                }
                _roiVisuals.Remove(roi);
            }
        }

        private void RefreshROI(ROIShape roi)
        {
            DrawROI(roi);
        }

        private void RefreshAllROIs()
        {
            DrawingCanvas.Children.Clear();
            _roiVisuals.Clear();

            if (ROICollection != null)
            {
                foreach (var roi in ROICollection)
                {
                    DrawROI(roi);
                }
            }
        }

        #endregion

        #region Zoom

        private void SetZoom(double level)
        {
            _zoomLevel = Math.Clamp(level, 0.1, 5.0);
            ZoomTransform.ScaleX = _zoomLevel;
            ZoomTransform.ScaleY = _zoomLevel;
            if (ZoomLevelText != null)
                ZoomLevelText.Text = $"{(int)(_zoomLevel * 100)}%";

            if (!_suppressZoomSync)
            {
                _suppressZoomSync = true;
                ZoomLevel = _zoomLevel;
                _suppressZoomSync = false;
            }
        }

        private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageCanvas canvas && e.NewValue is double level && !canvas._suppressZoomSync)
            {
                canvas._suppressZoomSync = true;
                canvas.SetZoom(level);
                canvas._suppressZoomSync = false;
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_zoomLevel + 0.1);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_zoomLevel - 0.1);
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            if (SourceImage != null && SourceImage.Width > 0 && SourceImage.Height > 0)
            {
                double scaleX = ActualWidth / SourceImage.Width;
                double scaleY = ActualHeight / SourceImage.Height;
                SetZoom(Math.Min(scaleX, scaleY) * 0.9);
            }
            else
            {
                SetZoom(1.0);
            }
        }

        #endregion

        #region Tool Selection

        private void SelectTool_Checked(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.Select;
            if (!IsLoaded) return;
            ClearTempDrawings();
            _polygonPoints.Clear();
            if (StatusText != null) StatusText.Text = "선택 모드: ROI를 클릭하여 선택하세요";
        }

        private void RectangleTool_Checked(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.DrawRectangle;
            if (!IsLoaded) return;
            ClearTempDrawings();
            _polygonPoints.Clear();
            if (StatusText != null) StatusText.Text = "사각형 모드: 드래그하여 사각형 ROI를 그리세요";
        }

        private void RectAffineTool_Checked(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.DrawRectangleAffine;
            if (!IsLoaded) return;
            ClearTempDrawings();
            _polygonPoints.Clear();
            if (StatusText != null) StatusText.Text = "회전 사각형 모드: 드래그하여 회전 가능한 사각형 ROI를 그리세요";
        }

        private void CircleTool_Checked(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.DrawCircle;
            if (!IsLoaded) return;
            ClearTempDrawings();
            _polygonPoints.Clear();
            if (StatusText != null) StatusText.Text = "원 모드: 중심에서 드래그하여 원 ROI를 그리세요";
        }

        private void EllipseTool_Checked(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.DrawEllipse;
            if (!IsLoaded) return;
            ClearTempDrawings();
            _polygonPoints.Clear();
            if (StatusText != null) StatusText.Text = "타원 모드: 드래그하여 타원 ROI를 그리세요";
        }

        private void PolygonTool_Checked(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.DrawPolygon;
            if (!IsLoaded) return;
            ClearTempDrawings();
            _polygonPoints.Clear();
            if (StatusText != null) StatusText.Text = "다각형 모드: 클릭하여 점을 추가하고, 우클릭으로 완료하세요";
        }

        #endregion

        #region ROI Actions

        private void DeleteROI_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedROI != null)
            {
                RemoveROI(SelectedROI);
                StatusText.Text = "ROI가 삭제되었습니다";
            }
        }

        private void ClearAllROI_Click(object sender, RoutedEventArgs e)
        {
            if (ROICollection != null)
            {
                ROICollection.Clear();
                SelectedROI = null;
                _roiVisuals.Clear();
                DrawingCanvas.Children.Clear();
                StatusText.Text = "모든 ROI가 삭제되었습니다";
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 현재 선택된 ROI를 OpenCV Rect로 변환
        /// </summary>
        public OpenCvSharp.Rect? GetSelectedROIRect()
        {
            return SelectedROI?.GetBoundingRect();
        }

        /// <summary>
        /// 현재 선택된 ROI의 마스크 생성
        /// </summary>
        public Mat? GetSelectedROIMask(int width, int height)
        {
            return SelectedROI?.CreateMask(width, height);
        }

        /// <summary>
        /// 외부에서 그리기 모드 활성화 (Draw ROI 버튼용)
        /// </summary>
        public void ActivateDrawingMode(EditMode mode)
        {
            _currentMode = mode;

            // 툴바 라디오 버튼도 동기화
            switch (mode)
            {
                case EditMode.DrawRectangle:
                    RectangleToolBtn.IsChecked = true;
                    break;
                case EditMode.Select:
                    SelectToolBtn.IsChecked = true;
                    break;
            }

            if (StatusText != null)
                StatusText.Text = "드래그하여 ROI를 그리세요...";
        }

        /// <summary>
        /// 도구 전환 시 해당 도구의 ROI만 표시
        /// </summary>
        public void ShowToolROI(ROIShape? roi)
        {
            // 기존 ROI 시각 요소 모두 제거
            foreach (var kvp in _roiVisuals.ToList())
            {
                ClearROIVisuals(kvp.Key);
            }
            ROICollection?.Clear();

            if (roi != null)
            {
                ROICollection ??= new ObservableCollection<ROIShape>();
                ROICollection.Add(roi);
                DrawROI(roi);
                SelectedROI = roi;
            }
            else
            {
                SelectedROI = null;
            }
        }

        /// <summary>
        /// 도구 전환 시 ROI와 SearchRegion 동시 표시 (FeatureMatchTool용)
        /// AddROI()를 우회하여 ROICreated 이벤트를 발생시키지 않음
        /// </summary>
        public void ShowToolROIs(ROIShape? roi, ROIShape? searchRegion = null)
        {
            // 기존 ROI 시각 요소 모두 제거
            foreach (var kvp in _roiVisuals.ToList())
            {
                ClearROIVisuals(kvp.Key);
            }
            ROICollection?.Clear();

            ROICollection ??= new ObservableCollection<ROIShape>();

            if (roi != null)
            {
                ROICollection.Add(roi);
                DrawROI(roi);
            }

            if (searchRegion != null)
            {
                ROICollection.Add(searchRegion);
                DrawROI(searchRegion);
            }

            SelectedROI = roi;
        }

        /// <summary>
        /// 텍스트 필드 편집 후 단일 ROI 다시 그리기
        /// </summary>
        public void RefreshROIVisual(ROIShape? roi)
        {
            if (roi == null) return;
            RefreshROI(roi);
        }

        #endregion
    }
}
