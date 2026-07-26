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

        // ── Measurement state ──
        private enum MeasureMode { None, Distance, Angle }
        private MeasureMode _measureMode = MeasureMode.None;
        private readonly List<Vector3> _measurePicks = new();          // 진행 중 선택 (viewport 좌표)
        private readonly List<Element3D> _pendingMeasureMarkers = new(); // 진행 중 마커 (취소 시 제거)
        private const double MeasureClickMaxDragPx = 5.0;   // 이내면 팬이 아니라 클릭으로 간주
        private const double MeasurePickRadiusPx = 12.0;
        private const string MeasureTag = "Measure";
        private static readonly Color4 MeasureColor = new(1f, 0.84f, 0.25f, 1f);
        private static readonly System.Windows.Media.Color MeasureMediaColor =
            System.Windows.Media.Color.FromRgb(255, 214, 64);

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

            // 탭 전환 시 Loaded 가 재발생한다 — 이미 점군이 표시 중이면 데이터 기준
            // 그리드/박스/축을 재구성하고, 기본(원점 ±500) 그리드로 되돌리지 않는다.
            // (기본 그리드로 덮어쓰면 그리드만 점군에서 분리되어 보이는 버그)
            if (PointCloudModel.Geometry is PointGeometry3D geo && geo.Positions is { Count: > 0 })
            {
                BuildGridTickLabels(geo.Positions);
            }
            else
            {
                BuildGridLines();
                BuildAxisLines();
            }

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

            // Mech-Eye 규약: 데이터 X=적 / Y=녹 / Z=청.
            // viewport Y = 데이터 Z(높이) → 청, viewport Z = 데이터 Y → 녹.
            var axisColors = new Color4Collection
            {
                AxisColorX, AxisColorX,
                AxisColorZ, AxisColorZ,
                AxisColorY, AxisColorY
            };
            geo.Colors = axisColors;

            AxisLines.Geometry = geo;

            BuildAxisLabel(AxisLabelX, "X(mm)", new Vector3(len * 1.1f, 0, 0), AxisColorX);
            BuildAxisLabel(AxisLabelY, "Z(mm)", new Vector3(0, len * 1.1f, 0), AxisColorZ);
            BuildAxisLabel(AxisLabelZ, "Y(mm)", new Vector3(0, 0, len * 1.1f), AxisColorY);
        }

        // 데이터 축 색 (Mech-Eye 규약): X=적, Y=녹, Z=청
        private static readonly Color4 AxisColorX = new(1f, 0.25f, 0.25f, 1f);
        private static readonly Color4 AxisColorY = new(0.3f, 1f, 0.3f, 1f);
        private static readonly Color4 AxisColorZ = new(0.35f, 0.65f, 1f, 1f);

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

        /// <summary>
        /// 데이터 바운딩 박스에 정합된 그리드·박스·축·눈금을 재구성 (Mech-Eye Viewer 스타일).
        /// 그리드는 원점(0,0)이 아니라 점군의 실좌표 범위(FOV)에 정확히 겹쳐 그려지고,
        /// 눈금 라벨은 박스 모서리를 따라 실제 좌표값을 표시한다.
        /// </summary>
        private void BuildGridTickLabels(Vector3Collection positions)
        {
            if (positions.Count == 0)
            {
                GridTickLabels.Geometry = null;
                BoundsBoxLines.Geometry = null;
                return;
            }

            // 로버스트 바운즈 (0.5%~99.5% 백분위) — 실측 점군의 소수 깊이 이상점이
            // 박스/그리드를 부풀려 본체에서 멀리 떨어져 보이는 문제 방지.
            // (Mech-Eye Viewer 가 그리드를 점군에 밀착시켜 보이는 것과 같은 효과)
            var (min, max) = ComputeRobustBounds(positions);

            // 라벨을 박스 밖으로 밀어내는 여백 — 데이터 크기에 비례
            float margin = MathF.Max((max - min).Length() * 0.04f, 5f);

            var tickText = new BillboardText3D();
            var tickColor = new Color4(0.78f, 0.78f, 0.78f, 1f);
            int tickCount = 5;

            // 바닥면 = 가장 깊은 쪽보다 약간 더 아래 — 시트의 깊은 부분과 그리드가
            // 같은 평면에서 겹쳐 안 보이는 문제 방지 (장면 크기의 1.5% 만큼 분리)
            float floorGap = MathF.Max((max - min).Length() * 0.015f, 2f);
            float floorY = max.Y + floorGap;

            // X 눈금 — 앞쪽 바닥 모서리(z = max.Z)를 따라, 실좌표 위치에 배치
            for (int i = 0; i <= tickCount; i++)
            {
                float x = min.X + (max.X - min.X) * i / tickCount;
                tickText.TextInfo.Add(new TextInfo(
                    x.ToString("F1"), new Vector3(x, floorY, max.Z + margin))
                { Foreground = tickColor, Scale = 0.6f });
            }

            // Y(데이터) 눈금 — 오른쪽 바닥 모서리(x = max.X)를 따라 (viewport Z = 데이터 Y)
            for (int i = 0; i <= tickCount; i++)
            {
                float z = min.Z + (max.Z - min.Z) * i / tickCount;
                tickText.TextInfo.Add(new TextInfo(
                    z.ToString("F1"), new Vector3(max.X + margin, floorY, z))
                { Foreground = tickColor, Scale = 0.6f });
            }

            // Z(높이) 눈금 — 왼쪽 앞 수직 모서리를 따라. viewport Y = dataZ - zMax
            for (int i = 0; i <= tickCount; i++)
            {
                float viewportY = min.Y + (max.Y - min.Y) * i / tickCount;
                float dataZ = viewportY + _dataZMax;
                tickText.TextInfo.Add(new TextInfo(
                    dataZ.ToString("F1"), new Vector3(min.X - margin, viewportY, max.Z))
                { Foreground = tickColor, Scale = 0.6f });
            }

            GridTickLabels.Geometry = tickText;

            RebuildGridForData(min, max, floorY);
            BuildBoundsBox(min, max, floorY);
            RebuildAxisEdges(min, max, margin, floorY);
        }

        /// <summary>
        /// 축별 0.5%~99.5% 백분위 바운즈 — 소수 이상점(outlier)이 그리드/박스를
        /// 부풀리지 않도록 한다. 성능을 위해 최대 10만 점 스트라이드 샘플링.
        /// 축 범위가 0에 수렴하면(평면 등) 전체 대각선의 0.5%만큼 확장해 퇴화 방지.
        /// </summary>
        public static (Vector3 min, Vector3 max) ComputeRobustBounds(Vector3Collection positions)
        {
            int count = positions.Count;
            int stride = Math.Max(1, count / 100_000);
            int sampleCount = (count + stride - 1) / stride;

            var xs = new float[sampleCount];
            var ys = new float[sampleCount];
            var zs = new float[sampleCount];
            int n = 0;
            for (int i = 0; i < count; i += stride)
            {
                xs[n] = positions[i].X;
                ys[n] = positions[i].Y;
                zs[n] = positions[i].Z;
                n++;
            }

            Array.Sort(xs, 0, n);
            Array.Sort(ys, 0, n);
            Array.Sort(zs, 0, n);

            int lo = (int)(n * 0.005f);
            int hi = Math.Max(lo, (int)(n * 0.995f) - 1);

            var min = new Vector3(xs[lo], ys[lo], zs[lo]);
            var max = new Vector3(xs[hi], ys[hi], zs[hi]);

            // 퇴화 방지 — 축 범위가 거의 0이면 살짝 벌려 박스가 접히지 않게
            float pad = MathF.Max((max - min).Length() * 0.005f, 0.5f);
            if (max.X - min.X < pad) { min.X -= pad; max.X += pad; }
            if (max.Y - min.Y < pad) { min.Y -= pad; max.Y += pad; }
            if (max.Z - min.Z < pad) { min.Z -= pad; max.Z += pad; }

            return (min, max);
        }

        private void RebuildGridForData(Vector3 min, Vector3 max, float floorY)
        {
            var builder = new LineBuilder();

            // 간격은 라운드 값(1/2/5×10ⁿ) 자동 선택으로 약 30칸 — 데이터 크기와
            // 무관하게 정사각 셀 + 좌표 정렬(라인이 간격의 배수 좌표에 놓임).
            float step = NiceGridStep(MathF.Max(max.X - min.X, max.Z - min.Z));

            for (int i = (int)MathF.Ceiling(min.X / step); i * step <= max.X; i++)
            {
                float x = i * step;
                builder.AddLine(new Vector3(x, floorY, min.Z), new Vector3(x, floorY, max.Z));
            }
            for (int i = (int)MathF.Ceiling(min.Z / step); i * step <= max.Z; i++)
            {
                float z = i * step;
                builder.AddLine(new Vector3(min.X, floorY, z), new Vector3(max.X, floorY, z));
            }

            // 경계선 — 데이터 범위 끝단은 항상 표시
            builder.AddLine(new Vector3(min.X, floorY, min.Z), new Vector3(min.X, floorY, max.Z));
            builder.AddLine(new Vector3(max.X, floorY, min.Z), new Vector3(max.X, floorY, max.Z));
            builder.AddLine(new Vector3(min.X, floorY, min.Z), new Vector3(max.X, floorY, min.Z));
            builder.AddLine(new Vector3(min.X, floorY, max.Z), new Vector3(max.X, floorY, max.Z));

            GridLines.Geometry = builder.ToLineGeometry3D();
        }

        /// <summary>
        /// 그리드 간격 — 큰 축 기준 약 30칸이 되도록 1/2/5×10ⁿ 라운드 값 선택.
        /// 예: 폭 1900mm → 50mm 간격(38칸), 폭 200mm → 5mm 간격(40칸).
        /// </summary>
        public static float NiceGridStep(float extent)
        {
            if (extent <= 0f) return 1f;
            float raw = extent / 30f;
            float pow = MathF.Pow(10f, MathF.Floor(MathF.Log10(raw)));
            float mantissa = raw / pow;
            float nice = mantissa < 1.5f ? 1f : mantissa < 3.5f ? 2f : mantissa < 7.5f ? 5f : 10f;
            return nice * pow;
        }

        /// <summary>데이터 바운딩 박스 와이어프레임 (12 모서리). 바닥은 floorY(그리드 면)까지.</summary>
        private void BuildBoundsBox(Vector3 min, Vector3 max, float floorY)
        {
            var b = new LineBuilder();

            // 위쪽(카메라 가까운 면) 4모서리
            b.AddLine(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z));
            b.AddLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z));
            b.AddLine(new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z));
            b.AddLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z));
            // 바닥(그리드 면) 4모서리
            b.AddLine(new Vector3(min.X, floorY, min.Z), new Vector3(max.X, floorY, min.Z));
            b.AddLine(new Vector3(max.X, floorY, min.Z), new Vector3(max.X, floorY, max.Z));
            b.AddLine(new Vector3(max.X, floorY, max.Z), new Vector3(min.X, floorY, max.Z));
            b.AddLine(new Vector3(min.X, floorY, max.Z), new Vector3(min.X, floorY, min.Z));
            // 수직 4모서리
            b.AddLine(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, floorY, min.Z));
            b.AddLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, floorY, min.Z));
            b.AddLine(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, floorY, max.Z));
            b.AddLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, floorY, max.Z));

            BoundsBoxLines.Geometry = b.ToLineGeometry3D();
        }

        /// <summary>
        /// 축 라인·라벨을 박스 모서리에 배치 — X(적): 앞 바닥, Y(녹): 오른쪽 바닥,
        /// Z(청): 왼쪽 앞 수직. 눈금 라벨과 같은 모서리를 공유해 읽기 일관성 유지.
        /// </summary>
        private void RebuildAxisEdges(Vector3 min, Vector3 max, float margin, float floorY)
        {
            var builder = new LineBuilder();
            builder.AddLine(new Vector3(min.X, floorY, max.Z), new Vector3(max.X, floorY, max.Z)); // X
            builder.AddLine(new Vector3(max.X, floorY, min.Z), new Vector3(max.X, floorY, max.Z)); // 데이터 Y
            builder.AddLine(new Vector3(min.X, floorY, max.Z), new Vector3(min.X, min.Y, max.Z));  // 높이(데이터 Z)

            var geo = builder.ToLineGeometry3D();
            geo.Colors = new Color4Collection
            {
                AxisColorX, AxisColorX,
                AxisColorY, AxisColorY,
                AxisColorZ, AxisColorZ
            };
            AxisLines.Geometry = geo;

            BuildAxisLabel(AxisLabelX, "X(mm)",
                new Vector3((min.X + max.X) * 0.5f, floorY, max.Z + margin * 2.2f), AxisColorX);
            BuildAxisLabel(AxisLabelZ, "Y(mm)",
                new Vector3(max.X + margin * 2.2f, floorY, (min.Z + max.Z) * 0.5f), AxisColorY);
            BuildAxisLabel(AxisLabelY, "Z(mm)",
                new Vector3(min.X - margin * 2.2f, (min.Y + max.Y) * 0.5f, max.Z), AxisColorZ);
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

                // 측정 모드에서 드래그 없이 뗀 좌클릭 = 점 선택 (드래그 팬은 그대로 동작)
                if (_measureMode != MeasureMode.None)
                {
                    var pos = e.GetPosition(Viewport);
                    double dx = pos.X - _mouseStart.X;
                    double dy = pos.Y - _mouseStart.Y;
                    if (dx * dx + dy * dy <= MeasureClickMaxDragPx * MeasureClickMaxDragPx)
                        TryPickMeasurePoint(pos);
                }

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

        #region Measurement (Distance / Angle)

        private void BtnMeasureDistance_Checked(object sender, RoutedEventArgs e)
        {
            BtnMeasureAngle.IsChecked = false;
            _measureMode = MeasureMode.Distance;
            CancelPendingMeasurement();
        }

        private void BtnMeasureAngle_Checked(object sender, RoutedEventArgs e)
        {
            BtnMeasureDistance.IsChecked = false;
            _measureMode = MeasureMode.Angle;
            CancelPendingMeasurement();
        }

        private void MeasureMode_Unchecked(object sender, RoutedEventArgs e)
        {
            if (BtnMeasureDistance.IsChecked != true && BtnMeasureAngle.IsChecked != true)
            {
                _measureMode = MeasureMode.None;
                CancelPendingMeasurement();
            }
        }

        private void BtnMeasureClear_Click(object sender, RoutedEventArgs e)
        {
            ClearMeasurements();
        }

        /// <summary>완료된 측정 포함 모든 측정 오버레이 제거.</summary>
        private void ClearMeasurements()
        {
            var toRemove = Viewport.Items
                .OfType<Element3D>()
                .Where(el => el.Tag is string tag && tag == MeasureTag)
                .ToList();
            foreach (var item in toRemove)
                Viewport.Items.Remove(item);

            _measurePicks.Clear();
            _pendingMeasureMarkers.Clear();
            UpdateMeasureHint();
        }

        /// <summary>진행 중(미완성) 선택만 취소 — 완료된 측정은 유지.</summary>
        private void CancelPendingMeasurement()
        {
            foreach (var marker in _pendingMeasureMarkers)
                Viewport.Items.Remove(marker);
            _pendingMeasureMarkers.Clear();
            _measurePicks.Clear();
            UpdateMeasureHint();
        }

        private void TryPickMeasurePoint(Point mouse)
        {
            if (PointCloudModel.Geometry is not PointGeometry3D geo || geo.Positions is not { Count: > 0 })
                return;
            if (Viewport.ActualWidth < 1 || Viewport.ActualHeight < 1)
                return;

            var camPos = new Vector3(
                (float)MainCamera.Position.X, (float)MainCamera.Position.Y, (float)MainCamera.Position.Z);
            var look = new Vector3(
                (float)MainCamera.LookDirection.X, (float)MainCamera.LookDirection.Y, (float)MainCamera.LookDirection.Z);
            var up = new Vector3(
                (float)MainCamera.UpDirection.X, (float)MainCamera.UpDirection.Y, (float)MainCamera.UpDirection.Z);

            var viewProj = PointCloudMeasurement.BuildViewProjection(
                camPos, look, up,
                (float)MainCamera.FieldOfView,
                (float)(Viewport.ActualWidth / Viewport.ActualHeight),
                (float)MainCamera.NearPlaneDistance,
                (float)MainCamera.FarPlaneDistance);

            int idx = PointCloudMeasurement.FindNearestPointOnScreen(
                geo.Positions, viewProj, camPos,
                Viewport.ActualWidth, Viewport.ActualHeight,
                mouse.X, mouse.Y, MeasurePickRadiusPx);
            if (idx < 0) return;

            var picked = geo.Positions[idx];
            _measurePicks.Add(picked);
            AddMeasureMarker(picked);

            int needed = _measureMode == MeasureMode.Distance ? 2 : 3;
            if (_measurePicks.Count >= needed)
                CompleteMeasurement();

            UpdateMeasureHint();
        }

        private void AddMeasureMarker(Vector3 viewportPos)
        {
            var markerGeo = new PointGeometry3D
            {
                Positions = new Vector3Collection(new[] { viewportPos })
            };
            var marker = new PointGeometryModel3D
            {
                Geometry = markerGeo,
                Color = MeasureMediaColor,
                Size = new System.Windows.Size(10, 10),
                Tag = MeasureTag
            };
            Viewport.Items.Add(marker);
            _pendingMeasureMarkers.Add(marker);
        }

        private void CompleteMeasurement()
        {
            var cloud = PointCloud;
            var intrinsics = cloud?.Intrinsics;
            bool metric = PointCloudMeasurement.IsMetric(cloud);

            // viewport → data 좌표 복원 후 mm 공간에서 계산
            var dataPts = _measurePicks.Select(ViewportToData).ToList();

            var builder = new LineBuilder();
            string label;
            Vector3 labelPos;

            if (_measureMode == MeasureMode.Distance)
            {
                builder.AddLine(_measurePicks[0], _measurePicks[1]);
                float dist = PointCloudMeasurement.Distance(dataPts[0], dataPts[1], intrinsics);
                label = metric ? $"{dist:F2} mm" : $"{dist:F2} (미보정)";
                labelPos = (_measurePicks[0] + _measurePicks[1]) * 0.5f;
            }
            else
            {
                builder.AddLine(_measurePicks[0], _measurePicks[1]);
                builder.AddLine(_measurePicks[1], _measurePicks[2]);
                float deg = PointCloudMeasurement.AngleDeg(dataPts[0], dataPts[1], dataPts[2], intrinsics);
                label = float.IsNaN(deg) ? "—"
                    : metric ? $"{deg:F1}°" : $"{deg:F1}° (미보정)";
                labelPos = _measurePicks[1];
            }

            var lineModel = new LineGeometryModel3D
            {
                Geometry = builder.ToLineGeometry3D(),
                Color = MeasureMediaColor,
                Thickness = 1.5,
                Tag = MeasureTag
            };
            Viewport.Items.Add(lineModel);

            var billboard = new BillboardSingleText3D
            {
                FontColor = MeasureColor,
                BackgroundColor = new Color4(0f, 0f, 0f, 0.55f),
                FontSize = 12,
                FontWeight = SharpDX.DirectWrite.FontWeight.Bold,
                TextInfo = new TextInfo(label, labelPos)
            };
            Viewport.Items.Add(new BillboardTextModel3D
            {
                Geometry = billboard,
                FixedSize = true,
                Tag = MeasureTag
            });

            // 완료 — 마커는 측정 결과의 일부로 유지, 다음 클릭부터 새 측정 시작
            _pendingMeasureMarkers.Clear();
            _measurePicks.Clear();
        }

        private void UpdateMeasureHint()
        {
            if (_measureMode == MeasureMode.None)
            {
                MeasureHintPanel.Visibility = Visibility.Collapsed;
                return;
            }

            int needed = _measureMode == MeasureMode.Distance ? 2 : 3;
            string what = _measureMode == MeasureMode.Distance ? "거리" : "각도 (두 번째 점 = 꼭짓점)";
            MeasureHintText.Text = $"{what}: 점 {_measurePicks.Count}/{needed} 선택 — 클릭 = 점, 드래그 = 화면 이동";
            MeasureHintPanel.Visibility = Visibility.Visible;
        }

        /// <summary>Viewport 좌표 → Data 좌표 (viewport Y = dataZ − zMax 역변환)</summary>
        private Vector3 ViewportToData(Vector3 viewportPos) =>
            new(viewportPos.X, viewportPos.Z, viewportPos.Y + _dataZMax);

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
            // 새 데이터는 zMax 기준 viewport 좌표계가 달라지므로 기존 측정은 무효
            ClearMeasurements();

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
                ResetDepthSliders(zMin, zMax);
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

        #region Depth Range Slider / Grid Toggle

        private bool _suppressDepthSliderEvents;
        private System.Windows.Threading.DispatcherTimer? _recolorThrottle;
        private bool _sceneOverlayVisible = true;

        /// <summary>새 데이터 로드 시 깊이 표시 범위를 전체로 초기화.</summary>
        private void ResetDepthSliders(float zMin, float zMax)
        {
            _suppressDepthSliderEvents = true;
            DepthMinSlider.Minimum = zMin;
            DepthMinSlider.Maximum = zMax;
            DepthMaxSlider.Minimum = zMin;
            DepthMaxSlider.Maximum = zMax;
            DepthMinSlider.Value = zMin;
            DepthMaxSlider.Value = zMax;
            _suppressDepthSliderEvents = false;
        }

        private void DepthSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressDepthSliderEvents) return;

            // 상한 ≥ 하한 강제 — 겹침 배치한 두 슬라이더를 범위 슬라이더처럼 사용
            if (DepthMinSlider.Value > DepthMaxSlider.Value)
            {
                _suppressDepthSliderEvents = true;
                if (ReferenceEquals(sender, DepthMinSlider))
                    DepthMaxSlider.Value = DepthMinSlider.Value;
                else
                    DepthMinSlider.Value = DepthMaxSlider.Value;
                _suppressDepthSliderEvents = false;
            }

            MinValueLabel.Text = DepthMinSlider.Value.ToString("F1");
            MaxValueLabel.Text = DepthMaxSlider.Value.ToString("F1");

            // 재색칠은 지오메트리 재구성이라 드래그 중 과호출 방지 (120ms 스로틀)
            _recolorThrottle ??= new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _recolorThrottle.Stop();
            _recolorThrottle.Tick -= RecolorThrottle_Tick;
            _recolorThrottle.Tick += RecolorThrottle_Tick;
            _recolorThrottle.Start();
        }

        private void RecolorThrottle_Tick(object? sender, EventArgs e)
        {
            _recolorThrottle?.Stop();
            RecolorWithRange((float)DepthMinSlider.Value, (float)DepthMaxSlider.Value);
        }

        private void BtnResetDepthRange_Click(object sender, RoutedEventArgs e)
        {
            ResetDepthSliders(_dataZMin, _dataZMax);
            MinValueLabel.Text = _dataZMin.ToString("F1");
            MaxValueLabel.Text = _dataZMax.ToString("F1");
            RecolorWithRange(_dataZMin, _dataZMax);
        }

        private void BtnToggleGrid_Click(object sender, RoutedEventArgs e)
        {
            _sceneOverlayVisible = !_sceneOverlayVisible;
            var vis = _sceneOverlayVisible ? Visibility.Visible : Visibility.Collapsed;
            GridLines.Visibility = vis;
            BoundsBoxLines.Visibility = vis;
            GridTickLabels.Visibility = vis;
            AxisLines.Visibility = vis;
            AxisLabelX.Visibility = vis;
            AxisLabelY.Visibility = vis;
            AxisLabelZ.Visibility = vis;
        }

        #endregion

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
