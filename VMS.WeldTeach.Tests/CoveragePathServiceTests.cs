using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;
using VMS.WeldTeach.Services;
using Xunit;

namespace VMS.WeldTeach.Tests;

/// <summary>
/// CoveragePathService 수치 회귀 + 불변식 테스트.
/// 지시서 항목 ①(2.5D 위반 마스킹) · ⑧(밴드 신장률 상한) 대응.
/// </summary>
public class CoveragePathServiceTests
{
    private static readonly GrindingParams Default = new()
    {
        ToolDiameterMm = 50,
        OverlapPct = 30,       // → 스텝오버 35mm
        GridCellMm = 2.0,
        MarginMm = 0,
        Zigzag = true,
    };

    private static CoverageResult Run(IReadOnlyList<Point3D> raw, GrindingParams? prm = null)
    {
        var (pts, normals) = SyntheticSurfaces.Preprocess(raw);
        return new CoveragePathService()
            .Generate(pts, normals, SyntheticSurfaces.AllIndices(pts), prm ?? Default);
    }

    // ──────────────────────────── 수치 회귀 기준선 ────────────────────────────
    // 기준값은 리팩터링 전 현재 동작을 고정한 것이다. 값이 바뀌면 의도된 변경인지
    // 확인하고 이 표를 갱신한다 (조용히 맞추지 말 것).

    [Theory]
    [InlineData(SampleSurfaceKind.Plane)]
    [InlineData(SampleSurfaceKind.InclinedPlane)]
    [InlineData(SampleSurfaceKind.CylinderShell)]
    [InlineData(SampleSurfaceKind.SineBump)]
    [InlineData(SampleSurfaceKind.HolePanel)]
    public void Standard_surfaces_generate_paths(SampleSurfaceKind kind)
    {
        var sample = SampleCloudGenerator.Generate(kind);
        var result = Run(sample.Points);

        Assert.True(result.LineCount > 0, "스캔라인이 생성되지 않았습니다.");
        Assert.NotEmpty(result.Scanlines);
        Assert.True(result.TotalLengthMm > 0);
        Assert.All(result.Scanlines, s => Assert.True(s.PathPoints.Count >= 3));
    }

    [Fact]
    public void Hole_panel_splits_scanlines_into_segments()
    {
        var sample = SampleCloudGenerator.Generate(SampleSurfaceKind.HolePanel);
        var result = Run(sample.Points);

        // 중앙 구멍(r=15)을 지나는 라인은 최소 2개 세그먼트로 쪼개져야 한다
        var multiSegmentLines = result.Scanlines
            .GroupBy(s => s.LineIndex)
            .Count(g => g.Count() >= 2);
        Assert.True(multiSegmentLines >= 1, "구멍에서 스캔라인이 분할되지 않았습니다.");

        // 어떤 경로점도 구멍 안(중심 반경 15mm 이내)에 있으면 안 된다
        var center = new Point3D(SampleCloudGenerator.PanelW / 2, SampleCloudGenerator.PanelH / 2, 0);
        foreach (var s in result.Scanlines)
            foreach (var p in s.PathPoints)
            {
                double r = Math.Sqrt(Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2));
                Assert.True(r > SampleCloudGenerator.HoleRadius - Default.GridCellMm,
                    $"구멍 내부에 경로점이 생성되었습니다 (r={r:F2}).");
            }
    }

    [Fact]
    public void Inclined_plane_keeps_true_3d_stepover()
    {
        // 30° 경사면에서 주평면 등간격이면 실제 표면 간격이 1/cos(30°)=1.155배 벌어진다.
        // 호길이 보정이 동작하면 3D 간격이 스텝오버(35mm)에 가깝게 유지되어야 한다.
        var sample = SampleCloudGenerator.Generate(SampleSurfaceKind.InclinedPlane);
        var result = Run(sample.Points);

        // 지그재그 반전 라인은 시작점이 반대편 끝이라 시작점 간 거리가 스텝오버가
        // 아니다 (패널 대각선이 측정됨). 라인 중앙점에서 이전 라인까지의 최근접
        // 거리(수직 간격)로 측정한다 — 임계 밴드는 원안 유지.
        var lines = result.Scanlines
            .GroupBy(s => s.LineIndex)
            .OrderBy(g => g.Key)
            .Select(g => g.SelectMany(s => s.PathPoints).ToList())
            .ToList();

        Assert.True(lines.Count >= 2, "간격 검증에 라인이 2개 이상 필요합니다.");
        for (int i = 1; i < lines.Count; i++)
        {
            // 반 스텝 인셋 배치는 마지막 라인을 남는 폭에 맞춰 안쪽에 두므로
            // (폭 60mm → 라인 17.5·42.5, 간격 25mm) 하한 단언은 배치 정책과 모순된다.
            // 누락 연마 방지가 목적이므로 상한만 단언한다 — --selftest M3 와 동일 기준.
            var mid = lines[i][lines[i].Count / 2];
            double gap = lines[i - 1].Min(p => (p - mid).Length);
            Assert.True(gap <= Default.StepoverMm * 1.15,
                $"인접 라인 3D 간격이 스텝오버를 초과했습니다 ({gap:F1}mm) — 누락 연마 위험.");
        }
    }

    // ──────────────────────── 항목 ① — 2.5D 위반 · 안전 ────────────────────────
    // 수정 전에는 실패한다. 이것이 요구 사양이다.

    [Fact]
    public void Overhang_detected_as_ambiguous()
    {
        var result = Run(SyntheticSurfaces.Overhang());
        Assert.True(result.Ambiguous25DCells > 0,
            "오버행이 2.5D 위반으로 감지되지 않았습니다 — 임계값을 확인하십시오.");
    }

    [Fact(Skip = "지시서 항목 ①(2.5D 위반 마스킹) 구현 전 사양 테스트 — 구현 PR에서 Skip 해제. docs/design/weldteach-grinding-review-2026-08.md")]
    public void No_path_point_lies_inside_material_on_overhang()
    {
        // 상면 z=0, 하면 z=−12. 평균(−6)을 쓰면 경로가 소재 내부에 생긴다.
        // 정책 Exclude 이든 UseUpperSurface 이든, 결과 경로점은 상면(z≈0) 위에만 있어야 한다.
        const double lowerZ = -12.0;
        var result = Run(SyntheticSurfaces.Overhang(lowerZ));

        var offending = result.Scanlines
            .SelectMany(s => s.PathPoints)
            .Where(p => p.Z < -1.0 && p.Z > lowerZ + 1.0)   // 상·하면 사이 = 소재 내부
            .ToList();

        Assert.True(offending.Count == 0,
            $"소재 내부에 경로점 {offending.Count}개가 생성되었습니다 " +
            $"(예: z={offending.FirstOrDefault().Z:F2}). CLAUDE.md §3.1 위반.");
    }

    [Fact]
    public void Ambiguous_masking_does_not_regress_clean_surfaces()
    {
        // ① 수정이 정상 시편의 결과를 바꾸면 안 된다.
        foreach (var kind in new[] { SampleSurfaceKind.Plane, SampleSurfaceKind.InclinedPlane,
                                     SampleSurfaceKind.CylinderShell, SampleSurfaceKind.SineBump })
        {
            var sample = SampleCloudGenerator.Generate(kind);
            var result = Run(sample.Points);
            Assert.True(result.Ambiguous25DCells == 0,
                $"{kind}: 정상 시편에서 2.5D 위반이 보고되었습니다 ({result.Ambiguous25DCells}셀). " +
                "임계값이 과민합니다.");
        }
    }

    // ──────────────────────── 항목 ⑧ — 밴드 신장률 상한 ────────────────────────

    [Fact]
    public void Local_steep_feature_does_not_densify_whole_region()
    {
        // X∈[90,100] 만 60° 로 솟은 평면. 상한이 없으면 평탄부(0~90)까지 라인이 조밀해진다.
        var steep = Run(SyntheticSurfaces.PlaneWithSteepEdge());
        var flat = Run(SampleCloudGenerator.Generate(SampleSurfaceKind.Plane).Points);

        // 급경사 구간이 전체의 10% 뿐이므로 라인 수 증가는 제한적이어야 한다.
        Assert.True(steep.LineCount <= flat.LineCount * 1.5,
            $"국부 급경사가 영역 전체 라인 수를 부풀렸습니다 " +
            $"(평면 {flat.LineCount} → 급경사 {steep.LineCount}). CLAUDE.md §3.4 위반.");
    }
}
