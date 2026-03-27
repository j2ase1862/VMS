using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Collections.Generic;
using System.Linq;
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

        // ── Waypoints ──

        public static readonly DependencyProperty WaypointsProperty =
            DependencyProperty.Register(
                nameof(Waypoints),
                typeof(IEnumerable<VMS.Camera.Models.ScanWaypoint>),
                typeof(PointCloudViewer),
                new PropertyMetadata(null, OnWaypointsChanged));

        public IEnumerable<VMS.Camera.Models.ScanWaypoint>? Waypoints
        {
            get => (IEnumerable<VMS.Camera.Models.ScanWaypoint>?)GetValue(WaypointsProperty);
            set => SetValue(WaypointsProperty, value);
        }

        public static readonly DependencyProperty ActiveWaypointIndexProperty =
            DependencyProperty.Register(
                nameof(ActiveWaypointIndex),
                typeof(int),
                typeof(PointCloudViewer),
                new PropertyMetadata(-1, OnWaypointsChanged));

        public int ActiveWaypointIndex
        {
            get => (int)GetValue(ActiveWaypointIndexProperty);
            set => SetValue(ActiveWaypointIndexProperty, value);
        }

        private static void OnWaypointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointCloudViewer viewer)
                viewer.RenderWaypoints();
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

            // Build axis labels (viewport Y = data Z height, viewport Z = data Y)
            BuildAxisLabel(AxisLabelX, "X", new Vector3(len * 1.1f, 0, 0), new Color4(1, 0, 0, 1));
            BuildAxisLabel(AxisLabelY, "Z(mm)", new Vector3(0, len * 1.1f, 0), new Color4(0, 1, 0, 1));
            BuildAxisLabel(AxisLabelZ, "Y(mm)", new Vector3(0, 0, len * 1.1f), new Color4(0, 0.5f, 1, 1));
        }

        private static void BuildAxisLabel(BillboardTextModel3D model, string text, Vector3 position, Color4 color)
        {
            var billboard = new BillboardSingleText3D()
            {
                FontColor = color,
                BackgroundColor = new Color4(0, 0, 0, 0),
                FontSize = 14,
                FontWeight = SharpDX.DirectWrite.FontWeight.Bold,
            };
            billboard.TextInfo = new TextInfo(text, position);
            model.Geometry = billboard;
        }

        private void BuildGridTickLabels(Vector3Collection positions)
        {
            if (positions.Count == 0)
            {
                GridTickLabels.Geometry = null;
                return;
            }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < positions.Count; i++)
            {
                min = Vector3.Min(min, new Vector3(positions[i].X, positions[i].Y, positions[i].Z));
                max = Vector3.Max(max, new Vector3(positions[i].X, positions[i].Y, positions[i].Z));
            }

            var tickText = new BillboardText3D();
            var tickColor = new Color4(0.6f, 0.6f, 0.6f, 1f);
            int tickCount = 5;

            // X-axis ticks (along grid X, at Y=0, Z=0 edge)
            float gridXMax = max.X - min.X;
            float gridZMax = max.Z - min.Z;
            for (int i = 0; i <= tickCount; i++)
            {
                float x = gridXMax * i / tickCount;
                float dataX = min.X + (max.X - min.X) * i / tickCount;
                tickText.TextInfo.Add(new TextInfo(
                    dataX.ToString("F0"), new Vector3(x, 0f, -20f))
                { Foreground = tickColor, Scale = 0.6f });
            }

            // Z-axis ticks (along grid Z = data Y, at Y=0, X=0 edge)
            for (int i = 0; i <= tickCount; i++)
            {
                float z = gridZMax * i / tickCount;
                float dataY = min.Z + (max.Z - min.Z) * i / tickCount;
                tickText.TextInfo.Add(new TextInfo(
                    dataY.ToString("F0"), new Vector3(-20f, 0f, z))
                { Foreground = tickColor, Scale = 0.6f });
            }

            // Y-axis ticks (vertical, show original data Z values)
            // viewport Y = dataZ - zMax, so dataZ = viewportY + zMax
            for (int i = 0; i <= tickCount; i++)
            {
                float viewportY = min.Y + (max.Y - min.Y) * i / tickCount;
                float dataZ = viewportY + _dataZMax;
                tickText.TextInfo.Add(new TextInfo(
                    dataZ.ToString("F1"), new Vector3(-20f, viewportY, 0f))
                { Foreground = tickColor, Scale = 0.6f });
            }

            GridTickLabels.Geometry = tickText;

            // Update grid to match data bounds
            RebuildGridForData(min, max);
        }

        private void RebuildGridForData(Vector3 min, Vector3 max)
        {
            var builder = new LineBuilder();
            int divisions = 10;

            // Grid top-left corner at (0, 0, 0), extends in +X and +Z
            float gridXMax = max.X - min.X;
            float gridZMax = max.Z - min.Z;
            float xStep = gridXMax / divisions;
            float zStep = gridZMax / divisions;

            for (int i = 0; i <= divisions; i++)
            {
                float x = xStep * i;
                builder.AddLine(new Vector3(x, 0f, 0f), new Vector3(x, 0f, gridZMax));
            }
            for (int i = 0; i <= divisions; i++)
            {
                float z = zStep * i;
                builder.AddLine(new Vector3(0f, 0f, z), new Vector3(gridXMax, 0f, z));
            }

            GridLines.Geometry = builder.ToLineGeometry3D();
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

            // ── Pass 1: Calculate Z bounds ──
            float zMin = float.MaxValue;
            float zMax = float.MinValue;

            for (int i = 0; i < totalCount; i += lodStride)
            {
                var p = positions[i];
                if (p.Z == 0f || float.IsNaN(p.Z) || float.IsInfinity(p.Z)) continue;
                if (p.Z < zMin) zMin = p.Z;
                if (p.Z > zMax) zMax = p.Z;
            }

            if (zMin >= zMax) { zMin = 0f; zMax = 1f; }
            _dataZMin = zMin;
            _dataZMax = zMax;
            float range = zMax - zMin;

            // ── Pass 2: Build positions + colors ──
            // Viewport Y = p.Z - zMax → grid(Y=0) at top, data hangs below (negative Y)
            int idx = 0;
            for (int i = 0; i < totalCount; i += lodStride)
            {
                var p = positions[i];
                if (p.Z == 0f || float.IsNaN(p.Z) || float.IsInfinity(p.Z)) continue;

                float viewportY = p.Z - zMax;
                pointPositions.Add(new Vector3(p.X, viewportY, p.Y));

                float t = range > 0.0001f ? 1f - (p.Z - zMin) / range : 0.5f;
                pointColors.Add(JetColormap(t));
                indices.Add(idx++);
            }

            displayCount = idx;

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

            BuildGridTickLabels(pointPositions);

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

                // Skip invalid points: Z==0 (no depth) or NaN/Infinity
                if (p.Z == 0f || float.IsNaN(p.Z) || float.IsInfinity(p.Z)) continue;

                // Grid(Y=0) at top, data hangs below (negative Y)
                float viewportY = p.Z - _dataZMax;
                pointPositions.Add(new Vector3(p.X, viewportY, p.Y));
                float t = range > 0.0001f ? 1f - (p.Z - zMin) / range : 0.5f;
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
                MainCamera.UpDirection = new Vector3D(0, -1, 0);
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
            MainCamera.UpDirection = new Vector3D(0, -1, 0);
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
            MainCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void BtnFrontView_Click(object sender, RoutedEventArgs e)
        {
            var (center, dist) = GetCloudBounds();
            MainCamera.Position = new Point3D(center.X, center.Y, center.Z + dist);
            MainCamera.LookDirection = new Vector3D(0, 0, -1);
            MainCamera.UpDirection = new Vector3D(0, -1, 0);
        }

        private void BtnSideView_Click(object sender, RoutedEventArgs e)
        {
            var (center, dist) = GetCloudBounds();
            MainCamera.Position = new Point3D(center.X + dist, center.Y, center.Z);
            MainCamera.LookDirection = new Vector3D(-1, 0, 0);
            MainCamera.UpDirection = new Vector3D(0, -1, 0);
        }

        #endregion

        #region Waypoint Rendering

        /// <summary>
        /// 웨이포인트를 3D 뷰포트에 렌더링 (카메라 위치 + 방향 화살표 + 라벨)
        /// </summary>
        public void RenderWaypoints()
        {
            // 기존 웨이포인트 모델 제거
            ClearWaypointModels();

            var waypoints = Waypoints;
            if (waypoints == null) return;

            int activeIdx = ActiveWaypointIndex;

            foreach (var wp in waypoints)
            {
                // 좌표 변환 (Data 좌표계 → Viewport 좌표계: Y↔Z swap, Y negate)
                var vpPos = DataToViewport(wp.CameraPosition);
                var vpTarget = DataToViewport(wp.LookAtTarget);
                var dir = vpTarget - vpPos;
                float dirLen = dir.Length();
                if (dirLen > 1e-3f) dir /= dirLen;

                // 색상 결정
                Color4 color = wp.IsCompleted ? new Color4(0.2f, 1f, 0.2f, 1f)   // LimeGreen
                    : wp.Index == activeIdx ? new Color4(1f, 1f, 0f, 1f)         // Yellow
                    : new Color4(0f, 1f, 1f, 1f);                                // Cyan

                var mediaColor = System.Windows.Media.Color.FromScRgb(1f, color.Red, color.Green, color.Blue);

                // 1. 카메라 위치 점
                var wpPointGeo = new PointGeometry3D();
                wpPointGeo.Positions = new Vector3Collection(new[]
                {
                    new Vector3(vpPos.X, vpPos.Y, vpPos.Z)
                });
                var wpPointModel = new PointGeometryModel3D
                {
                    Geometry = wpPointGeo,
                    Color = mediaColor,
                    Size = new System.Windows.Size(12, 12),
                    Tag = "Waypoint"
                };
                Viewport.Items.Add(wpPointModel);

                // 2. 방향 화살표 (Position → LookAt)
                float defaultArrowLen = 30f;
                float arrowLen = dirLen > 1f ? MathF.Min(dirLen * 0.5f, 80f) : defaultArrowLen;
                var arrowEnd = vpPos + dir * arrowLen;

                var arrowGeo = new LineGeometry3D();
                var arrowPositions = new Vector3Collection();
                var arrowIndices = new IntCollection();

                arrowPositions.Add(new Vector3(vpPos.X, vpPos.Y, vpPos.Z));
                arrowPositions.Add(new Vector3(arrowEnd.X, arrowEnd.Y, arrowEnd.Z));
                arrowIndices.Add(0);
                arrowIndices.Add(1);

                arrowGeo.Positions = arrowPositions;
                arrowGeo.Indices = arrowIndices;

                var arrowModel = new LineGeometryModel3D
                {
                    Geometry = arrowGeo,
                    Color = mediaColor,
                    Thickness = wp.Index == activeIdx ? 3.0 : 1.5,
                    Tag = "Waypoint"
                };
                Viewport.Items.Add(arrowModel);

                // 3. 라벨 (번호)
                var labelGeo = new BillboardText3D();
                labelGeo.TextInfo.Add(new TextInfo(
                    $" {wp.Index + 1}",
                    new Vector3(vpPos.X, vpPos.Y - 5, vpPos.Z))
                {
                    Foreground = color,
                    Scale = 1.0f
                });
                var labelModel = new BillboardTextModel3D
                {
                    Geometry = labelGeo,
                    FixedSize = true,
                    Tag = "Waypoint"
                };
                Viewport.Items.Add(labelModel);
            }
        }

        /// <summary>기존 웨이포인트 모델 모두 제거</summary>
        private void ClearWaypointModels()
        {
            var toRemove = Viewport.Items
                .OfType<Element3D>()
                .Where(e => e.Tag is string tag && tag == "Waypoint")
                .ToList();

            foreach (var item in toRemove)
                Viewport.Items.Remove(item);
        }

        /// <summary>웨이포인트 표시 갱신 요청 (외부 호출용)</summary>
        public void RefreshWaypoints() => RenderWaypoints();

        /// <summary>Data 좌표 → Viewport 좌표 변환 (Y↔Z swap + Y negate)</summary>
        private static Vector3 DataToViewport(Vector3 dataPos)
        {
            // PointCloudViewer 좌표 규칙:
            // Viewport X = Data X, Viewport Y = -Data Z (height), Viewport Z = Data Y
            return new Vector3(dataPos.X, -dataPos.Z, dataPos.Y);
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
