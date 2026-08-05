using System.Windows.Media.Media3D;
using VMS.WeldTeach.Services;

namespace VMS.WeldTeach.Tests;

/// <summary>
/// SampleCloudGenerator 가 다루지 않는 실패 케이스 시편.
/// CLAUDE.md §5 의 실패 케이스 목록에 대응한다 — 오버행·오목면·국부 급경사.
/// 모두 결정적(고정 격자, 노이즈 없음)이라 회귀 비교가 가능하다.
/// </summary>
public static class SyntheticSurfaces
{
    /// <summary>기본 시점 — 패널 상방 (SampleCloudGenerator 와 동일 규약).</summary>
    public static readonly Point3D Viewpoint = new(50, 30, 500);

    /// <summary>
    /// 오버행 시편 — 100×60 상면(z=0)과 그 아래 z=−12 하면이 X ∈ [30,70] 구간에서 겹친다.
    /// 주평면(XY) 투영 시 겹침 구간의 셀은 상·하면을 동시에 포함 → 2.5D 위반.
    /// 평균 높이(−6)는 소재 내부다 (항목 ①).
    /// </summary>
    /// <param name="lowerZ">하면 높이(mm). 상면과의 간격이 2.5D 임계를 넘어야 한다.</param>
    public static List<Point3D> Overhang(double lowerZ = -12.0, double spacing = 0.6)
    {
        var pts = new List<Point3D>();
        for (double x = 0; x <= 100; x += spacing)
            for (double y = 0; y <= 60; y += spacing)
            {
                pts.Add(new Point3D(x, y, 0));                       // 상면 (전체)
                if (x >= 30 && x <= 70) pts.Add(new Point3D(x, y, lowerZ));  // 하면 (부분)
            }
        return pts;
    }

    /// <summary>
    /// 오목 원통 홈 — 반경 R 의 원통 내면. 축은 X, 축선은 (y=30, z=+R).
    /// 법선이 축선을 향하므로 오목이다. 다층 패스 오프셋 접힘 검증용 (항목 ④).
    /// </summary>
    public static List<Point3D> ConcaveCylinder(double radius = 20.0, double halfAngleDeg = 45,
        double spacing = 0.6)
    {
        var pts = new List<Point3D>();
        double half = halfAngleDeg * Math.PI / 180.0;
        double dTheta = spacing / radius;
        for (double x = 0; x <= 100; x += spacing)
            for (double t = -half; t <= half + 1e-9; t += dTheta)
                pts.Add(new Point3D(x, 30 + radius * Math.Sin(t), radius - radius * Math.Cos(t)));
        return pts;
    }

    /// <summary>
    /// 볼록 원통 — ConcaveCylinder 의 부호 반전. 오목 판정이 볼록에서 걸리지 않는지 확인용.
    /// </summary>
    public static List<Point3D> ConvexCylinder(double radius = 20.0, double halfAngleDeg = 45,
        double spacing = 0.6)
        => ConcaveCylinder(radius, halfAngleDeg, spacing)
            .Select(p => new Point3D(p.X, p.Y, -p.Z)).ToList();

    /// <summary>
    /// 국부 급경사 평면 — 100×60 평면에 X ∈ [90,100] 구간만 60° 로 솟은 시편.
    /// 밴드 신장률 상한 검증용 (항목 ⑧). 상한이 없으면 평탄부까지 라인이 조밀해진다.
    /// </summary>
    public static List<Point3D> PlaneWithSteepEdge(double spacing = 0.6)
    {
        var pts = new List<Point3D>();
        double tan60 = Math.Tan(60 * Math.PI / 180.0);
        for (double x = 0; x <= 100; x += spacing)
            for (double y = 0; y <= 60; y += spacing)
                pts.Add(new Point3D(x, y, x <= 90 ? 0 : (x - 90) * tan60));
        return pts;
    }

    /// <summary>점군 전체를 하나의 영역으로 쓰는 인덱스 목록.</summary>
    public static List<int> AllIndices(IReadOnlyList<Point3D> pts)
        => Enumerable.Range(0, pts.Count).ToList();

    /// <summary>표준 전처리 — 복셀 1mm → 이상치 제거 → PCA 법선(k=20).</summary>
    public static (List<Point3D> Points, List<Vector3D> Normals) Preprocess(
        IReadOnlyList<Point3D> raw, Point3D? viewpoint = null)
    {
        var result = new CloudPreprocessService().Process(raw, viewpoint ?? Viewpoint,
            new PreprocessOptions());
        return (result.Points.ToList(), result.Normals.ToList());
    }
}
