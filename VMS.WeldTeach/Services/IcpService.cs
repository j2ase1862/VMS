using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Services;

/// <summary>ICP 정합 결과. CadToScan 이 명세서의 T_align (CAD 경로 → 로봇/스캔 좌표).</summary>
public record IcpResult(Matrix3D CadToScan, double RmseMm, double InlierRatio, int Iterations, bool Converged);

/// <summary>
/// 명세서 Step 3-2 — 비전 점군 정합 (자체 구현 point-to-point ICP).
/// 스캔(부분) → CAD 샘플(전체) 방향으로 대응을 잡아 추정 후 역변환으로 T_align 을 만든다.
/// 대응은 k-d 트리 최근접, 강체 추정은 Horn 쿼터니언법, 이상치는 트리밍(기본 상위 20% 제외).
/// 초기 정렬은 무게중심 일치 — 큰 회전 오프셋(>30°)은 별도 거친 정합 필요 (PoC 범위 외).
/// </summary>
public class IcpService
{
    /// <summary>
    /// 멀티스타트 ICP — 부분 스캔(상면 등)은 큰 평면이 CAD 의 다른 평면에 붙는 국소 최소가
    /// 흔해서(예: 상면↔밑면 +두께 오프셋), 여러 초기값에서 각각 수렴시켜 최저 RMSE 를 채택한다.
    /// 초기값: 무게중심 일치 + 바운딩박스 1/4·1/2 축방향 이동 6종 + 요(Z) 90/180/270°.
    /// </summary>
    public IcpResult Register(IReadOnlyList<Point3D> scanCloud, IReadOnlyList<Point3D> cadSamples,
        int maxIterations = 80, double trimRatio = 0.2, double convergenceMm = 1e-4,
        IReadOnlyList<int>? sampleTriIndex = null,
        IReadOnlyList<(Point3D A, Point3D B, Point3D C)>? triangles = null)
    {
        if (scanCloud.Count < 10 || cadSamples.Count < 10)
            throw new InvalidOperationException("정합에 필요한 점이 부족합니다.");

        var tree = new KdTree3(cadSamples);
        var cadCentroid = Centroid(cadSamples);
        var scanCentroid = Centroid(scanCloud);
        var (bbMin, bbMax) = BoundingBox(cadSamples);
        var ext = bbMax - bbMin;

        // 후보 초기 변환 (스캔 → CAD 방향)
        var candidates = new List<Matrix3D>();
        void AddCandidate(double yawDeg, Vector3D extraOffset)
        {
            var m = Matrix3D.Identity;
            if (yawDeg != 0)
            {
                // 스캔 무게중심 기준 요 회전
                m.Translate(-(Vector3D)scanCentroid);
                m.Rotate(new Quaternion(new Vector3D(0, 0, 1), yawDeg));
                m.Translate((Vector3D)scanCentroid);
            }
            m.Translate(cadCentroid - scanCentroid + extraOffset);
            candidates.Add(m);
        }
        AddCandidate(0, new Vector3D());
        foreach (double f in new[] { 0.25, -0.25, 0.5, -0.5 })
        {
            AddCandidate(0, new Vector3D(ext.X * f, 0, 0));
            AddCandidate(0, new Vector3D(0, ext.Y * f, 0));
            AddCandidate(0, new Vector3D(0, 0, ext.Z * f));
        }
        foreach (double yaw in new[] { 90.0, 180.0, 270.0 })
            AddCandidate(yaw, new Vector3D());

        // 정합은 서브샘플(≤4000점)로 — 멀티스타트 × 반복 비용을 1/10 이하로.
        // 최종 RMSE/인라이어는 전체 점군으로 평가한다.
        IReadOnlyList<Point3D> icpCloud = scanCloud;
        if (scanCloud.Count > 4000)
        {
            int stride = (scanCloud.Count + 3999) / 4000;
            var sub = new List<Point3D>(4000);
            for (int i = 0; i < scanCloud.Count; i += stride) sub.Add(scanCloud[i]);
            icpCloud = sub;
        }

        (Matrix3D Total, double Rmse, int Iters, bool Converged)? best = null;
        foreach (var init in candidates)
        {
            var r = RunIcp(icpCloud, tree, init, maxIterations, trimRatio, convergenceMm);
            if (best == null || r.Rmse < best.Value.Rmse) best = r;
        }
        var (total, rmse, iters, converged) = best!.Value;

        // 최종 RMSE·인라이어 — 삼각형 정보가 있으면 점-표면(점-삼각형) 거리로 평가.
        // 점-점 거리는 CAD 샘플 간격이 하한을 지배해 정렬 오차를 과대평가한다.
        int inliers = 0;
        if (sampleTriIndex != null && triangles != null)
        {
            var surfDist = new double[scanCloud.Count];
            for (int i = 0; i < scanCloud.Count; i++)
            {
                var q = total.Transform(scanCloud[i]);
                int si = tree.NearestIndex(q, out _);
                var (a, b, c) = triangles[sampleTriIndex[si]];
                surfDist[i] = PointTriangleDistance(q, a, b, c);
            }
            var sorted = (double[])surfDist.Clone();
            Array.Sort(sorted);
            int keepN = Math.Max(10, (int)(sorted.Length * (1.0 - trimRatio)));
            double sumSq = 0;
            for (int i = 0; i < keepN; i++) sumSq += sorted[i] * sorted[i];
            rmse = Math.Sqrt(sumSq / keepN);

            double surfThresh = Math.Max(0.3, rmse * 3);
            foreach (var d in surfDist)
                if (d < surfThresh) inliers++;
        }
        else
        {
            double thresh = Math.Max(0.3, rmse * 3);
            foreach (var p in scanCloud)
            {
                tree.Nearest(total.Transform(p), out double d);
                if (d < thresh) inliers++;
            }
        }

        if (!total.HasInverse)
            throw new InvalidOperationException("정합 변환이 특이(singular)합니다.");
        var cadToScan = total;
        cadToScan.Invert();

        return new IcpResult(cadToScan, rmse, (double)inliers / scanCloud.Count, iters, converged);
    }

    private static (Matrix3D Total, double Rmse, int Iters, bool Converged) RunIcp(
        IReadOnlyList<Point3D> scanCloud, KdTree3 tree, Matrix3D init,
        int maxIterations, double trimRatio, double convergenceMm)
    {
        var work = new Point3D[scanCloud.Count];
        for (int i = 0; i < work.Length; i++) work[i] = init.Transform(scanCloud[i]);
        var total = init;

        double rmse = double.MaxValue, prevRmse = double.MaxValue;
        int iter;
        bool converged = false;
        var nearest = new Point3D[work.Length];
        var dist = new double[work.Length];
        var order = new int[work.Length];

        for (iter = 1; iter <= maxIterations; iter++)
        {
            for (int i = 0; i < work.Length; i++)
            {
                nearest[i] = tree.Nearest(work[i], out double d);
                dist[i] = d;
                order[i] = i;
            }
            Array.Sort((double[])dist.Clone(), order);   // dist 사본으로 정렬해 원본 인덱스 유지
            int keep = Math.Max(10, (int)(work.Length * (1.0 - trimRatio)));

            // Horn 쿼터니언법으로 강체 변환 추정 (kept 대응)
            var delta = EstimateRigid(work, nearest, order, keep);
            for (int i = 0; i < work.Length; i++) work[i] = delta.Transform(work[i]);
            total = Matrix3D.Multiply(total, delta);

            double sum = 0;
            for (int k = 0; k < keep; k++)
            {
                int i = order[k];
                sum += (work[i] - nearest[i]).LengthSquared;
            }
            rmse = Math.Sqrt(sum / keep);
            if (Math.Abs(prevRmse - rmse) < convergenceMm) { converged = true; break; }
            prevRmse = rmse;
        }
        return (total, rmse, Math.Min(iter, maxIterations), converged);
    }

    /// <summary>점-삼각형 최단 거리 (Ericson, Real-Time Collision Detection).</summary>
    private static double PointTriangleDistance(Point3D p, Point3D a, Point3D b, Point3D c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        double d1 = Vector3D.DotProduct(ab, ap);
        double d2 = Vector3D.DotProduct(ac, ap);
        if (d1 <= 0 && d2 <= 0) return (p - a).Length;

        var bp = p - b;
        double d3 = Vector3D.DotProduct(ab, bp);
        double d4 = Vector3D.DotProduct(ac, bp);
        if (d3 >= 0 && d4 <= d3) return (p - b).Length;

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            double v = d1 / (d1 - d3);
            return (p - (a + v * ab)).Length;
        }

        var cp = p - c;
        double d5 = Vector3D.DotProduct(ab, cp);
        double d6 = Vector3D.DotProduct(ac, cp);
        if (d6 >= 0 && d5 <= d6) return (p - c).Length;

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            double w = d2 / (d2 - d6);
            return (p - (a + w * ac)).Length;
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return (p - (b + w * (c - b))).Length;
        }

        double denom = 1.0 / (va + vb + vc);
        double v2 = vb * denom, w2 = vc * denom;
        return (p - (a + v2 * ab + w2 * ac)).Length;
    }

    private static (Point3D Min, Point3D Max) BoundingBox(IReadOnlyList<Point3D> pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
        }
        return (new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
    }

    private static Vector3D Centroid(IReadOnlyList<Point3D> pts)
    {
        double x = 0, y = 0, z = 0;
        foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; }
        return new Vector3D(x / pts.Count, y / pts.Count, z / pts.Count);
    }

    /// <summary>kept 대응쌍으로 src→dst 강체 변환 (R, t) 을 Horn 쿼터니언법으로 추정.</summary>
    private static Matrix3D EstimateRigid(Point3D[] src, Point3D[] dst, int[] order, int keep)
    {
        double cx = 0, cy = 0, cz = 0, dx = 0, dy = 0, dz = 0;
        for (int k = 0; k < keep; k++)
        {
            var s = src[order[k]]; var t = dst[order[k]];
            cx += s.X; cy += s.Y; cz += s.Z;
            dx += t.X; dy += t.Y; dz += t.Z;
        }
        cx /= keep; cy /= keep; cz /= keep;
        dx /= keep; dy /= keep; dz /= keep;

        // 교차 공분산 S = Σ a·bᵀ
        var S = new double[3, 3];
        for (int k = 0; k < keep; k++)
        {
            var s = src[order[k]]; var t = dst[order[k]];
            double ax = s.X - cx, ay = s.Y - cy, az = s.Z - cz;
            double bx = t.X - dx, by = t.Y - dy, bz = t.Z - dz;
            S[0, 0] += ax * bx; S[0, 1] += ax * by; S[0, 2] += ax * bz;
            S[1, 0] += ay * bx; S[1, 1] += ay * by; S[1, 2] += ay * bz;
            S[2, 0] += az * bx; S[2, 1] += az * by; S[2, 2] += az * bz;
        }

        // Horn 의 대칭 4x4 N — 최대 고유벡터 = 회전 쿼터니언
        var N = new double[4, 4]
        {
            { S[0,0]+S[1,1]+S[2,2], S[1,2]-S[2,1],        S[2,0]-S[0,2],        S[0,1]-S[1,0]        },
            { S[1,2]-S[2,1],        S[0,0]-S[1,1]-S[2,2], S[0,1]+S[1,0],        S[0,2]+S[2,0]        },
            { S[2,0]-S[0,2],        S[0,1]+S[1,0],       -S[0,0]+S[1,1]-S[2,2], S[1,2]+S[2,1]        },
            { S[0,1]-S[1,0],        S[0,2]+S[2,0],        S[1,2]+S[2,1],       -S[0,0]-S[1,1]+S[2,2] },
        };
        var q = LargestEigenvector4(N);
        double w = q[0], x = q[1], y = q[2], z = q[3];

        // 쿼터니언 → 회전행렬 (열 규약: p' = R·p)
        var R = new double[3, 3]
        {
            { 1-2*(y*y+z*z), 2*(x*y-w*z),   2*(x*z+w*y)   },
            { 2*(x*y+w*z),   1-2*(x*x+z*z), 2*(y*z-w*x)   },
            { 2*(x*z-w*y),   2*(y*z+w*x),   1-2*(x*x+y*y) },
        };
        // t = c_dst − R·c_src
        double tx = dx - (R[0, 0] * cx + R[0, 1] * cy + R[0, 2] * cz);
        double ty = dy - (R[1, 0] * cx + R[1, 1] * cy + R[1, 2] * cz);
        double tz = dz - (R[2, 0] * cx + R[2, 1] * cy + R[2, 2] * cz);

        // WPF Matrix3D 는 행벡터 규약(p' = p·M) — Mij = R[j,i]
        return new Matrix3D(
            R[0, 0], R[1, 0], R[2, 0], 0,
            R[0, 1], R[1, 1], R[2, 1], 0,
            R[0, 2], R[1, 2], R[2, 2], 0,
            tx, ty, tz, 1);
    }

    /// <summary>대칭 4x4 의 최대 고유값 고유벡터 — 야코비 회전법.</summary>
    private static double[] LargestEigenvector4(double[,] a)
    {
        var v = new double[4, 4];
        for (int i = 0; i < 4; i++) v[i, i] = 1;

        for (int sweep = 0; sweep < 50; sweep++)
        {
            // 최대 비대각 원소
            int p = 0, q = 1;
            double max = 0;
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (Math.Abs(a[i, j]) > max) { max = Math.Abs(a[i, j]); p = i; q = j; }
            if (max < 1e-12) break;

            double theta = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
            double c = Math.Cos(theta), s = Math.Sin(theta);
            for (int i = 0; i < 4; i++)
            {
                double aip = a[i, p], aiq = a[i, q];
                a[i, p] = c * aip - s * aiq;
                a[i, q] = s * aip + c * aiq;
            }
            for (int j = 0; j < 4; j++)
            {
                double apj = a[p, j], aqj = a[q, j];
                a[p, j] = c * apj - s * aqj;
                a[q, j] = s * apj + c * aqj;
            }
            for (int i = 0; i < 4; i++)
            {
                double vip = v[i, p], viq = v[i, q];
                v[i, p] = c * vip - s * viq;
                v[i, q] = s * vip + c * viq;
            }
        }
        int best = 0;
        for (int i = 1; i < 4; i++) if (a[i, i] > a[best, best]) best = i;
        var vec = new double[4];
        double norm = 0;
        for (int i = 0; i < 4; i++) { vec[i] = v[i, best]; norm += vec[i] * vec[i]; }
        norm = Math.Sqrt(norm);
        for (int i = 0; i < 4; i++) vec[i] /= norm;
        return vec;
    }
}

/// <summary>정적 3D k-d 트리 — 최근접 탐색 전용 (배열 기반, 재귀 빌드).</summary>
public class KdTree3
{
    private readonly Point3D[] _pts;
    private readonly int[] _idx;

    public KdTree3(IReadOnlyList<Point3D> points)
    {
        _pts = points.ToArray();
        _idx = Enumerable.Range(0, _pts.Length).ToArray();
        Build(0, _pts.Length - 1, 0);
    }

    private void Build(int lo, int hi, int depth)
    {
        if (lo >= hi) return;
        int axis = depth % 3;
        int mid = (lo + hi) / 2;
        NthElement(lo, hi, mid, axis);
        Build(lo, mid - 1, depth + 1);
        Build(mid + 1, hi, depth + 1);
    }

    // quickselect — _idx[lo..hi] 에서 mid 위치에 axis 기준 중앙값 배치
    private void NthElement(int lo, int hi, int mid, int axis)
    {
        while (lo < hi)
        {
            double pivot = Coord(_idx[(lo + hi) / 2], axis);
            int i = lo, j = hi;
            while (i <= j)
            {
                while (Coord(_idx[i], axis) < pivot) i++;
                while (Coord(_idx[j], axis) > pivot) j--;
                if (i <= j) { (_idx[i], _idx[j]) = (_idx[j], _idx[i]); i++; j--; }
            }
            if (mid <= j) hi = j;
            else if (mid >= i) lo = i;
            else return;
        }
    }

    private double Coord(int i, int axis) => axis switch
    {
        0 => _pts[i].X,
        1 => _pts[i].Y,
        _ => _pts[i].Z,
    };

    public Point3D Nearest(Point3D query, out double distance)
    {
        int idx = NearestIndex(query, out distance);
        return _pts[idx];
    }

    public int NearestIndex(Point3D query, out double distance)
    {
        double best = double.MaxValue;
        int bestIdx = -1;
        Search(0, _pts.Length - 1, 0, query, ref best, ref bestIdx);
        distance = Math.Sqrt(best);
        return bestIdx;
    }

    private void Search(int lo, int hi, int depth, Point3D q, ref double bestSq, ref int bestIdx)
    {
        if (lo > hi) return;
        int mid = (lo + hi) / 2;
        var p = _pts[_idx[mid]];
        double dsq = (p - q).LengthSquared;
        if (dsq < bestSq) { bestSq = dsq; bestIdx = _idx[mid]; }

        int axis = depth % 3;
        double diff = axis switch { 0 => q.X - p.X, 1 => q.Y - p.Y, _ => q.Z - p.Z };
        if (diff <= 0)
        {
            Search(lo, mid - 1, depth + 1, q, ref bestSq, ref bestIdx);
            if (diff * diff < bestSq) Search(mid + 1, hi, depth + 1, q, ref bestSq, ref bestIdx);
        }
        else
        {
            Search(mid + 1, hi, depth + 1, q, ref bestSq, ref bestIdx);
            if (diff * diff < bestSq) Search(lo, mid - 1, depth + 1, q, ref bestSq, ref bestIdx);
        }
    }
}
