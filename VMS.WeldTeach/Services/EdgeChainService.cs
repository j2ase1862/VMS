using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;

namespace VMS.WeldTeach.Services;

/// <summary>
/// 명세서 Step 2 — 위상 추적 및 연속 엣지 자동 체이닝.
/// 선택 엣지에서 시작해 끝점이 맞닿고 접선이 C1 연속(기본 15° 이내)인 엣지를 양방향으로 이어 붙인다.
/// </summary>
public class EdgeChainService
{
    public double AngleToleranceDeg { get; set; } = 15.0;
    public double JoinTolerance { get; set; } = 1e-4;

    public WeldingPathContour BuildChain(CadModelData model, int seedEdgeId)
    {
        var byId = model.Edges.ToDictionary(e => e.EdgeId);
        var seed = byId[seedEdgeId];

        // 체인은 (엣지, 순방향 여부) 목록 — 순방향 = 엣지 폴리라인 순서대로 진행
        var chain = new LinkedList<(CadEdgeInfo Edge, bool Forward)>();
        chain.AddFirst((seed, true));
        var used = new HashSet<int> { seed.EdgeId };

        if (!seed.IsClosed)
        {
            Extend(chain, byId.Values, used, atTail: true);
            Extend(chain, byId.Values, used, atTail: false);
        }

        return ToContour(chain);
    }

    private void Extend(LinkedList<(CadEdgeInfo Edge, bool Forward)> chain,
        IEnumerable<CadEdgeInfo> all, HashSet<int> used, bool atTail)
    {
        while (true)
        {
            var (edge, forward) = atTail ? chain.Last!.Value : chain.First!.Value;
            // 진행 끝점과 그 지점의 진행 접선
            Point3D endPt = atTail
                ? (forward ? edge.EndPoint : edge.StartPoint)
                : (forward ? edge.StartPoint : edge.EndPoint);
            Vector3D outTan = atTail
                ? (forward ? edge.EndTangent : Neg(edge.StartTangent))
                : (forward ? Neg(edge.StartTangent) : edge.EndTangent);

            (CadEdgeInfo Edge, bool Forward)? best = null;
            double bestAngle = AngleToleranceDeg;
            foreach (var cand in all)
            {
                if (used.Contains(cand.EdgeId) || cand.IsClosed) continue;
                // 후보의 어느 끝이 붙는가
                foreach (bool candForward in new[] { true, false })
                {
                    Point3D candStart = candForward ? cand.StartPoint : cand.EndPoint;
                    if ((candStart - endPt).Length > JoinTolerance) continue;
                    // 후보 진입 접선 (진행 방향 기준)
                    Vector3D candTan = candForward ? cand.StartTangent : Neg(cand.EndTangent);
                    double angle = AngleDeg(outTan, candTan);
                    if (angle < bestAngle)
                    {
                        bestAngle = angle;
                        best = (cand, candForward);
                    }
                }
            }
            if (best == null) return;

            used.Add(best.Value.Edge.EdgeId);
            if (atTail) chain.AddLast(best.Value);
            else chain.AddFirst((best.Value.Edge, !best.Value.Forward));
            // head 쪽은 진행 방향이 반대이므로 방향 플래그를 뒤집어 저장:
            // head 확장에서 cand 는 "endPt 에서 시작해 바깥으로" 진행하는 방향(candForward)으로 찾았고,
            // 체인 정방향(head→tail) 기준으로는 그 반대 방향으로 지나가게 된다.
        }
    }

    private WeldingPathContour ToContour(LinkedList<(CadEdgeInfo Edge, bool Forward)> chain)
    {
        var contour = new WeldingPathContour { PathId = $"PATH_{chain.First!.Value.Edge.EdgeId}" };
        double total = 0;
        foreach (var (edge, forward) in chain)
        {
            var pts = forward ? edge.Points : Enumerable.Reverse(edge.Points).ToList();
            // 이등분 벡터는 Points 와 1:1 — 같은 순서로 뒤집어 함께 나른다
            var bis = edge.PointBisectors.Count == edge.Points.Count
                ? (forward ? edge.PointBisectors : Enumerable.Reverse(edge.PointBisectors).ToList())
                : Enumerable.Repeat(default(Vector3D), pts.Count).ToList();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (contour.PathPoints.Count > 0)
                {
                    var d = (p - contour.PathPoints[^1]).Length;
                    if (d < 1e-9) continue;   // 이음새 중복점 제거
                    total += d;
                }
                contour.PathPoints.Add(p);
                contour.PointBisectors.Add(bis[i]);
            }
            contour.EdgeIds.Add(edge.EdgeId);
        }
        // 접선 = 인접 점 차분 (양 끝은 단방향 차분)
        for (int i = 0; i < contour.PathPoints.Count; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(contour.PathPoints.Count - 1, i + 1);
            var t = contour.PathPoints[b] - contour.PathPoints[a];
            if (t.Length > 1e-12) t.Normalize();
            contour.TangentVectors.Add(t);
        }
        contour.TotalLength = total;
        return contour;
    }

    private static Vector3D Neg(Vector3D v) => new(-v.X, -v.Y, -v.Z);

    private static double AngleDeg(Vector3D a, Vector3D b)
    {
        var dot = Math.Clamp(Vector3D.DotProduct(a, b), -1.0, 1.0);
        return Math.Acos(dot) * 180.0 / Math.PI;
    }
}
