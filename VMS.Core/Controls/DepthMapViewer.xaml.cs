using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VMS.Camera.Models;

namespace VMS.Core.Controls
{
    public partial class DepthMapViewer : UserControl
    {
        private Point _panStart;
        private double _translateStartX;
        private double _translateStartY;
        private bool _isPanning;

        // Actual data Z bounds (auto-calculated)
        private float _dataZMin;
        private float _dataZMax;

        // Thumb dragging state
        private bool _isDraggingMaxThumb;
        private bool _isDraggingMinThumb;
        private double _thumbDragStartY;
        private float _thumbDragStartValue;

        /// <summary>
        /// Fired after depth map rendering completes.
        /// Parameters: dataZMin, dataZMax
        /// </summary>
        public event Action<float, float>? DepthMapRendered;

        #region Dependency Properties

        public static readonly DependencyProperty PointCloudProperty =
            DependencyProperty.Register(
                nameof(PointCloud),
                typeof(PointCloudData),
                typeof(DepthMapViewer),
                new PropertyMetadata(null, OnPointCloudChanged));

        public PointCloudData? PointCloud
        {
            get => (PointCloudData?)GetValue(PointCloudProperty);
            set => SetValue(PointCloudProperty, value);
        }

        public static readonly DependencyProperty ShowColorBarProperty =
            DependencyProperty.Register(
                nameof(ShowColorBar),
                typeof(bool),
                typeof(DepthMapViewer),
                new PropertyMetadata(true, OnShowColorBarChanged));

        public bool ShowColorBar
        {
            get => (bool)GetValue(ShowColorBarProperty);
            set => SetValue(ShowColorBarProperty, value);
        }

        public static readonly DependencyProperty RangeMinProperty =
            DependencyProperty.Register(
                nameof(RangeMin),
                typeof(float),
                typeof(DepthMapViewer),
                new FrameworkPropertyMetadata(float.NaN,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnRangeChanged));

        /// <summary>
        /// Custom colormap range minimum. NaN = auto from data.
        /// </summary>
        public float RangeMin
        {
            get => (float)GetValue(RangeMinProperty);
            set => SetValue(RangeMinProperty, value);
        }

        public static readonly DependencyProperty RangeMaxProperty =
            DependencyProperty.Register(
                nameof(RangeMax),
                typeof(float),
                typeof(DepthMapViewer),
                new FrameworkPropertyMetadata(float.NaN,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnRangeChanged));

        /// <summary>
        /// Custom colormap range maximum. NaN = auto from data.
        /// </summary>
        public float RangeMax
        {
            get => (float)GetValue(RangeMaxProperty);
            set => SetValue(RangeMaxProperty, value);
        }

        public static readonly DependencyProperty DataMinProperty =
            DependencyProperty.Register(
                nameof(DataMin),
                typeof(float),
                typeof(DepthMapViewer),
                new PropertyMetadata(float.NaN, OnDataBoundsChanged));

        /// <summary>
        /// Overall data minimum Z value (sets thumb movement lower bound).
        /// </summary>
        public float DataMin
        {
            get => (float)GetValue(DataMinProperty);
            set => SetValue(DataMinProperty, value);
        }

        public static readonly DependencyProperty DataMaxProperty =
            DependencyProperty.Register(
                nameof(DataMax),
                typeof(float),
                typeof(DepthMapViewer),
                new PropertyMetadata(float.NaN, OnDataBoundsChanged));

        /// <summary>
        /// Overall data maximum Z value (sets thumb movement upper bound).
        /// </summary>
        public float DataMax
        {
            get => (float)GetValue(DataMaxProperty);
            set => SetValue(DataMaxProperty, value);
        }

        public static readonly DependencyProperty UseGrayColormapProperty =
            DependencyProperty.Register(
                nameof(UseGrayColormap),
                typeof(bool),
                typeof(DepthMapViewer),
                new PropertyMetadata(false, OnColormapChanged));

        /// <summary>
        /// true = 그레이스케일(가까울수록 밝음), false = Jet 컬러맵 (Mech-Eye Viewer 의
        /// Depth Map 컬러/그레이 표시 선택과 동일 개념).
        /// </summary>
        public bool UseGrayColormap
        {
            get => (bool)GetValue(UseGrayColormapProperty);
            set => SetValue(UseGrayColormapProperty, value);
        }

        private static void OnColormapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DepthMapViewer viewer)
            {
                viewer.UpdateGradientBar();
                viewer.RenderDepthMapWithRange(resetView: false);
            }
        }

        /// <summary>
        /// 컬러바 그라데이션을 현재 컬러맵에 맞게 갱신.
        /// 상단 = Max Z(원거리) — 기존 Jet 바(상단 파랑)와 동일 방향 유지:
        /// 픽셀 매핑이 t = 1 - (z - zMin)/range 라 zMax → t=0(파랑/검정), zMin → t=1(빨강/흰색).
        /// </summary>
        private void UpdateGradientBar()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            if (UseGrayColormap)
            {
                brush.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
                brush.GradientStops.Add(new GradientStop(Colors.White, 1.0));
            }
            else
            {
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x00, 0xFF), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xFF, 0xFF), 0.25));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xFF, 0x00), 0.5));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 0.75));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 1.0));
            }
            GradientRect.Fill = brush;
        }

        #endregion

        public DepthMapViewer()
        {
            InitializeComponent();

            DepthImage.MouseWheel += OnMouseWheel;
            DepthImage.MouseLeftButtonDown += OnMouseLeftButtonDown;
            DepthImage.MouseLeftButtonUp += OnMouseLeftButtonUp;
            DepthImage.MouseMove += OnMouseMove;
            DepthImage.MouseLeftButtonDown += OnMouseDoubleClick;
        }

        #region Rendering

        private static void OnPointCloudChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DepthMapViewer viewer)
                viewer.RenderDepthMap();
        }

        private void RenderDepthMap()
        {
            RenderDepthMapWithRange(resetView: true);
        }

        private void RenderDepthMapWithRange(bool resetView)
        {
            var data = PointCloud;
            if (data == null || data.PointCount == 0)
            {
                ShowPlaceholder("No Depth Data");
                return;
            }

            if (!data.IsOrganized)
            {
                ShowPlaceholder("Requires organized point cloud");
                return;
            }

            int width = data.GridWidth;
            int height = data.GridHeight;
            var positions = data.Positions;
            int pointCount = data.PointCount;

            // Calculate actual data Z range (exclude invalid points)
            float dataMin = float.MaxValue;
            float dataMax = float.MinValue;

            for (int i = 0; i < pointCount; i++)
            {
                var p = positions[i];
                if (p.Z == 0f || float.IsNaN(p.Z) || float.IsInfinity(p.Z)) continue;

                if (p.Z < dataMin) dataMin = p.Z;
                if (p.Z > dataMax) dataMax = p.Z;
            }

            if (dataMin >= dataMax)
            {
                dataMin = 0f;
                dataMax = 1f;
            }

            _dataZMin = dataMin;
            _dataZMax = dataMax;

            // Use custom range if set, otherwise use data bounds
            float zMin = float.IsNaN(RangeMin) ? dataMin : RangeMin;
            float zMax = float.IsNaN(RangeMax) ? dataMax : RangeMax;
            float range = zMax - zMin;
            if (range <= 0f) range = 1f;

            // Create WriteableBitmap (Bgr24)
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
            int stride = width * 3;
            byte[] pixels = new byte[stride * height];
            bool useGray = UseGrayColormap;

            for (int row = 0; row < height; row++)
            {
                int rowOffset = row * stride;
                for (int col = 0; col < width; col++)
                {
                    int idx = row * width + col;
                    int pixelOffset = rowOffset + col * 3;

                    if (idx >= pointCount)
                    {
                        pixels[pixelOffset] = 0;
                        pixels[pixelOffset + 1] = 0;
                        pixels[pixelOffset + 2] = 0;
                        continue;
                    }

                    var p = positions[idx];

                    // Invalid point (Z==0 means no depth) → black
                    if (p.Z == 0f || float.IsNaN(p.Z) || float.IsInfinity(p.Z))
                    {
                        pixels[pixelOffset] = 0;
                        pixels[pixelOffset + 1] = 0;
                        pixels[pixelOffset + 2] = 0;
                        continue;
                    }

                    // Out of range → black
                    if (p.Z < zMin || p.Z > zMax)
                    {
                        pixels[pixelOffset] = 0;
                        pixels[pixelOffset + 1] = 0;
                        pixels[pixelOffset + 2] = 0;
                        continue;
                    }

                    float t = 1f - (p.Z - zMin) / range;
                    byte b, g, r;
                    if (useGray)
                    {
                        // 그레이: 가까울수록(t→1) 밝게 — Mech-Eye Viewer 그레이 표시와 동일
                        byte v = (byte)(Math.Clamp(t, 0f, 1f) * 255f);
                        b = g = r = v;
                    }
                    else
                    {
                        JetColormapBgr(t, out b, out g, out r);
                    }
                    pixels[pixelOffset] = b;
                    pixels[pixelOffset + 1] = g;
                    pixels[pixelOffset + 2] = r;
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            bitmap.Freeze();

            DepthImage.Source = bitmap;
            DepthImage.Visibility = Visibility.Visible;
            PlaceholderPanel.Visibility = Visibility.Collapsed;

            // Update color bar labels to show data bounds
            float displayMax = !float.IsNaN(DataMax) ? DataMax : dataMax;
            float displayMin = !float.IsNaN(DataMin) ? DataMin : dataMin;
            MaxValueLabel.Text = displayMax.ToString("F1");
            MinValueLabel.Text = displayMin.ToString("F1");

            UpdateThumbPositions();

            if (resetView)
            {
                ResetTransform();
                DepthMapRendered?.Invoke(dataMin, dataMax);
            }
        }

        private void ShowPlaceholder(string message)
        {
            DepthImage.Source = null;
            DepthImage.Visibility = Visibility.Collapsed;
            PlaceholderText.Text = message;
            PlaceholderPanel.Visibility = Visibility.Visible;
            MaxValueLabel.Text = "--";
            MinValueLabel.Text = "--";
        }

        /// <summary>
        /// Jet colormap: maps [0,1] → BGR bytes. Blue→Cyan→Green→Yellow→Red.
        /// </summary>
        private static void JetColormapBgr(float t, out byte b, out byte g, out byte r)
        {
            t = Math.Clamp(t, 0f, 1f);

            float rf, gf, bf;

            if (t < 0.25f)
            {
                float s = t / 0.25f;
                rf = 0f; gf = s; bf = 1f;
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                rf = 0f; gf = 1f; bf = 1f - s;
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                rf = s; gf = 1f; bf = 0f;
            }
            else
            {
                float s = (t - 0.75f) / 0.25f;
                rf = 1f; gf = 1f - s; bf = 0f;
            }

            r = (byte)(rf * 255f);
            g = (byte)(gf * 255f);
            b = (byte)(bf * 255f);
        }

        #endregion

        #region Mouse: Wheel=Zoom, Left Drag=Pan, Double-click=Reset

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(DepthImage);
            // 기본 ×1.02/노치, Ctrl+휠 = ×1.01 정밀 줌 (사용자 요청 2026-08-05)
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1.01 : 1.02;
            double zoom = e.Delta > 0 ? step : 1.0 / step;

            double oldScale = ImageScale.ScaleX;
            double newScale = oldScale * zoom;
            newScale = Math.Clamp(newScale, 0.1, 50.0);

            // Zoom toward cursor position
            double relX = pos.X;
            double relY = pos.Y;

            ImageTranslate.X = relX - (relX - ImageTranslate.X) * (newScale / oldScale);
            ImageTranslate.Y = relY - (relY - ImageTranslate.Y) * (newScale / oldScale);

            ImageScale.ScaleX = newScale;
            ImageScale.ScaleY = newScale;

            e.Handled = true;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                return; // handled by double-click

            _panStart = e.GetPosition(this);
            _translateStartX = ImageTranslate.X;
            _translateStartY = ImageTranslate.Y;
            _isPanning = true;
            DepthImage.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                DepthImage.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;

            var current = e.GetPosition(this);
            ImageTranslate.X = _translateStartX + (current.X - _panStart.X);
            ImageTranslate.Y = _translateStartY + (current.Y - _panStart.Y);
            e.Handled = true;
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetTransform();
                e.Handled = true;
            }
        }

        private void ResetTransform()
        {
            ImageScale.ScaleX = 1.0;
            ImageScale.ScaleY = 1.0;
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
        }

        #endregion

        #region ShowColorBar / Range / DataBounds

        private static void OnShowColorBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DepthMapViewer viewer)
            {
                viewer.ColorBarBorder.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DepthMapViewer viewer && viewer.PointCloud != null)
            {
                viewer.RenderDepthMapWithRange(resetView: false);
            }
        }

        private static void OnDataBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DepthMapViewer viewer)
            {
                viewer.UpdateThumbPositions();

                // Update labels
                float displayMax = !float.IsNaN(viewer.DataMax) ? viewer.DataMax : viewer._dataZMax;
                float displayMin = !float.IsNaN(viewer.DataMin) ? viewer.DataMin : viewer._dataZMin;
                if (displayMax != float.MinValue) viewer.MaxValueLabel.Text = displayMax.ToString("F1");
                if (displayMin != float.MaxValue) viewer.MinValueLabel.Text = displayMin.ToString("F1");
            }
        }

        #endregion

        #region Thumb Drag Handlers

        /// <summary>
        /// Gets the effective data bounds for thumb positioning.
        /// Uses DataMin/DataMax DPs if set, otherwise falls back to auto-calculated bounds.
        /// </summary>
        private void GetEffectiveBounds(out float boundsMin, out float boundsMax)
        {
            boundsMin = !float.IsNaN(DataMin) ? DataMin : _dataZMin;
            boundsMax = !float.IsNaN(DataMax) ? DataMax : _dataZMax;
        }

        private void ThumbCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateThumbPositions();
        }

        private void UpdateThumbPositions()
        {
            double canvasHeight = ThumbCanvas.ActualHeight;
            if (canvasHeight <= 0) return;

            GetEffectiveBounds(out float boundsMin, out float boundsMax);
            float boundsRange = boundsMax - boundsMin;
            if (boundsRange <= 0f) return;

            float rangeMin = float.IsNaN(RangeMin) ? boundsMin : RangeMin;
            float rangeMax = float.IsNaN(RangeMax) ? boundsMax : RangeMax;

            // Z value → Y pixel: higher Z = top (Y=0), lower Z = bottom (Y=canvasHeight)
            double maxThumbY = (1.0 - (rangeMax - boundsMin) / boundsRange) * canvasHeight;
            double minThumbY = (1.0 - (rangeMin - boundsMin) / boundsRange) * canvasHeight;

            maxThumbY = Math.Clamp(maxThumbY, 0, canvasHeight);
            minThumbY = Math.Clamp(minThumbY, 0, canvasHeight);

            Canvas.SetTop(MaxThumbElement, maxThumbY);
            Canvas.SetTop(MinThumbElement, minThumbY);

            // Update dim overlays
            UpperDimOverlay.Height = Math.Max(0, maxThumbY);
            Canvas.SetTop(LowerDimOverlay, minThumbY);
            LowerDimOverlay.Height = Math.Max(0, canvasHeight - minThumbY);

            // Update thumb labels
            MaxThumbLabel.Text = rangeMax.ToString("F1");
            MinThumbLabel.Text = rangeMin.ToString("F1");
        }

        private void MaxThumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingMaxThumb = true;
            _thumbDragStartY = e.GetPosition(ThumbCanvas).Y;
            _thumbDragStartValue = float.IsNaN(RangeMax) ? _dataZMax : RangeMax;
            MaxThumbElement.CaptureMouse();
            e.Handled = true;
        }

        private void MinThumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingMinThumb = true;
            _thumbDragStartY = e.GetPosition(ThumbCanvas).Y;
            _thumbDragStartValue = float.IsNaN(RangeMin) ? _dataZMin : RangeMin;
            MinThumbElement.CaptureMouse();
            e.Handled = true;
        }

        private void Thumb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingMaxThumb)
            {
                _isDraggingMaxThumb = false;
                MaxThumbElement.ReleaseMouseCapture();
                e.Handled = true;
            }
            if (_isDraggingMinThumb)
            {
                _isDraggingMinThumb = false;
                MinThumbElement.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void MaxThumb_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingMaxThumb) return;

            double canvasHeight = ThumbCanvas.ActualHeight;
            if (canvasHeight <= 0) return;

            GetEffectiveBounds(out float boundsMin, out float boundsMax);
            float boundsRange = boundsMax - boundsMin;
            if (boundsRange <= 0f) return;

            double currentY = e.GetPosition(ThumbCanvas).Y;
            double deltaY = currentY - _thumbDragStartY;

            // Moving down = decreasing Z value (inverted axis)
            float deltaZ = -(float)(deltaY / canvasHeight) * boundsRange;
            float newValue = _thumbDragStartValue + deltaZ;

            // Clamp: must stay within bounds and above RangeMin
            float currentMin = float.IsNaN(RangeMin) ? boundsMin : RangeMin;
            newValue = Math.Clamp(newValue, currentMin, boundsMax);

            RangeMax = newValue;
            e.Handled = true;
        }

        private void MinThumb_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingMinThumb) return;

            double canvasHeight = ThumbCanvas.ActualHeight;
            if (canvasHeight <= 0) return;

            GetEffectiveBounds(out float boundsMin, out float boundsMax);
            float boundsRange = boundsMax - boundsMin;
            if (boundsRange <= 0f) return;

            double currentY = e.GetPosition(ThumbCanvas).Y;
            double deltaY = currentY - _thumbDragStartY;

            // Moving down = decreasing Z value (inverted axis)
            float deltaZ = -(float)(deltaY / canvasHeight) * boundsRange;
            float newValue = _thumbDragStartValue + deltaZ;

            // Clamp: must stay within bounds and below RangeMax
            float currentMax = float.IsNaN(RangeMax) ? boundsMax : RangeMax;
            newValue = Math.Clamp(newValue, boundsMin, currentMax);

            RangeMin = newValue;
            e.Handled = true;
        }

        #endregion
    }
}
