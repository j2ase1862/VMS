using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;
using VMS.WeldTeach.Services;
using Xunit;

namespace VMS.WeldTeach.Tests;

/// <summary>
/// 지시서 항목 ④(오목면 오프셋 접힘) · ⑤(절입 기준축) · ②(JSON 계약) 대응.
/// </summary>
public class PoseAndExportTests
{
    private static GrindingParams Base() => new()
    {
        ToolDiameterMm = 20,
        OverlapPct = 30,
        GridCellMm = 1.5,
        PoseSpacingMm = 2.0,
        AdaptivePoseSpacing = false,
        LeadAngleDeg = 0,
        TiltAngleDeg = 0,
    };

    private static (List<CoverageScanline> Lines, CoverageResult Result) Build(
        IReadOnlyList<Point3D> raw, GrindingParams prm)
    {
        var (pts, normals) = SyntheticSurfaces.Preprocess(raw);
        var result = new CoveragePathService()
            .Generate(pts, normals, SyntheticSurfaces.AllIndices(pts), prm);
        new CoveragePoseService().ComputePoses(result.Scanlines, prm);
        return (result.Scanlines, result);
    }

    // ──────────────────── 항목 ④ — 오목면 다층 패스 자기교차 ────────────────────

    [Fact(Skip = "지시서 항목 ④(오목면 오프셋 접힘 검사) 구현 전 사양 테스트 — 구현 PR에서 Skip 해제. docs/design/weldteach-grinding-review-2026-08.md")]
    public void Concave_multipass_depth_is_limited_by_curvature_radius()
    {
        // R=20 오목 홈에 5mm × 6패스(누적 25mm > 20mm) 요청 → 오프셋 곡면이 접힌다.
        var prm = Base();
        prm.PassCount = 6;
        prm.DepthPerPassMm = 5.0;

        var (lines, _) = Build(SyntheticSurfaces.ConcaveCylinder(radius: 20), prm);

        Assert.NotEmpty(lines);
        int maxPasses = lines.Max(l => l.PassPoses.Count);
        Assert.True(maxPasses <= 4,
            $"오목 반경(20mm)을 넘는 누적 절입이 허용되었습니다 (패스 {maxPasses}). " +
            "CLAUDE.md §3.3 위반 — 오프셋 곡면이 접힙니다.");
    }

    [Fact]
    public void Convex_surface_is_not_limited()
    {
        // 볼록면에서는 오프셋이 접히지 않으므로 요청 패스 수가 유지되어야 한다.
        var prm = Base();
        prm.PassCount = 6;
        prm.DepthPerPassMm = 5.0;

        var (lines, _) = Build(SyntheticSurfaces.ConvexCylinder(radius: 20), prm);

        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.Equal(6, l.PassPoses.Count));
    }

    [Fact]
    public void Multipass_poses_do_not_self_intersect()
    {
        // 마지막 패스의 인접 포즈가 서로 교차(순서 역전)하면 오프셋이 접힌 것이다.
        var prm = Base();
        prm.PassCount = 3;
        prm.DepthPerPassMm = 2.0;

        var (lines, _) = Build(SyntheticSurfaces.ConcaveCylinder(radius: 20), prm);

        foreach (var line in lines)
        {
            var last = line.PassPoses[^1];
            for (int i = 1; i < last.Count; i++)
            {
                var a = new Point3D(last[i - 1].X, last[i - 1].Y, last[i - 1].Z);
                var b = new Point3D(last[i].X, last[i].Y, last[i].Z);
                Assert.True((b - a).Length > 1e-6,
                    "다층 패스에서 포즈가 겹쳤습니다 — 오프셋 접힘.");
            }
        }
    }

    // ──────────────────────── 항목 ② — JSON 계약 ────────────────────────

    [Fact(Skip = "지시서 항목 ②(JSON 프레임 계약) 구현 전 사양 테스트 — 구현 PR에서 Skip 해제. docs/design/weldteach-grinding-review-2026-08.md")]
    public void Exported_json_declares_frame_conventions()
    {
        var prm = Base();
        var (lines, result) = Build(
            SampleCloudGenerator.Generate(SampleSurfaceKind.Plane).Points, prm);

        var region = new GrindingRegion
        {
            RegionId = "R1",
            Params = prm,
            Scanlines = lines,
            TotalLengthMm = result.TotalLengthMm,
            CoveredAreaMm2 = result.CoveredAreaMm2,
        };

        string path = Path.Combine(Path.GetTempPath(), $"weldteach_export_{Guid.NewGuid():N}.json");
        try
        {
            new CoveragePoseService().ExportJson(path, new List<GrindingRegion> { region });
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            // CLAUDE.md §4 — 파일만으로 해석 가능해야 한다
            foreach (var field in new[] { "schemaVersion", "units", "eulerConvention",
                                          "toolAxis", "travelAxis", "matrixConvention", "frame" })
                Assert.True(root.TryGetProperty(field, out _),
                    $"필수 필드 '{field}' 가 없습니다 — 제3자가 파일만으로 해석할 수 없습니다.");

            Assert.Equal("mm", root.GetProperty("units").GetString());
            Assert.Equal("camera", root.GetProperty("frame").GetString());   // T_cam2base 미입력
            Assert.Equal("X", root.GetProperty("toolAxis").GetString());
            Assert.Equal("Z", root.GetProperty("travelAxis").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact(Skip = "지시서 항목 ②(JSON 프레임 계약) 구현 전 사양 테스트 — 구현 PR에서 Skip 해제. docs/design/weldteach-grinding-review-2026-08.md")]
    public void Exported_json_marks_robot_base_when_handeye_applied()
    {
        var prm = Base();
        var (lines, result) = Build(
            SampleCloudGenerator.Generate(SampleSurfaceKind.Plane).Points, prm);

        var region = new GrindingRegion
        {
            RegionId = "R1",
            Params = prm,
            Scanlines = lines,
            TotalLengthMm = result.TotalLengthMm,
            CoveredAreaMm2 = result.CoveredAreaMm2,
        };

        var identity = Matrix3D.Identity;
        string path = Path.Combine(Path.GetTempPath(), $"weldteach_export_{Guid.NewGuid():N}.json");
        try
        {
            new CoveragePoseService().ExportJson(path, new List<GrindingRegion> { region }, identity);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("robot_base", doc.RootElement.GetProperty("frame").GetString());
            Assert.True(doc.RootElement.TryGetProperty("tCam2BaseRowMajor", out _));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
