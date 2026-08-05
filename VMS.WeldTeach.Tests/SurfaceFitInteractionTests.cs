using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;
using VMS.WeldTeach.Services;
using Xunit;

namespace VMS.WeldTeach.Tests;

/// <summary>
/// 지시서 항목 ③ — 곡면 피팅 모드에서 후처리 평활이 정밀도를 되돌리는 문제.
/// 옵션 단독 검증이 아니라 <b>조합</b> 검증이다 (CLAUDE.md §6 옵션 조합 회귀).
/// </summary>
public class SurfaceFitInteractionTests
{
    private static GrindingParams Params(bool useFit) => new()
    {
        ToolDiameterMm = 50,
        OverlapPct = 30,
        GridCellMm = 2.0,
        UseSurfaceFit = useFit,
        FitRmseMm = 0.05,
    };

    private static (CoverageResult Result, SampleCloud Sample) Run(
        SampleSurfaceKind kind, bool useFit)
    {
        var sample = SampleCloudGenerator.Generate(kind);
        var (pts, normals) = SyntheticSurfaces.Preprocess(sample.Points, sample.Viewpoint);
        var result = new CoveragePathService()
            .Generate(pts, normals, SyntheticSurfaces.AllIndices(pts), Params(useFit));
        return (result, sample);
    }

    /// <summary>경로점의 참값 표면 편차 RMS(mm).</summary>
    private static double SurfaceDeviationRms(CoverageResult r, SampleCloud sample)
    {
        var devs = r.Scanlines.SelectMany(s => s.PathPoints)
            .Select(p => sample.TrueDeviationAt(p)).ToList();
        Assert.NotEmpty(devs);
        return Math.Sqrt(devs.Sum(d => d * d) / devs.Count);
    }

    /// <summary>인접 경로점 간 법선 각도차의 표준편차(deg) — 법선 요동 지표.</summary>
    private static double NormalJitterDeg(CoverageResult r)
    {
        var diffs = new List<double>();
        foreach (var s in r.Scanlines)
            for (int i = 1; i < s.PointNormals.Count; i++)
            {
                double dot = Math.Clamp(
                    Vector3D.DotProduct(s.PointNormals[i - 1], s.PointNormals[i]), -1, 1);
                diffs.Add(Math.Acos(dot) * 180.0 / Math.PI);
            }
        if (diffs.Count == 0) return 0;
        double mean = diffs.Average();
        return Math.Sqrt(diffs.Sum(d => (d - mean) * (d - mean)) / diffs.Count);
    }

    [Fact]
    public void Surface_fit_improves_deviation_on_cylinder()
    {
        var (fitOn, sample) = Run(SampleSurfaceKind.CylinderShell, useFit: true);
        var (fitOff, _) = Run(SampleSurfaceKind.CylinderShell, useFit: false);

        double devOn = SurfaceDeviationRms(fitOn, sample);
        double devOff = SurfaceDeviationRms(fitOff, sample);

        Assert.True(devOn < devOff,
            $"곡면 피팅이 표면 편차를 개선하지 못했습니다 (피팅 {devOn:F4} vs 높이맵 {devOff:F4}). " +
            "후처리 평활이 피팅 결과를 덮어쓰고 있는지 확인하십시오 — CLAUDE.md §3.2.");
    }

    [Fact]
    public void Surface_fit_reduces_normal_jitter_on_cylinder()
    {
        var (fitOn, _) = Run(SampleSurfaceKind.CylinderShell, useFit: true);
        var (fitOff, _) = Run(SampleSurfaceKind.CylinderShell, useFit: false);

        double jOn = NormalJitterDeg(fitOn);
        double jOff = NormalJitterDeg(fitOff);

        Assert.True(jOn < jOff,
            $"곡면 피팅이 법선 요동을 줄이지 못했습니다 (피팅 {jOn:F3}° vs 높이맵 {jOff:F3}°).");
    }

    [Fact]
    public void Plane_is_identical_in_both_modes()
    {
        // 평면은 평활해도 불변이므로 두 모드가 사실상 같은 경로를 내야 한다.
        var (fitOn, _) = Run(SampleSurfaceKind.Plane, useFit: true);
        var (fitOff, _) = Run(SampleSurfaceKind.Plane, useFit: false);

        Assert.Equal(fitOff.LineCount, fitOn.LineCount);
        Assert.InRange(fitOn.TotalLengthMm,
            fitOff.TotalLengthMm * 0.99, fitOff.TotalLengthMm * 1.01);
    }
}
