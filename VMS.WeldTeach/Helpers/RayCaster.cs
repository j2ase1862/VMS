using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;

namespace VMS.WeldTeach.Helpers;

/// <summary>
/// 명세서 Step 1 — Ray 와 엣지 폴리라인 선분 간 최단 거리로 엣지를 피킹한다.
/// </summary>
public static class RayCaster
{
    /// <summary>Ray 에 가장 가까운 엣지를 찾는다. 임계값(threshold, 모델 단위) 밖이면 null.</summary>
    public static CadEdgeInfo? PickEdge(Point3D origin, Vector3D direction, IEnumerable<CadEdgeInfo> edges, double threshold)
    {
        CadEdgeInfo? best = null;
        double bestDist = threshold;
        foreach (var edge in edges)
        {
            for (int i = 0; i < edge.Points.Count - 1; i++)
            {
                double d = RaySegmentDistance(origin, direction, edge.Points[i], edge.Points[i + 1]);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = edge;
                }
            }
        }
        return best;
    }

    /// <summary>반직선(origin+t*dir, t≥0)과 선분 [a,b] 간 최단 거리.</summary>
    public static double RaySegmentDistance(Point3D origin, Vector3D dir, Point3D a, Point3D b)
    {
        var u = dir;                    // ray 방향 (단위 가정)
        var v = b - a;                  // 선분 방향
        var w0 = origin - a;

        double aa = Vector3D.DotProduct(u, u);
        double bb = Vector3D.DotProduct(u, v);
        double cc = Vector3D.DotProduct(v, v);
        double dd = Vector3D.DotProduct(u, w0);
        double ee = Vector3D.DotProduct(v, w0);
        double denom = aa * cc - bb * bb;

        double s, t;                    // s: ray 파라미터, t: 선분 파라미터 [0,1]
        if (denom < 1e-12)
        {
            s = 0;
            t = cc > 1e-12 ? ee / cc : 0;
        }
        else
        {
            s = (bb * ee - cc * dd) / denom;
            t = (aa * ee - bb * dd) / denom;
        }
        s = Math.Max(0, s);
        t = Math.Clamp(t, 0, 1);
        // s 재보정 (t 클램프 후)
        if (cc > 1e-12)
        {
            var pt = a + t * v;
            s = Math.Max(0, Vector3D.DotProduct(pt - origin, u) / aa);
        }
        var closestRay = origin + s * u;
        var closestSeg = a + t * v;
        return (closestRay - closestSeg).Length;
    }
}
