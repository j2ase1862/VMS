using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;

namespace VMS.WeldTeach.Services;

/// <summary>
/// 그라인딩 스캔 명세 Step S4 — 커버리지 스캔라인의 6-DoF 공구 포즈 생성·내보내기.
/// 프레임 규약은 용접과 동일(Z=진행 방향, X=공구 축, Y=Z×X, 오일러 ZYX deg)하고,
/// 호길이·곡률 적응 재샘플과 오일러 변환은 TorchPoseService 자산을 공유한다.
/// </summary>
public class CoveragePoseService
{
    /// <summary>스캔라인들에 포즈를 채우고 생성된 총 포즈 수(전 패스 합)를 돌려준다.</summary>
    public int ComputePoses(IEnumerable<CoverageScanline> scanlines, GrindingParams prm)
    {
        int total = 0;
        foreach (var s in scanlines) total += ComputeSegmentPoses(s, prm);
        return total;
    }

    private static int ComputeSegmentPoses(CoverageScanline seg, GrindingParams prm)
    {
        seg.PosePoints.Clear();
        seg.ToolAxes.Clear();
        seg.PassPoses.Clear();

        var src = seg.PathPoints;
        if (src.Count < 2) return 0;

        // ---- 1) 호길이 재샘플 (용접과 동일한 스테이션 생성기) ----
        double spacing = Math.Clamp(prm.PoseSpacingMm, 0.1, 100.0);
        var cum = new double[src.Count];
        for (int i = 1; i < src.Count; i++) cum[i] = cum[i - 1] + (src[i] - src[i - 1]).Length;
        double len = cum[^1];
        if (len < 1e-9) return 0;

        var stations = prm.AdaptivePoseSpacing
            ? TorchPoseService.BuildAdaptiveStations(src, cum, len, spacing)
            : TorchPoseService.BuildUniformStations(len, spacing);

        // ---- 2) 위치·법선 보간 ----
        bool hasNormals = seg.PointNormals.Count == src.Count;
        var pts = new List<Point3D>(stations.Count);
        var normals = new List<Vector3D>(stations.Count);
        int k = 0;
        foreach (var s in stations)
        {
            while (k < src.Count - 2 && cum[k + 1] < s) k++;
            double segLen = cum[k + 1] - cum[k];
            double t = segLen > 1e-12 ? (s - cum[k]) / segLen : 0;
            pts.Add(src[k] + t * (src[k + 1] - src[k]));

            var n = hasNormals
                ? seg.PointNormals[k] + t * (seg.PointNormals[k + 1] - seg.PointNormals[k])
                : new Vector3D(0, 0, 1);
            if (n.Length > 1e-9) n.Normalize(); else n = new Vector3D(0, 0, 1);
            normals.Add(n);
        }

        // ---- 3) 지점별 프레임 (리드/틸트 적용) ----
        double lead = prm.LeadAngleDeg * Math.PI / 180.0;
        double tilt = prm.TiltAngleDeg * Math.PI / 180.0;
        var frames = new List<(Point3D P, Vector3D N, Vector3D X, Vector3D Y, Vector3D Z)>(pts.Count);

        for (int i = 0; i < pts.Count; i++)
        {
            int a = Math.Max(0, i - 1), c = Math.Min(pts.Count - 1, i + 1);
            var tan = pts[c] - pts[a];
            if (tan.Length > 1e-12) tan.Normalize();
            else tan = new Vector3D(1, 0, 0);

            // 표면 법선을 접선에 직교화 — 곡면을 타는 스캔라인은 N·T ≠ 0 이다
            var nrm = normals[i];
            var nPerp = nrm - Vector3D.DotProduct(nrm, tan) * tan;
            if (nPerp.Length < 1e-9)
            {
                // 퇴화(법선 ∥ 접선) — 접선에 직교인 임의 축으로 폴백
                var seed = Math.Abs(tan.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                nPerp = seed - Vector3D.DotProduct(seed, tan) * tan;
            }
            nPerp.Normalize();

            // 측면 벡터 S = N × T (리드 회전축) → V = Rot(T,tilt)·Rot(S,lead)·N
            var side = Vector3D.CrossProduct(nPerp, tan);
            if (side.Length > 1e-9) side.Normalize();
            var toolAxis = Rotate(Rotate(nPerp, side, lead), tan, tilt);
            toolAxis.Normalize();

            // 프레임 — X = 공구 축(리드/틸트 포함), Y = T × X, Z = X × Y.
            // 명세 초안의 "접선에 직교화"는 리드 성분이 T 방향이라 투영으로 지워져
            // 리드각이 0 이 되어버린다. 대신 공구 축을 X 로 두고 진행 방향을 그 축에
            // 직교하도록 재구성한다 — 리드가 0 이면 Z = T 로 같아지고, 리드가 있으면
            // 프레임 전체가 측면 축(S) 둘레로 리드각만큼 회전한 자세가 된다.
            var y = Vector3D.CrossProduct(tan, toolAxis);
            if (y.Length < 1e-9)
            {
                var seed = Math.Abs(toolAxis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                y = Vector3D.CrossProduct(seed, toolAxis);
            }
            y.Normalize();
            var z = Vector3D.CrossProduct(toolAxis, y);
            z.Normalize();

            frames.Add((pts[i], nPerp, toolAxis, y, z));
            seg.PosePoints.Add(pts[i]);
            seg.ToolAxes.Add(toolAxis);
        }

        // ---- 4) 다층 패스 — 패스 k 는 표면에서 k·depthPerPassMm 만큼 법선 반대로 ----
        int passCount = Math.Clamp(prm.PassCount, 1, 100);
        double depthPerPass = Math.Max(0, prm.DepthPerPassMm);
        int made = 0;
        for (int pass = 0; pass < passCount; pass++)
        {
            double depth = pass * depthPerPass;
            var list = new List<TorchPose>(frames.Count);
            foreach (var f in frames)
            {
                var p = f.P - depth * f.N;
                var (roll, pitch, yaw) = TorchPoseService.ToZyxEuler(f.X, f.Y, f.Z);
                list.Add(new TorchPose(p.X, p.Y, p.Z, roll, pitch, yaw));
            }
            seg.PassPoses.Add(list);
            made += list.Count;
        }
        return made;
    }

    /// <summary>로드리게스 회전 — 단위축 a 둘레로 v 를 angle(rad) 회전.</summary>
    private static Vector3D Rotate(Vector3D v, Vector3D a, double angle)
    {
        if (Math.Abs(angle) < 1e-12 || a.Length < 1e-9) return v;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return v * c
               + Vector3D.CrossProduct(a, v) * s
               + a * (Vector3D.DotProduct(a, v) * (1 - c));
    }

    /// <summary>
    /// 가공 영역들을 가공 순서(목록 순서)대로 JSON 으로 내보낸다.
    /// 경로는 스캔(카메라) 좌표계에서 생성되므로 CAD↔스캔 정합은 불필요하고,
    /// 로봇 베이스 변환은 핸드-아이 행렬 T_cam2base 를 외부 입력으로 받아 적용한다.
    /// </summary>
    public void ExportJson(string path, List<GrindingRegion> regions, Matrix3D? camToBase = null)
    {
        var payload = new
        {
            eulerConvention = "ZYX(deg)",
            frame = camToBase.HasValue
                ? "robot base coordinates (T_cam2base 적용됨)"
                : "camera coordinates (T_cam2base 미적용)",
            tCam2BaseRowMajor = camToBase.HasValue ? MatrixRows(camToBase.Value) : null,
            pathCount = regions.Count,
            paths = regions.Select((r, i) =>
            {
                var prm = r.Params ?? new GrindingParams();
                return new
                {
                    order = i + 1,
                    processType = "grinding",
                    regionId = r.RegionId,
                    parameters = new
                    {
                        toolDiameterMm = Round(prm.ToolDiameterMm),
                        overlapPct = Round(prm.OverlapPct),
                        stepoverMm = Round(prm.StepoverMm),
                        marginMm = Round(prm.MarginMm),
                        pattern = prm.Zigzag ? "Zigzag" : "OneWay",
                        rasterAngleDeg = Round(prm.RasterAngleDeg),
                        leadAngleDeg = Round(prm.LeadAngleDeg),
                        tiltAngleDeg = Round(prm.TiltAngleDeg),
                        passCount = Math.Max(1, prm.PassCount),
                        depthPerPassMm = Round(prm.DepthPerPassMm),
                        poseSpacingMm = Round(prm.PoseSpacingMm),
                        adaptiveSpacing = prm.AdaptivePoseSpacing,
                        feedRateMmS = Round(prm.FeedRateMmS),
                        targetForceN = Round(prm.TargetForceN),
                    },
                    totalLengthMm = Math.Round(r.TotalLengthMm, 3),
                    coveredAreaMm2 = Math.Round(r.CoveredAreaMm2, 1),
                    scanlineCount = r.Scanlines.Count,
                    scanlines = r.Scanlines.Select(s => new
                    {
                        lineIndex = s.LineIndex,
                        segmentIndex = s.SegmentIndex,
                        reversed = s.Reversed,
                        lengthMm = Math.Round(s.LengthMm, 3),
                        // 패스별로 나눠 담는다 — 로봇단이 절입 단계를 구분할 수 있어야 한다
                        passes = s.PassPoses.Select((poses, pass) => new
                        {
                            passIndex = pass,
                            depthMm = Round(pass * Math.Max(0, prm.DepthPerPassMm)),
                            pointCount = poses.Count,
                            poses = poses.Select(p =>
                            {
                                var q = camToBase.HasValue
                                    ? TorchPoseService.TransformPose(p, camToBase.Value)
                                    : p;
                                return new
                                {
                                    x = Math.Round(q.X, 4), y = Math.Round(q.Y, 4), z = Math.Round(q.Z, 4),
                                    roll = Math.Round(q.RollDeg, 3),
                                    pitch = Math.Round(q.PitchDeg, 3),
                                    yaw = Math.Round(q.YawDeg, 3),
                                };
                            }),
                        }),
                    }),
                };
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static double Round(double v) => Math.Round(v, 3);

    private static double[][] MatrixRows(Matrix3D m) => new[]
    {
        // 열 규약(p' = T·p) 기준 행 — WPF Matrix3D(행벡터 규약)의 전치
        new[] { m.M11, m.M21, m.M31, m.OffsetX },
        new[] { m.M12, m.M22, m.M32, m.OffsetY },
        new[] { m.M13, m.M23, m.M33, m.OffsetZ },
        new[] { 0.0, 0.0, 0.0, 1.0 },
    };
}
