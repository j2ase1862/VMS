using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Services;

/// <summary>점군 전처리 파라미터 — 그라인딩 스캔 명세 Step S1 (기본값으로 동작해야 한다).</summary>
public record PreprocessOptions
{
    /// <summary>복셀 다운샘플 격자 크기(mm) — 밀도 균일화 목표 해상도.</summary>
    public double VoxelSizeMm { get; init; } = 1.0;
    /// <summary>이상치 판정용 k-NN 이웃 수.</summary>
    public int OutlierK { get; init; } = 16;
    /// <summary>이상치 임계 = 평균 이웃거리 μ + ratio·σ.</summary>
    public double OutlierStdRatio { get; init; } = 2.0;
    /// <summary>법선 추정용 k-NN 이웃 수.</summary>
    public int NormalK { get; init; } = 20;
}

/// <summary>전처리 결과 — 정제 점군 + 지점별 법선(1:1) + 단계별 통계.</summary>
public record PreprocessedCloud(
    List<Point3D> Points,
    List<Vector3D> Normals,
    int RawCount,
    int VoxelCount,
    int OutlierRemovedCount)
{
    public string Summary =>
        $"점 {RawCount:N0} → 복셀 {VoxelCount:N0} → 이상치 −{OutlierRemovedCount:N0} → {Points.Count:N0}";
}

/// <summary>
/// 그라인딩 스캔 명세 Step S1 — 점군 전처리 및 법선 추정.
/// 복셀 다운샘플 → 통계적 이상치 제거 → PCA k-NN 법선 추정(시점 방향 일관화).
/// 순수 C# — 외부 점군 라이브러리 없이 KdTree3 공용 자산을 사용한다.
/// </summary>
public class CloudPreprocessService
{
    /// <summary>전처리 전체 파이프라인. viewpoint 는 스캔 카메라 원점(법선 방향 일관화 기준).</summary>
    public PreprocessedCloud Process(IReadOnlyList<Point3D> raw, Point3D viewpoint,
        PreprocessOptions? options = null)
    {
        var opt = options ?? new PreprocessOptions();
        if (raw.Count < 10)
            throw new InvalidOperationException("전처리에 필요한 점이 부족합니다 (≥10).");

        var voxeled = VoxelDownsample(raw, opt.VoxelSizeMm);
        var (kept, removed) = RemoveStatisticalOutliers(voxeled, opt.OutlierK, opt.OutlierStdRatio);
        var normals = EstimateNormals(kept, opt.NormalK, viewpoint);
        return new PreprocessedCloud(kept, normals, raw.Count, voxeled.Count, removed);
    }

    /// <summary>복셀 격자별 무게중심으로 다운샘플 — 밀도 균일화 + 노이즈 완화(격자 내 평균).</summary>
    public static List<Point3D> VoxelDownsample(IReadOnlyList<Point3D> pts, double voxelSizeMm)
    {
        if (voxelSizeMm <= 0) return pts.ToList();
        var cells = new Dictionary<(int, int, int), (double X, double Y, double Z, int N)>(pts.Count / 2);
        double inv = 1.0 / voxelSizeMm;
        foreach (var p in pts)
        {
            var key = ((int)Math.Floor(p.X * inv), (int)Math.Floor(p.Y * inv), (int)Math.Floor(p.Z * inv));
            cells[key] = cells.TryGetValue(key, out var a)
                ? (a.X + p.X, a.Y + p.Y, a.Z + p.Z, a.N + 1)
                : (p.X, p.Y, p.Z, 1);
        }
        var result = new List<Point3D>(cells.Count);
        foreach (var a in cells.Values)
            result.Add(new Point3D(a.X / a.N, a.Y / a.N, a.Z / a.N));
        return result;
    }

    /// <summary>
    /// 통계적 이상치 제거 — 각 점의 k-NN 평균 거리를 구해 전체 분포의 μ + ratio·σ 를
    /// 초과하는 고립점을 버린다. 반환: (남은 점, 제거 수).
    /// </summary>
    public static (List<Point3D> Kept, int Removed) RemoveStatisticalOutliers(
        IReadOnlyList<Point3D> pts, int k, double stdRatio)
    {
        if (pts.Count <= k + 1) return (pts.ToList(), 0);

        var tree = new KdTree3(pts);
        var buf = new KdNeighbor[k + 1];
        var meanDist = new double[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            int n = tree.KNearest(pts[i], k + 1, buf);
            double sum = 0;
            int cnt = 0;
            for (int j = 0; j < n; j++)
            {
                if (buf[j].Index == i) continue;   // 자기 자신 제외
                sum += Math.Sqrt(buf[j].DistSq);
                cnt++;
            }
            meanDist[i] = cnt > 0 ? sum / cnt : 0;
        }

        double mu = meanDist.Average();
        double sigma = Math.Sqrt(meanDist.Sum(d => (d - mu) * (d - mu)) / meanDist.Length);
        // 복셀 다운샘플 후에는 밀도가 균일해 σ→0 — 경계 점(이웃 거리 ~1.3μ)까지 잘려
        // 커버리지 경계 라인이 사라진다. μ 비례 하한(+50%)으로 경계는 보존하고,
        // 진짜 부유 이상치(수 배 μ)만 제거되게 한다.
        double limit = mu + Math.Max(stdRatio * sigma, 0.5 * mu);

        var kept = new List<Point3D>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
            if (meanDist[i] <= limit) kept.Add(pts[i]);
        return (kept, pts.Count - kept.Count);
    }

    /// <summary>
    /// PCA k-NN 법선 추정 — 각 점의 이웃 공분산 최소 고유벡터를 법선으로 취하고,
    /// 스캔 시점(카메라) 방향으로 부호를 일관화한다 (단일 뷰 스캔 전제 — MST 전파 불필요).
    /// </summary>
    public static List<Vector3D> EstimateNormals(IReadOnlyList<Point3D> pts, int k, Point3D viewpoint)
    {
        var tree = new KdTree3(pts);
        var buf = new KdNeighbor[k + 1];
        var normals = new List<Vector3D>(pts.Count);

        for (int i = 0; i < pts.Count; i++)
        {
            int n = tree.KNearest(pts[i], k + 1, buf);

            // 이웃(자신 포함) 무게중심
            double cx = 0, cy = 0, cz = 0;
            for (int j = 0; j < n; j++)
            {
                var p = pts[buf[j].Index];
                cx += p.X; cy += p.Y; cz += p.Z;
            }
            cx /= n; cy /= n; cz /= n;

            // 공분산 (대칭 3×3)
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int j = 0; j < n; j++)
            {
                var p = pts[buf[j].Index];
                double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
                xx += dx * dx; xy += dx * dy; xz += dx * dz;
                yy += dy * dy; yz += dy * dz; zz += dz * dz;
            }

            var normal = n >= 3
                ? SmallestEigenvector3(xx, xy, xz, yy, yz, zz)
                : new Vector3D(0, 0, 1);   // 퇴화 — 임시 상향 (아래 시점 일관화가 보정)

            // 시점 방향 일관화
            var toView = viewpoint - pts[i];
            if (Vector3D.DotProduct(normal, toView) < 0) normal = -normal;
            normals.Add(normal);
        }
        return normals;
    }

    /// <summary>대칭 3×3 의 최소 고유값 고유벡터 — 야코비 회전법 (Horn 4×4 와 동일 접근).</summary>
    private static Vector3D SmallestEigenvector3(
        double xx, double xy, double xz, double yy, double yz, double zz)
    {
        var a = new double[3, 3] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
        var v = new double[3, 3];
        for (int i = 0; i < 3; i++) v[i, i] = 1;

        for (int sweep = 0; sweep < 50; sweep++)
        {
            int p = 0, q = 1;
            double max = 0;
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if (Math.Abs(a[i, j]) > max) { max = Math.Abs(a[i, j]); p = i; q = j; }
            if (max < 1e-12) break;

            double theta = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
            double c = Math.Cos(theta), s = Math.Sin(theta);
            for (int i = 0; i < 3; i++)
            {
                double aip = a[i, p], aiq = a[i, q];
                a[i, p] = c * aip - s * aiq;
                a[i, q] = s * aip + c * aiq;
            }
            for (int j = 0; j < 3; j++)
            {
                double apj = a[p, j], aqj = a[q, j];
                a[p, j] = c * apj - s * aqj;
                a[q, j] = s * apj + c * aqj;
            }
            for (int i = 0; i < 3; i++)
            {
                double vip = v[i, p], viq = v[i, q];
                v[i, p] = c * vip - s * viq;
                v[i, q] = s * vip + c * viq;
            }
        }

        int best = 0;
        for (int i = 1; i < 3; i++) if (a[i, i] < a[best, best]) best = i;
        var vec = new Vector3D(v[0, best], v[1, best], v[2, best]);
        if (vec.Length < 1e-9) return new Vector3D(0, 0, 1);
        vec.Normalize();
        return vec;
    }
}
