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
    private readonly LinesVisual3D _edgeLines = new() { Color = Colors.LightSteelBlue, Thickness = 1.2 };
    private readonly LinesVisual3D _hoverLines = new() { Color = Colors.Orange, Thickness = 3.0 };
    private readonly LinesVisual3D _contourLines = new() { Color = Colors.Red, Thickness = 3.5 };
    private readonly LinesVisual3D _torchLines = new() { Color = Colors.Cyan, Thickness = 1.5 };

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Viewport.Children.Add(_modelRoot);
        Viewport.Children.Add(_edgeLines);
        Viewport.Children.Add(_hoverLines);
        Viewport.Children.Add(_contourLines);
        Viewport.Children.Add(_torchLines);

        _viewModel.ModelChanged += (_, _) => RebuildScene();
        _viewModel.HighlightChanged += (_, _) => UpdateHighlights();
    }

    // ---- 렌더링 트리 구성 ----

    private void RebuildScene()
    {
        _modelRoot.Children.Clear();
        _edgeLines.Points.Clear();
        UpdateHighlights();

        var model = _viewModel.Model;
        if (model == null) return;

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

        // 전체 엣지 폴리라인
        foreach (var edge in model.Edges)
            AppendPolyline(_edgeLines.Points, edge.Points);

        Viewport.ZoomExtents(300);
    }

    private void UpdateHighlights()
    {
        _hoverLines.Points.Clear();
        _contourLines.Points.Clear();
        _torchLines.Points.Clear();

        var model = _viewModel.Model;
        if (model == null) return;

        var hovered = model.Edges.FirstOrDefault(e => e.EdgeId == _viewModel.HoveredEdgeId);
        if (hovered != null)
            AppendPolyline(_hoverLines.Points, hovered.Points);

        var contour = _viewModel.SelectedContour;
        if (contour != null)
        {
            AppendPolyline(_contourLines.Points, contour.PathPoints);
            // 토치 방향 화살선 (8mm, 일정 간격)
            int stride = Math.Max(1, contour.PathPoints.Count / 24);
            for (int i = 0; i < contour.PathPoints.Count && i < contour.TorchDirections.Count; i += stride)
            {
                var p = contour.PathPoints[i];
                _torchLines.Points.Add(p);
                _torchLines.Points.Add(p + contour.TorchDirections[i] * 8);
            }
        }
    }

    private static void AppendPolyline(Point3DCollection target, IList<Point3D> pts)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            target.Add(pts[i]);
            target.Add(pts[i + 1]);
        }
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
