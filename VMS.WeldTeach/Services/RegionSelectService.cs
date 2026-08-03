using System.Windows;
using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Services;

/// <summary>화면 투영 결과 — 화면 좌표(px) + 카메라 시선 방향 깊이(mm). Visible=false 는 카메라 뒤.</summary>
public readonly record struct ProjectedPoint(double X, double Y, double DepthMm, bool Visible);

/// <summary>
/// 그라인딩 스캔 명세 Step S2 — 점군 위 가공 영역 선택 (라쏘/브러시).
/// 뷰가 만든 투영 델리게이트(월드→화면)로 화면 공간에서 판정한다 — UI 컨트롤 참조 없음.
/// 깊이 밴드: 선택 후보의 전방(5% 분위) 깊이 + bandMm 이내만 남겨 뒤쪽 표면(가림 점)을
/// 걸러낸다 (기본 "표면 첫 레이어만" 동작). bandMm ≤ 0 이면 깊이 필터 없음.
/// </summary>
public static class RegionSelectService
{
    public delegate ProjectedPoint ProjectFunc(Point3D worldPoint);

    /// <summary>폴리곤 라쏘 선택 — 화면 다각형 내부 점의 인덱스 (깊이 밴드 적용).</summary>
    public static List<int> SelectByPolygon(IReadOnlyList<Point3D> points, ProjectFunc project,
        IReadOnlyList<Point> polygon, double depthBandMm)
    {
        if (polygon.Count < 3 || points.Count == 0) return new List<int>();

        // 화면 바운딩박스 선별로 다각형 판정 횟수를 줄인다
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var v in polygon)
        {
            minX = Math.Min(minX, v.X); minY = Math.Min(minY, v.Y);
            maxX = Math.Max(maxX, v.X); maxY = Math.Max(maxY, v.Y);
        }

        var hits = new List<(int Idx, double Depth)>();
        for (int i = 0; i < points.Count; i++)
        {
            var pr = project(points[i]);
            if (!pr.Visible) continue;
            if (pr.X < minX || pr.X > maxX || pr.Y < minY || pr.Y > maxY) continue;
            if (PointInPolygon(pr.X, pr.Y, polygon))
                hits.Add((i, pr.DepthMm));
        }
        return FilterFirstLayer(hits, depthBandMm);
    }

    /// <summary>브러시 스트로크 선택 — 드래그 궤적(중심점들) 반경 radiusPx 이내 점의 인덱스.</summary>
    public static List<int> SelectByStroke(IReadOnlyList<Point3D> points, ProjectFunc project,
        IReadOnlyList<Point> strokeCenters, double radiusPx, double depthBandMm)
    {
        if (strokeCenters.Count == 0 || points.Count == 0 || radiusPx <= 0) return new List<int>();

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var c in strokeCenters)
        {
            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
        }
        minX -= radiusPx; minY -= radiusPx; maxX += radiusPx; maxY += radiusPx;
        double rSq = radiusPx * radiusPx;

        var hits = new List<(int Idx, double Depth)>();
        for (int i = 0; i < points.Count; i++)
        {
            var pr = project(points[i]);
            if (!pr.Visible) continue;
            if (pr.X < minX || pr.X > maxX || pr.Y < minY || pr.Y > maxY) continue;
            foreach (var c in strokeCenters)
            {
                double dx = pr.X - c.X, dy = pr.Y - c.Y;
                if (dx * dx + dy * dy <= rSq)
                {
                    hits.Add((i, pr.DepthMm));
                    break;
                }
            }
        }
        return FilterFirstLayer(hits, depthBandMm);
    }

    /// <summary>
    /// 첫 레이어 필터 — 후보 깊이의 전방 5% 분위를 기준면으로 잡고 + bandMm 이내만 남긴다.
    /// (최솟값 대신 분위수 — 노이즈·이상치 한 점이 기준을 끌어내리지 않도록)
    /// </summary>
    private static List<int> FilterFirstLayer(List<(int Idx, double Depth)> hits, double bandMm)
    {
        if (bandMm <= 0 || hits.Count == 0)
            return hits.Select(h => h.Idx).ToList();

        var depths = hits.Select(h => h.Depth).ToArray();
        Array.Sort(depths);
        double front = depths[(int)(depths.Length * 0.05)];
        double limit = front + bandMm;
        return hits.Where(h => h.Depth <= limit).Select(h => h.Idx).ToList();
    }

    /// <summary>점-다각형 내부 판정 (ray casting).</summary>
    public static bool PointInPolygon(double x, double y, IReadOnlyList<Point> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if (a.Y > y != b.Y > y &&
                x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
