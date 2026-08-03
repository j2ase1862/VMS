using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Services;

/// <summary>합성 표면 종류 — 그라인딩 명세 §6 검증 시편.</summary>
public enum SampleSurfaceKind
{
    /// <summary>수평 평면 패널 (z=0).</summary>
    Plane,
    /// <summary>X축 기준 30° 경사 평면 — 스텝오버 3D 보정 검증용.</summary>
    InclinedPlane,
    /// <summary>원통 셸 부분 뷰 (반경 80, ±40°) — 곡면 추종·법선 검증용.</summary>
    CylinderShell,
    /// <summary>사인 범프 패널 (진폭 5, 파장 50) — 자유곡면 근사 검증용.</summary>
    SineBump,
    /// <summary>중앙 원형 구멍(r=15) 평면 패널 — 스캔라인 분할 검증용.</summary>
    HolePanel,
}

/// <summary>
/// 합성 점군 — 점 + 해석적 참값 법선/표면거리 평가 함수. 전처리(다운샘플)나 경로 생성으로
/// 점 위치가 바뀌어도 임의 위치에서 참값과의 오차를 수치화할 수 있다.
/// </summary>
/// <param name="TrueDeviationAt">해당 위치에서 참값 표면까지의 최단 거리(mm) 근사 —
/// 커버리지 경로의 표면 추종 편차 검증용.</param>
public record SampleCloud(
    string Name,
    List<Point3D> Points,
    Point3D Viewpoint,
    Func<Point3D, Vector3D> TrueNormalAt,
    Func<Point3D, double> TrueDeviationAt);

/// <summary>
/// 그라인딩 스캔 명세 §6 — CAD·실물 없이 전 단계를 정량 검증하기 위한 합성 점군 생성기
/// (용접의 GenerateSampleStep 에 대응). 결정적(고정 시드) — 회귀 비교 가능.
/// 시편은 100×60 mm 패널, 카메라 시점은 패널 상방 (50, 30, 500).
/// </summary>
public static class SampleCloudGenerator
{
    /// <summary>시편 패널 X 방향 크기 (mm).</summary>
    public const double PanelW = 100.0;
    /// <summary>시편 패널 Y 방향 크기 (mm).</summary>
    public const double PanelH = 60.0;
    /// <summary>원통 셸 반경 (mm) — 축은 X, 축선은 (y=0, z=−R).</summary>
    public const double CylRadius = 80.0;
    /// <summary>원통 셸 부분 뷰 반각 (°).</summary>
    public const double CylHalfAngleDeg = 40.0;
    /// <summary>사인 범프 진폭 (mm).</summary>
    public const double BumpAmp = 5.0;
    /// <summary>사인 범프 파장 (mm).</summary>
    public const double BumpWaveLen = 50.0;
    /// <summary>구멍 패널의 중앙 원형 구멍 반경 (mm).</summary>
    public const double HoleRadius = 15.0;

    /// <summary>
    /// 합성 점군 생성 — spacing 격자 + 지터, 등방 가우시안 노이즈(σ mm),
    /// outlierCount 개의 부유 이상치(표면 상방 5~20 mm)를 주입한다.
    /// </summary>
    public static SampleCloud Generate(SampleSurfaceKind kind, double noiseSigmaMm = 0.0,
        int outlierCount = 0, double spacingMm = 0.6, int seed = 20260803)
    {
        var rng = new Random(seed);
        double Gauss() // Box-Muller
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        var pts = new List<Point3D>();
        Func<Point3D, Vector3D> normalAt;
        Func<Point3D, double> deviationAt;

        switch (kind)
        {
            case SampleSurfaceKind.Plane:
            case SampleSurfaceKind.HolePanel:
            {
                bool hole = kind == SampleSurfaceKind.HolePanel;
                var center = new Point3D(PanelW / 2, PanelH / 2, 0);
                ForGrid(spacingMm, rng, (x, y) =>
                {
                    if (hole && (new Point3D(x, y, 0) - center).Length < HoleRadius) return;
                    pts.Add(new Point3D(x, y, 0));
                });
                normalAt = _ => new Vector3D(0, 0, 1);
                deviationAt = p => Math.Abs(p.Z);
                break;
            }
            case SampleSurfaceKind.InclinedPlane:
            {
                double rad = 30.0 * Math.PI / 180.0;
                double c = Math.Cos(rad), s = Math.Sin(rad);
                ForGrid(spacingMm, rng, (x, y) => pts.Add(new Point3D(x, y * c, y * s)));
                normalAt = _ => new Vector3D(0, -s, c);
                deviationAt = p => Math.Abs(-s * p.Y + c * p.Z);   // 평면 법선 방향 성분
                break;
            }
            case SampleSurfaceKind.CylinderShell:
            {
                // 축 = X 방향, 축선은 (y=0, z=−R) — 원통 상단이 z=0 에 접한다.
                double half = CylHalfAngleDeg * Math.PI / 180.0;
                double arcLen = 2 * half * CylRadius;
                int nPhi = (int)(arcLen / spacingMm);
                int nX = (int)(PanelW / spacingMm);
                for (int ix = 0; ix <= nX; ix++)
                    for (int ip = 0; ip <= nPhi; ip++)
                    {
                        double x = Jitter(ix * spacingMm, spacingMm, rng);
                        double phi = -half + 2 * half * ip / nPhi
                                     + (rng.NextDouble() - 0.5) * 0.8 * (2 * half / nPhi);
                        pts.Add(new Point3D(x,
                            CylRadius * Math.Sin(phi),
                            CylRadius * Math.Cos(phi) - CylRadius));
                    }
                normalAt = p =>
                {
                    var n = new Vector3D(0, p.Y, p.Z + CylRadius);
                    if (n.Length < 1e-9) return new Vector3D(0, 0, 1);
                    n.Normalize();
                    return n;
                };
                deviationAt = p => Math.Abs(
                    Math.Sqrt(p.Y * p.Y + (p.Z + CylRadius) * (p.Z + CylRadius)) - CylRadius);
                break;
            }
            case SampleSurfaceKind.SineBump:
            {
                double k = 2 * Math.PI / BumpWaveLen;
                ForGrid(spacingMm, rng, (x, y) =>
                    pts.Add(new Point3D(x, y, BumpAmp * Math.Sin(k * x))));
                normalAt = p =>
                {
                    var n = new Vector3D(-BumpAmp * k * Math.Cos(k * p.X), 0, 1);
                    n.Normalize();
                    return n;
                };
                // 수직 오차를 국부 경사로 나눠 최단 거리로 환산 (완만한 곡면 근사)
                deviationAt = p =>
                {
                    double slope = BumpAmp * k * Math.Cos(k * p.X);
                    return Math.Abs(p.Z - BumpAmp * Math.Sin(k * p.X)) / Math.Sqrt(1 + slope * slope);
                };
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (noiseSigmaMm > 0)
            for (int i = 0; i < pts.Count; i++)
                pts[i] = new Point3D(
                    pts[i].X + Gauss() * noiseSigmaMm,
                    pts[i].Y + Gauss() * noiseSigmaMm,
                    pts[i].Z + Gauss() * noiseSigmaMm);

        // 부유 이상치 — 표면에서 충분히 떨어진(5~20 mm 상방) 고립점.
        // 전처리의 통계적 이상치 제거가 걸러내야 한다.
        for (int i = 0; i < outlierCount; i++)
            pts.Add(new Point3D(
                rng.NextDouble() * PanelW,
                rng.NextDouble() * PanelH,
                5.0 + rng.NextDouble() * 15.0));

        return new SampleCloud(kind.ToString(), pts,
            new Point3D(PanelW / 2, PanelH / 2, 500), normalAt, deviationAt);
    }

    private static void ForGrid(double spacing, Random rng, Action<double, double> emit)
    {
        int nx = (int)(PanelW / spacing), ny = (int)(PanelH / spacing);
        for (int ix = 0; ix <= nx; ix++)
            for (int iy = 0; iy <= ny; iy++)
                emit(Jitter(ix * spacing, spacing, rng), Jitter(iy * spacing, spacing, rng));
    }

    /// <summary>격자 규칙성으로 인한 PCA 편향을 피하기 위한 ±40% 지터.</summary>
    private static double Jitter(double v, double spacing, Random rng)
        => v + (rng.NextDouble() - 0.5) * 0.8 * spacing;
}
