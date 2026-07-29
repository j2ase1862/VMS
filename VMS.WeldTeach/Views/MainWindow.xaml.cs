using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
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
    private readonly ModelVisual3D _labelRoot = new();   // 경로 순번 3D 라벨
    private readonly LinesVisual3D _edgeLines = new() { Color = Colors.LightSteelBlue, Thickness = 1.2 };
    private readonly LinesVisual3D _hoverLines = new() { Color = Colors.Orange, Thickness = 3.0 };
    private readonly LinesVisual3D _contourLines = new() { Color = Colors.IndianRed, Thickness = 2.5 };
    private readonly LinesVisual3D _selectedLines = new() { Color = Colors.Red, Thickness = 4.0 };
    private readonly LinesVisual3D _torchLines = new() { Color = Colors.Cyan, Thickness = 1.5 };
    private readonly PointsVisual3D _posePoints = new() { Color = Colors.Yellow, Size = 5 };

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Viewport.Children.Add(_modelRoot);
        Viewport.Children.Add(_edgeLines);
        Viewport.Children.Add(_hoverLines);
        Viewport.Children.Add(_contourLines);
        Viewport.Children.Add(_selectedLines);
        Viewport.Children.Add(_torchLines);
        Viewport.Children.Add(_posePoints);
        Viewport.Children.Add(_labelRoot);

        _viewModel.ModelChanged += (_, _) => RebuildScene();
        _viewModel.HighlightChanged += (_, _) => UpdateHighlights();
    }

    // ---- 렌더링 트리 구성 ----

    // 주의: 보이는 LinesVisual3D 의 Points 에 Add 를 반복하면 HelixToolkit 이
    // 점 하나마다 라인 메쉬 전체를 재생성해 O(N²) 로 UI 가 수십 초 멈춘다 (스택 덤프로 확인).
    // 반드시 컬렉션을 오프라인으로 채워 한 번에 교체 할당한다.

    private void RebuildScene()
    {
        _modelRoot.Children.Clear();
        UpdateHighlights();

        var model = _viewModel.Model;
        if (model == null)
        {
            _edgeLines.Points = new Point3DCollection();
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
        _modelRoot.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(mesh, material) { BackMaterial = material },
        });

        // 전체 엣지 폴리라인 — 단일 교체 할당
        var edgePts = new Point3DCollection(model.Edges.Sum(e => Math.Max(0, e.Points.Count - 1) * 2));
        foreach (var edge in model.Edges)
            AppendPolyline(edgePts, edge.Points);
        edgePts.Freeze();
        _edgeLines.Points = edgePts;

        Viewport.ZoomExtents(300);
    }

    private void UpdateHighlights()
    {
        var model = _viewModel.Model;

        var hoverPts = new Point3DCollection();
        var allPts = new Point3DCollection();
        var selectedPts = new Point3DCollection();
        var torchPts = new Point3DCollection();
        var poseDots = new Point3DCollection();
        _labelRoot.Children.Clear();

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
                    _labelRoot.Children.Add(new BillboardTextVisual3D
                    {
                        Text = $" {order + 1} ",
                        Position = c.PathPoints[0],
                        Foreground = Brushes.White,
                        Background = isSelected ? Brushes.Red : Brushes.IndianRed,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(2),
                    });
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

        hoverPts.Freeze(); allPts.Freeze(); selectedPts.Freeze(); torchPts.Freeze(); poseDots.Freeze();
        _hoverLines.Points = hoverPts;
        _contourLines.Points = allPts;
        _selectedLines.Points = selectedPts;
        _torchLines.Points = torchPts;
        _posePoints.Points = poseDots;
    }

    private static void AppendPolyline(Point3DCollection target, IList<Point3D> pts)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            target.Add(pts[i]);
            target.Add(pts[i + 1]);
        }
    }

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
        if (TryGetRay(e.GetPosition(Viewport.Viewport), out var origin, out var direction))
            _viewModel.OnViewportHover(origin, direction);
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;
        if (TryGetRay(e.GetPosition(Viewport.Viewport), out var origin, out var direction))
            _viewModel.OnViewportClick(origin, direction);
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
