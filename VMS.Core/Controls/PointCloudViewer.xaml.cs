using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using VMS.Camera.Models;

namespace VMS.Core.Controls
{
    public partial class PointCloudViewer : UserControl
    {
        private DefaultEffectsManager? _effectsManager;

        // ── GPU dual-buffer geometry ──
        private readonly PointGeometry3D _geometryA = new();
        private readonly PointGeometry3D _geometryB = new();
        private bool _useBufferA = true;

        // ── Camera auto-fit state ──
        private bool _hasInitialFit;

        // ── LOD thresholds ──
        private const int LodThreshold4 = 2_000_000;
        private const int LodThreshold2 = 500_000;

        // ── Current LOD stride (for external use) ──
        private int _currentLodStride = 1;

        // ── Current data bounds (Z-axis = depth/height) ──
        private float _dataZMin;
        private float _dataZMax;

        // ── Mouse interaction state ──
        private bool _isPanning;
        private bool _isOrbiting;
        private Point _mouseStart;
        private Point3D _cameraStartPos;
        private Vector3D _cameraStartLook;
        private Vector3D _cameraStartUp;

        #region Dependency Properties

        public static readonly DependencyProperty PointCloudProperty =
            DependencyProperty.Register(
                nameof(PointCloud),
                typeof(PointCloudData),
                typeof(PointCloudViewer),
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
                typeof(PointCloudViewer),
                new PropertyMetadata(true, OnShowColorBarChanged));

        public bool ShowColorBar
        {
            get => (bool)GetValue(ShowColorBarProperty);
            set => SetValue(ShowColorBarProperty, value);
        }

        public static readonly DependencyProperty ShowStatusBarProperty =
            DependencyProperty.Register(
                nameof(ShowStatusBar),
                typeof(bool),
                typeof(PointCloudViewer),
                new PropertyMetadata(false, OnShowStatusBarChanged));

        public bool ShowStatusBar
        {
            get => (bool)GetValue(ShowStatusBarProperty);
            set => SetValue(ShowStatusBarProperty, value);
        }

        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register(
                nameof(StatusText),
                typeof(string),
                typeof(PointCloudViewer),
                new PropertyMetadata(string.Empty, OnStatusTextChanged));

        public string StatusText
        {
            get => (string)GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        #endregion

        #region Event

        /// <summary>
        /// Fired after point cloud rendering completes.
        /// Parameters: dataYMin, dataYMax, displayCount, totalCount
        /// </summary>
        public event Action<float, float, int, int>? PointCloudRendered;

        #endregion

        public PointCloudViewer()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_effectsManager == null)
            {
                _effectsManager = new DefaultEffectsManager();
                Viewport.EffectsManager = _effectsManager;
            }
            BuildGridLines();
            BuildAxisLines();

            // Wire mouse events on the viewport
            Viewport.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            Viewport.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            Viewport.PreviewMouseMove += OnPreviewMouseMove;
            Viewport.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            Viewport.PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
            Viewport.PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Viewport.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            Viewport.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
            Viewport.PreviewMouseMove -= OnPreviewMouseMove;
            Viewport.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            Viewport.PreviewMouseRightButtonUp -= OnPreviewMouseRightButtonUp;
            Viewport.PreviewMouseWheel -= OnPreviewMouseWheel;

            if (_effectsManager != null)
            {
                Viewport.EffectsManager = null;
                _effectsManager.Dispose();
                _effectsManager = null;
            }
        }

        #region Grid and Axis

        private void BuildGridLines()
        {
            var builder = new LineBuilder();
            float extent = 500f;
            float step = 50f;

            for (float i = -extent; i <= extent; i += step)
            {
                builder.AddLine(new Vector3(i, 0, -extent), new Vector3(i, 0, extent));
                builder.AddLine(new Vector3(-extent, 0, i), new Vector3(extent, 0, i));
            }

            GridLines.Geometry = builder.ToLineGeometry3D();
        }

        private void BuildAxisLines()
        {
            var builder = new LineBuilder();
            float len = 200f;

            builder.AddLine(Vector3.Zero, new Vector3(len, 0, 0));
            builder.AddLine(Vector3.Zero, new Vector3(0, len, 0));
            builder.AddLine(Vector3.Zero, new Vector3(0, 0, len));

            var geo = builder.ToLineGeometry3D();

            var axisColors = new Color4Collection
            {
                new Color4(1, 0, 0, 1), new Color4(1, 0, 0, 1),
                new Color4(0, 1, 0, 1), new Color4(0, 1, 0, 1),
                new Color4(0, 0, 1, 1), new Color4(0, 0, 1, 1)
            };
            geo.Colors = axisColors;

            AxisLines.Geometry = geo;
        }

        #endregion

        #region Mouse: Left Drag = Pan, Right Drag = Orbit, Wheel = Zoom

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseStart = e.GetPosition(Viewport);
            _cameraStartPos = MainCamera.Position;
            _cameraStartLook = MainCamera.LookDirection;
            _cameraStartUp = MainCamera.UpDirection;
            _isPanning = true;
            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                Viewport.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mouseStart = e.GetPosition(Viewport);
            _cameraStartPos = MainCamera.Position;
            _cameraStartLook = MainCamera.LookDirection;
            _cameraStartUp = MainCamera.UpDirection;
            _isOrbiting = true;
            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isOrbiting)
            {
                _isOrbiting = false;
                Viewport.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            var current = e.GetPosition(Viewport);
            double dx = current.X - _mouseStart.X;
            double dy = current.Y - _mouseStart.Y;

            if (_isPanning)
            {
                DoPan(dx, dy);
                e.Handled = true;
            }
            else if (_isOrbiting)
            {
                DoOrbit(dx, dy);
                e.Handled = true;
            }
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var look = MainCamera.LookDirection;
            double distance = look.Length;
            double zoomStep = distance * (e.Delta > 0 ? 0.15 : -0.15);

            var dir = look;
            dir.Normalize();

            MainCamera.Position = new Point3D(
                MainCamera.Position.X + dir.X * zoomStep,
                MainCamera.Position.Y + dir.Y * zoomStep,
                MainCamera.Position.Z + dir.Z * zoomStep);

            e.Handled = true;
        }

        private void DoPan(double dx, double dy)
        {
            var right = Vector3D.CrossProduct(_cameraStartLook, _cameraStartUp);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, _cameraStartLook);
            up.Normalize();

            double distance = _cameraStartLook.Length;
            double sensitivity = distance * 0.002;

            var offset = right * (-dx * sensitivity) + up * (dy * sensitivity);

            MainCamera.Position = new Point3D(
                _cameraStartPos.X + offset.X,
                _cameraStartPos.Y + offset.Y,
                _cameraStartPos.Z + offset.Z);
        }

        private void DoOrbit(double dx, double dy)
        {
            var target = _cameraStartPos + _cameraStartLook;
            var offset = _cameraStartPos - target;

            double yawDeg = -dx * 0.3;
            double pitchDeg = -dy * 0.3;

            var yawRotation = new AxisAngleRotation3D(_cameraStartUp, yawDeg);
            var yawTransform = new RotateTransform3D(yawRotation);
            offset = yawTransform.Transform(offset);

            var right = Vector3D.CrossProduct(_cameraStartLook, _cameraStartUp);
            right.Normalize();
            var pitchRotation = new AxisAngleRotation3D(right, pitchDeg);
            var pitchTransform = new RotateTransform3D(pitchRotation);
            offset = pitchTransform.Transform(offset);

            var newUp = pitchTransform.Transform(yawTransform.Transform(_cameraStartUp));

            MainCamera.Position = target + offset;
            MainCamera.LookDirection = target - MainCamera.Position;
            MainCamera.UpDirection = newUp;
        }

        #endregion

        #region Point Cloud Rendering

        private static void OnPointCloudChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointCloudViewer viewer)
            {
                viewer.UpdatePointCloud();
            }
        }

        private void UpdatePointCloud()
        {
            var data = PointCloud;
            if (data == null || data.PointCount == 0)
            {
                PointCloudModel.Geometry = null;
                PointCountText.Text = "No points";
                LodIndicator.Text = string.Empty;
                if (ShowColorBar)
                    ColorBarPanel.Visibility = Visibility.Collapsed;
                _hasInitialFit = false;
                return;
            }

            var positions = data.Positions;
            int totalCount = data.PointCount;

            // ── Dynamic LOD ──
            int lodStride = 1;
            if (totalCount > LodThreshold4) lodStride = 4;
            else if (totalCount > LodThreshold2) lodStride = 2;
            _currentLodStride = lodStride;

            int displayCount = 0;
            for (int i = 0; i < totalCount; i += lodStride)
                displayCount++;

            // ── Dual-buffer geometry ──
            _useBufferA = !_useBufferA;
            var geometry = _useBufferA ? _geometryA : _geometryB;

            var pointPositions = new Vector3Collection(displayCount);
            var pointColors = new Color4Collection(displayCount);
            var indices = new IntCollection(displayCount);

            // ── Single pass: positions + min/max (Z = depth/height) ──
            float zMin = float.MaxValue;
            float zMax = float.MinValue;
            int idx = 0;

            for (int i = 0; i < totalCount; i += lodStride)
            {
                var p = positions[i];
                if (p.Z < zMin) zMin = p.Z;
                if (p.Z > zMax) zMax = p.Z;
                pointPositions.Add(new Vector3(p.X, p.Y, p.Z));
                indices.Add(idx++);
            }

            _dataZMin = zMin;
            _dataZMax = zMax;

            // ── Color pass (Z-axis colormap) ──
            float range = zMax - zMin;
            for (int i = 0; i < totalCount; i += lodStride)
            {
                float t = range > 0.0001f ? (positions[i].Z - zMin) / range : 0.5f;
                pointColors.Add(JetColormap(t));
            }

            geometry.Positions = pointPositions;
            geometry.Colors = pointColors;
            geometry.Indices = indices;
            PointCloudModel.Geometry = geometry;

            if (ShowColorBar)
            {
                MinValueLabel.Text = zMin.ToString("F1");
                MaxValueLabel.Text = zMax.ToString("F1");
                ColorBarPanel.Visibility = Visibility.Visible;
            }

            PointCountText.Text = $"{displayCount:N0} / {totalCount:N0} pts";
            LodIndicator.Text = lodStride > 1 ? $"LOD x{lodStride}" : string.Empty;

            if (!_hasInitialFit)
            {
                _hasInitialFit = true;
                FitCameraToData(pointPositions);
            }

            PointCloudRendered?.Invoke(zMin, zMax, displayCount, totalCount);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Apply custom colors to the current point cloud (dual-buffer swap).
        /// </summary>
        public void ApplyColors(Color4Collection colors)
        {
            if (PointCloudModel.Geometry is not PointGeometry3D currentGeo || currentGeo.Positions == null)
                return;

            _useBufferA = !_useBufferA;
            var geometry = _useBufferA ? _geometryA : _geometryB;

            geometry.Positions = currentGeo.Positions;
            geometry.Indices = currentGeo.Indices;
            geometry.Colors = colors;
            PointCloudModel.Geometry = geometry;
        }

        /// <summary>
        /// Recolor the current point cloud with jet colormap using the specified Z range.
        /// </summary>
        public void RecolorWithRange(float zMin, float zMax)
        {
            var data = PointCloud;
            if (data == null || data.PointCount == 0) return;
            if (PointCloudModel.Geometry is not PointGeometry3D) return;

            var positions = data.Positions;
            int totalCount = data.PointCount;
            float range = zMax - zMin;

            int displayCount = 0;
            for (int i = 0; i < totalCount; i += _currentLodStride)
                displayCount++;

            var pointPositions = new Vector3Collection(displayCount);
            var pointColors = new Color4Collection(displayCount);
            var indices = new IntCollection(displayCount);

            int idx = 0;
            for (int i = 0; i < totalCount; i += _currentLodStride)
            {
                var p = positions[i];
                pointPositions.Add(new Vector3(p.X, p.Y, p.Z));
                float t = range > 0.0001f ? (p.Z - zMin) / range : 0.5f;
                pointColors.Add(JetColormap(t));
                indices.Add(idx++);
            }

            _useBufferA = !_useBufferA;
            var geometry = _useBufferA ? _geometryA : _geometryB;
            geometry.Positions = pointPositions;
            geometry.Colors = pointColors;
            geometry.Indices = indices;
            PointCloudModel.Geometry = geometry;
        }

        /// <summary>
        /// Reset camera to auto-fit the current data.
        /// </summary>
        public void ResetView()
        {
            _hasInitialFit = false;

            var geo = PointCloudModel.Geometry as PointGeometry3D;
            if (geo?.Positions != null && geo.Positions.Count > 0)
            {
                FitCameraToData(geo.Positions);
                _hasInitialFit = true;
            }
            else
            {
                MainCamera.Position = new Point3D(200, 200, 200);
                MainCamera.LookDirection = new Vector3D(-200, -200, -200);
                MainCamera.UpDirection = new Vector3D(0, 1, 0);
            }
        }

        /// <summary>
        /// Get the current data Z-axis (depth/height) bounds.
        /// </summary>
        public (float zMin, float zMax) GetDataBounds() => (_dataZMin, _dataZMax);

        /// <summary>
        /// Get the current LOD stride value.
        /// </summary>
        public int GetCurrentLodStride() => _currentLodStride;

        /// <summary>
        /// Jet colormap: maps [0,1] to blue→cyan→green→yellow→red.
        /// </summary>
        public static Color4 JetColormap(float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            float r, g, b;

            if (t < 0.25f)
            {
                float s = t / 0.25f;
                r = 0f; g = s; b = 1f;
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                r = 0f; g = 1f; b = 1f - s;
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                r = s; g = 1f; b = 0f;
            }
            else
            {
                float s = (t - 0.75f) / 0.25f;
                r = 1f; g = 1f - s; b = 0f;
            }

            return new Color4(r, g, b, 1f);
        }

        #endregion

        #region Camera Controls

        private void FitCameraToData(Vector3Collection positions)
        {
            if (positions.Count == 0) return;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            for (int i = 0; i < positions.Count; i++)
            {
                var p = positions[i];
                min = Vector3.Min(min, new Vector3(p.X, p.Y, p.Z));
                max = Vector3.Max(max, new Vector3(p.X, p.Y, p.Z));
            }

            var center = (min + max) * 0.5f;
            float distance = (max - min).Length() * 1.5f;

            MainCamera.Position = new Point3D(
                center.X + distance * 0.5,
                center.Y + distance * 0.5,
                center.Z + distance * 0.5);
            MainCamera.LookDirection = new Vector3D(
                center.X - MainCamera.Position.X,
                center.Y - MainCamera.Position.Y,
                center.Z - MainCamera.Position.Z);
            MainCamera.UpDirection = new Vector3D(0, 1, 0);
        }

        private (Vector3 center, float distance) GetCloudBounds()
        {
            if (PointCloud == null || PointCloud.PointCount == 0)
                return (Vector3.Zero, 300f);

            var positions = PointCloud.Positions;
            int count = PointCloud.PointCount;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }

            return ((min + max) * 0.5f, (max - min).Length() * 1.5f);
        }

        private void BtnResetView_Click(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void BtnTopView_Click(object sender, RoutedEventArgs e)
        {
            var (center, dist) = GetCloudBounds();
            MainCamera.Position = new Point3D(center.X, center.Y + dist, center.Z);
            MainCamera.LookDirection = new Vector3D(0, -1, 0);
            MainCamera.UpDirection = new Vector3D(0, 0, -1);
        }

        private void BtnFrontView_Click(object sender, RoutedEventArgs e)
        {
            var (center, dist) = GetCloudBounds();
            MainCamera.Position = new Point3D(center.X, center.Y, center.Z + dist);
            MainCamera.LookDirection = new Vector3D(0, 0, -1);
            MainCamera.UpDirection = new Vector3D(0, 1, 0);
        }

        private void BtnSideView_Click(object sender, RoutedEventArgs e)
        {
            var (center, dist) = GetCloudBounds();
            MainCamera.Position = new Point3D(center.X + dist, center.Y, center.Z);
            MainCamera.LookDirection = new Vector3D(-1, 0, 0);
            MainCamera.UpDirection = new Vector3D(0, 1, 0);
        }

        #endregion

        #region Property Changed Callbacks

        private static void OnShowColorBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointCloudViewer viewer && !(bool)e.NewValue)
            {
                viewer.ColorBarPanel.Visibility = Visibility.Collapsed;
            }
        }

        private static void OnShowStatusBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointCloudViewer viewer)
            {
                viewer.StatusBarPanel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void OnStatusTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointCloudViewer viewer)
            {
                viewer.StatusBarText.Text = (string)e.NewValue;
            }
        }

        #endregion
    }
}
