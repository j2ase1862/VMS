using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using VMS.WeldTeach.Services;
using VMS.WeldTeach.ViewModels;

namespace VMS.WeldTeach.Views;

/// <summary>
/// 코드비하인드는 뷰포트 직접 상호작용(렌더링 트리 구성, 마우스→Ray 변환)만 담당한다.
/// 피킹/체이닝/포즈 계산 로직은 전부 ViewModel/Service 에 있다.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ModelVisual3D _modelRoot = new();
    // 경로 순번 라벨 앵커(3D) — 카메라 이동 시 2D 오버레이로 재투영
    private readonly List<(Point3D Pos, int Order, bool Selected)> _labelAnchors = new();
    private readonly LinesVisual3D _edgeLines = new() { Color = Colors.LightSteelBlue, Thickness = 1.2 };
    private readonly LinesVisual3D _hoverLines = new() { Color = Colors.Orange, Thickness = 3.0 };
    private readonly LinesVisual3D _contourLines = new() { Color = Colors.IndianRed, Thickness = 2.5 };
    private readonly LinesVisual3D _selectedLines = new() { Color = Colors.Red, Thickness = 4.0 };
    private readonly LinesVisual3D _torchLines = new() { Color = Colors.Cyan, Thickness = 1.5 };
    private readonly PointsVisual3D _posePoints = new() { Color = Colors.Yellow, Size = 5 };
    private readonly LinesVisual3D _labelLeaders = new() { Color = Colors.Gainsboro, Thickness = 1.0 };
    private readonly PointsVisual3D _scanCloud = new() { Color = Colors.Silver, Size = 2 };
    // 그라인딩 영역 하이라이트 (선택 영역은 더 크고 진하게)
    private readonly PointsVisual3D _regionPoints = new() { Color = Colors.Orange, Size = 3.5 };
    private readonly PointsVisual3D _selectedRegionPoints = new() { Color = Colors.OrangeRed, Size = 4.5 };
    // 커버리지 스캔라인 (선택 영역은 밝게)
    private readonly LinesVisual3D _scanlineLines = new() { Color = Colors.MediumSeaGreen, Thickness = 1.5 };
    private readonly LinesVisual3D _selectedScanlineLines = new() { Color = Colors.Lime, Thickness = 2.5 };

    // 라쏘/브러시 드래그 상태 (뷰 전용 — 선택 판정은 ViewModel/Service)
    private readonly List<Point> _dragTrail = new();
    private bool _dragSelecting;
    private MainViewModel.SelectionCombine _dragCombine;
    private readonly System.Windows.Shapes.Polyline _dragVisual = new()
    {
        Stroke = Brushes.Orange,
        StrokeThickness = 1.5,
        StrokeDashArray = new DoubleCollection { 4, 3 },
    };

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Viewport.Children.Add(_modelRoot);
        Viewport.Children.Add(_scanCloud);
        Viewport.Children.Add(_edgeLines);
        Viewport.Children.Add(_hoverLines);
        Viewport.Children.Add(_contourLines);
        Viewport.Children.Add(_selectedLines);
        Viewport.Children.Add(_torchLines);
        Viewport.Children.Add(_posePoints);
        Viewport.Children.Add(_labelLeaders);
        Viewport.Children.Add(_regionPoints);
        Viewport.Children.Add(_selectedRegionPoints);
        Viewport.Children.Add(_scanlineLines);
        Viewport.Children.Add(_selectedScanlineLines);
        SelectionOverlay.Children.Add(_dragVisual);

        _viewModel.ModelChanged += (_, _) => RebuildScene();
        _viewModel.HighlightChanged += (_, _) => UpdateHighlights();
    }

    // ---- 렌더링 트리 구성 ----

    // 주의: 보이는 LinesVisual3D 의 Points 에 Add 를 반복하면 HelixToolkit 이
    // 점 하나마다 라인 메쉬 전체를 재생성해 O(N²) 로 UI 가 수십 초 멈춘다 (스택 덤프로 확인).
    // 반드시 컬렉션을 오프라인으로 채워 한 번에 교체 할당한다.

    // 빌드된 CAD 비주얼 캐시 — 모드 전환 때 재빌드 없이 표시만 토글한다.
    private ModelVisual3D? _cadContent;
    private Point3DCollection _cadEdgePoints = new();

    /// <summary>
    /// 현재 공정 모드에 속하지 않는 비주얼을 화면에서 내린다.
    /// CAD 메쉬·엣지는 용접 전용이라 그라인딩 모드에 남아 있으면 점군 위에 겹쳐 보인다
    /// (모드 전환·재로드 시 이전 그래픽이 지워지지 않던 원인). 데이터는 캐시에 유지하므로
    /// 용접 모드로 돌아오면 재빌드 없이 즉시 복원된다.
    /// </summary>
    private void ApplyModeVisibility()
    {
        bool welding = !_viewModel.IsGrindingMode;
        _modelRoot.Children.Clear();
        if (welding && _cadContent != null) _modelRoot.Children.Add(_cadContent);
        _edgeLines.Points = welding ? _cadEdgePoints : new Point3DCollection();
    }

    private void RebuildScene()
    {
        _cadContent = null;
        _cadEdgePoints = new Point3DCollection();

        var model = _viewModel.Model;
        if (model == null)
        {
            ApplyModeVisibility();
            UpdateHighlights();
            return;
        }

        // 면 메쉬 → 단일 MeshGeometry (양면 재질 — 면 방향 이슈 회피)
        var builder = new MeshBuilder(false, false);
        foreach (var face in model.FaceMeshes)
        {
            int baseIdx = builder.Positions.Count;
            foreach (var p in face.Positions) builder.Positions.Add(p);
            foreach (var idx in face.TriangleIndices) builder.TriangleIndices.Add(baseIdx + idx);
        }
        var mesh = builder.ToMesh();
        var material = MaterialHelper.CreateMaterial(Color.FromRgb(150, 160, 175));
        _cadContent = new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material) { BackMaterial = material },
        };

        // 전체 엣지 폴리라인 — 단일 교체 할당
        var edgePts = new Point3DCollection(model.Edges.Sum(e => Math.Max(0, e.Points.Count - 1) * 2));
        foreach (var edge in model.Edges)
            AppendPolyline(edgePts, edge.Points);
        edgePts.Freeze();
        _cadEdgePoints = edgePts;

        ApplyModeVisibility();
        UpdateHighlights();
        Viewport.ZoomExtents(300);
    }

    private void UpdateHighlights()
    {
        // 모드가 바뀌었을 수 있다 — 매번 반대편 모드 비주얼을 내린다
        ApplyModeVisibility();

        if (_viewModel.IsGrindingMode)
        {
            UpdateGrindingHighlights();
            return;
        }
        _regionPoints.Points = new Point3DCollection();
        _selectedRegionPoints.Points = new Point3DCollection();
        _scanlineLines.Points = new Point3DCollection();
        _selectedScanlineLines.Points = new Point3DCollection();

        var model = _viewModel.Model;

        var hoverPts = new Point3DCollection();
        var allPts = new Point3DCollection();
        var selectedPts = new Point3DCollection();
        var torchPts = new Point3DCollection();
        var poseDots = new Point3DCollection();
        var leaderPts = new Point3DCollection();
        _labelAnchors.Clear();

        if (model != null)
        {
            var hovered = model.Edges.FirstOrDefault(e => e.EdgeId == _viewModel.HoveredEdgeId);
            if (hovered != null)
                AppendPolyline(hoverPts, hovered.Points);

            // 모든 경로 (목록 순서 = 용접 순서) + 시작점에 순번 라벨
            for (int order = 0; order < _viewModel.Contours.Count; order++)
            {
                var c = _viewModel.Contours[order];
                bool isSelected = ReferenceEquals(c, _viewModel.SelectedContour);
                AppendPolyline(isSelected ? selectedPts : allPts, c.PathPoints);

                if (c.PathPoints.Count > 0)
                {
                    // 순번 라벨 앵커: 경로 중간점에서 토치 방향으로 띄운 3D 점.
                    // 라벨 자체는 2D 오버레이(항상 최상위)로 그리고, 3D 지시선으로 경로와 연결한다.
                    var mid = c.PathPoints[c.PathPoints.Count / 2];
                    var lift = c.TorchDirections.Count > 0
                        ? c.TorchDirections[c.TorchDirections.Count / 2]
                        : new Vector3D(0, 0, 1);
                    var labelPos = mid + lift * 14;
                    leaderPts.Add(mid);
                    leaderPts.Add(labelPos);
                    _labelAnchors.Add((labelPos, order + 1, isSelected));
                }

                // 포즈 포인트(노란 점) + 토치 방향 화살선(8mm) — 기본은 선택 경로만,
                // [전체 보기] 켜면 모든 경로에 표시. 화살선은 화면 복잡도 제한(경로당 최대 ~80개)
                if (!isSelected && !_viewModel.ShowAllPaths) continue;
                foreach (var pp in c.PosePoints) poseDots.Add(pp);
                int stride = Math.Max(1, c.PosePoints.Count / 80);
                for (int i = 0; i < c.PosePoints.Count && i < c.TorchDirections.Count; i += stride)
                {
                    var p = c.PosePoints[i];
                    torchPts.Add(p);
                    torchPts.Add(p + c.TorchDirections[i] * 8);
                }
            }
        }

        hoverPts.Freeze(); allPts.Freeze(); selectedPts.Freeze(); torchPts.Freeze(); poseDots.Freeze(); leaderPts.Freeze();
        _hoverLines.Points = hoverPts;
        _contourLines.Points = allPts;
        _selectedLines.Points = selectedPts;
        _torchLines.Points = torchPts;
        _posePoints.Points = poseDots;
        _labelLeaders.Points = leaderPts;

        // 스캔 점군 (정합 후에는 CAD 좌표계로 변환되어 모델 위에 겹침)
        var cloudPts = new Point3DCollection(_viewModel.GetDisplayCloud());
        cloudPts.Freeze();
        _scanCloud.Points = cloudPts;

        RebuildLabelOverlay();
    }

    /// <summary>그라인딩 모드 렌더링 — 전처리 점군 + 영역 하이라이트 + 순번 라벨.</summary>
    private void UpdateGrindingHighlights()
    {
        // 용접 비주얼 정리
        var empty = new Point3DCollection();
        _hoverLines.Points = empty;
        _contourLines.Points = new Point3DCollection();
        _selectedLines.Points = new Point3DCollection();
        _labelLeaders.Points = new Point3DCollection();
        _labelAnchors.Clear();

        var pts = _viewModel.GrindingPoints;
        var cloudPts = new Point3DCollection(pts.Count);
        foreach (var p in pts) cloudPts.Add(p);
        cloudPts.Freeze();
        _scanCloud.Points = cloudPts;

        var regionPts = new Point3DCollection();
        var selectedPts = new Point3DCollection();
        var linePts = new Point3DCollection();
        var selLinePts = new Point3DCollection();
        var poseDots = new Point3DCollection();
        var poseFrames = new List<(Point3D P, Vector3D A)>();
        for (int order = 0; order < _viewModel.Regions.Count; order++)
        {
            var r = _viewModel.Regions[order];
            bool isSelected = ReferenceEquals(r, _viewModel.SelectedRegion);
            var target = isSelected ? selectedPts : regionPts;

            double cx = 0, cy = 0, cz = 0;
            foreach (var i in r.PointIndices)
            {
                if (i < 0 || i >= pts.Count) continue;
                var p = pts[i];
                target.Add(p);
                cx += p.X; cy += p.Y; cz += p.Z;
            }
            if (r.PointIndices.Count > 0)
            {
                int n = r.PointIndices.Count;
                // 순번 라벨 앵커 — 영역 무게중심 상방 (스캔 좌표계 +Z 가 카메라 쪽 전제)
                _labelAnchors.Add((new Point3D(cx / n, cy / n, cz / n + 10), order + 1, isSelected));
            }

            // 커버리지 스캔라인 (세그먼트별 폴리라인)
            var lineTarget = isSelected ? selLinePts : linePts;
            foreach (var s in r.Scanlines)
            {
                AppendPolyline(lineTarget, s.PathPoints);

                // 공구 포즈 — 선택 영역만(미선택이면 전체). 표면 패스(0번) 기준.
                if (_viewModel.SelectedRegion != null && !isSelected) continue;
                for (int i = 0; i < s.PosePoints.Count && i < s.ToolAxes.Count; i++)
                {
                    poseDots.Add(s.PosePoints[i]);
                    poseFrames.Add((s.PosePoints[i], s.ToolAxes[i]));
                }
            }
        }

        // 공구 축 화살선(8mm) — 화면 복잡도 제한으로 최대 ~200개만
        var axisPts = new Point3DCollection();
        int stride = Math.Max(1, poseFrames.Count / 200);
        for (int i = 0; i < poseFrames.Count; i += stride)
        {
            axisPts.Add(poseFrames[i].P);
            axisPts.Add(poseFrames[i].P + poseFrames[i].A * 8);
        }

        regionPts.Freeze(); selectedPts.Freeze(); linePts.Freeze(); selLinePts.Freeze();
        poseDots.Freeze(); axisPts.Freeze();
        _regionPoints.Points = regionPts;
        _selectedRegionPoints.Points = selectedPts;
        _scanlineLines.Points = linePts;
        _selectedScanlineLines.Points = selLinePts;
        _posePoints.Points = poseDots;
        _torchLines.Points = axisPts;

        RebuildLabelOverlay();
    }

    private static void AppendPolyline(Point3DCollection target, IList<Point3D> pts)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            target.Add(pts[i]);
            target.Add(pts[i + 1]);
        }
    }

    // ---- 경로 순번 2D 오버레이 (3D 앵커 → 화면 좌표 재투영) ----

    private void RebuildLabelOverlay()
    {
        LabelOverlay.Children.Clear();
        foreach (var (_, order, selected) in _labelAnchors)
        {
            var badge = new System.Windows.Controls.Border
            {
                Background = selected ? Brushes.Red : Brushes.IndianRed,
                CornerRadius = new CornerRadius(10),
                MinWidth = 20,
                Height = 20,
                Padding = new Thickness(5, 0, 5, 0),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = order.ToString(),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            LabelOverlay.Children.Add(badge);
        }
        PositionLabelOverlay();
    }

    private void PositionLabelOverlay()
    {
        for (int i = 0; i < _labelAnchors.Count && i < LabelOverlay.Children.Count; i++)
        {
            var el = (FrameworkElement)LabelOverlay.Children[i];
            var pt = Viewport.Viewport.Point3DtoPoint2D(_labelAnchors[i].Pos);
            System.Windows.Controls.Canvas.SetLeft(el, pt.X - 10);
            System.Windows.Controls.Canvas.SetTop(el, pt.Y - 10);
        }
    }

    private void Viewport_CameraChanged(object sender, RoutedEventArgs e) => PositionLabelOverlay();

    /// <summary>진단 모드 — 점군/모델 전체가 보이도록 카메라를 맞춘다.</summary>
    public void ZoomExtentsForDiagnostics() => Viewport.ZoomExtents(300);

    /// <summary>진단 모드 — 현재 창을 PNG 로 캡처한다.</summary>
    public void CaptureToPng(string path)
    {
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    // ---- 마우스 → Ray 변환 (명세서 Step 1 의 Unproject) ----

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel.IsGrindingMode)
        {
            if (!_dragSelecting) return;
            var pos = e.GetPosition(SelectionOverlay);
            // 2px 이상 움직였을 때만 기록 — 궤적 점 수 제한
            if (_dragTrail.Count == 0 || (pos - _dragTrail[^1]).Length > 2)
            {
                _dragTrail.Add(pos);
                _dragVisual.Points.Add(pos);
            }
            return;
        }
        if (TryGetRay(e.GetPosition(Viewport.Viewport), out var origin, out var direction))
            _viewModel.OnViewportHover(origin, direction);
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;
        if (_viewModel.IsGrindingMode)
        {
            _dragSelecting = true;
            _dragTrail.Clear();
            _dragVisual.Points.Clear();
            var pos = e.GetPosition(SelectionOverlay);
            _dragTrail.Add(pos);
            _dragVisual.Points.Add(pos);
            // 조합은 드래그 시작 시점의 수정키로 고정 (Shift=추가, Ctrl=제거)
            _dragCombine = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? MainViewModel.SelectionCombine.Remove
                : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                    ? MainViewModel.SelectionCombine.Add
                    : MainViewModel.SelectionCombine.NewRegion;
            Viewport.CaptureMouse();
            return;
        }
        if (TryGetRay(e.GetPosition(Viewport.Viewport), out var origin, out var direction))
            _viewModel.OnViewportClick(origin, direction);
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragSelecting) return;
        _dragSelecting = false;
        Viewport.ReleaseMouseCapture();
        var trail = _dragTrail.ToList();
        _dragTrail.Clear();
        _dragVisual.Points.Clear();

        var project = BuildProjector();
        if (project == null || trail.Count < 2) return;
        if (_viewModel.IsBrushMode)
            _viewModel.ApplyBrushSelection(trail, project, _dragCombine);
        else if (trail.Count >= 3)
            _viewModel.ApplyLassoSelection(trail, project, _dragCombine);
    }

    /// <summary>
    /// 월드→화면 투영 델리게이트 — 총 변환 행렬을 한 번만 취득해 대량 점 판정에 재사용한다.
    /// 깊이는 카메라 시선 방향 거리(mm) — 첫 레이어 필터의 밴드 단위와 일치.
    /// </summary>
    private RegionSelectService.ProjectFunc? BuildProjector()
    {
        var vp = Viewport.Viewport;
        if (vp.ActualWidth < 1 || vp.ActualHeight < 1) return null;
        if (Viewport.Camera is not ProjectionCamera cam) return null;
        var total = Viewport3DHelper.GetTotalTransform(vp);
        var camPos = cam.Position;
        var look = cam.LookDirection;
        if (look.Length < 1e-12) return null;
        look.Normalize();
        return p =>
        {
            double depth = Vector3D.DotProduct(p - camPos, look);
            if (depth <= 0) return new ProjectedPoint(0, 0, 0, false);
            var q = total.Transform(p);
            return new ProjectedPoint(q.X, q.Y, depth, true);
        };
    }

    /// <summary>진단 모드(--grindcap) — 뷰포트 중앙 40% 사각형을 라쏘 선택해 UI 흐름을 검증한다.</summary>
    public void DiagSelectCenterRegion()
    {
        var project = BuildProjector();
        if (project == null) return;
        double w = Viewport.Viewport.ActualWidth, h = Viewport.Viewport.ActualHeight;
        var poly = new List<Point>
        {
            new(w * 0.3, h * 0.3), new(w * 0.7, h * 0.3),
            new(w * 0.7, h * 0.7), new(w * 0.3, h * 0.7),
        };
        _viewModel.ApplyLassoSelection(poly, project, MainViewModel.SelectionCombine.NewRegion);
    }

    private bool TryGetRay(Point mousePos, out Point3D origin, out Vector3D direction)
    {
        origin = default;
        direction = default;
        var ray = Viewport.Viewport.Point2DtoRay3D(mousePos);
        if (ray == null) return false;
        origin = ray.Origin;
        direction = ray.Direction;
        if (direction.Length < 1e-12) return false;
        direction.Normalize();
        return true;
    }
}
