using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;
using VMS.WeldTeach.Models;

namespace VMS.WeldTeach.Services;

/// <summary>
/// 명세서 Step 3 — 6-DoF 토치 포즈 계산.
/// Z축 = 진행 접선, X축 = 인접 두 면 법선의 이등분 벡터(접선에 직교화), Y = Z × X.
/// 오일러 각은 ZYX(yaw-pitch-roll) 규약, 도(deg) 단위.
/// </summary>
public class TorchPoseService
{
    /// <summary>
    /// 경로를 포즈 간격(mm)으로 호 길이 기준 균일 재샘플링한 뒤, 각 포즈 점의 토치 방향과
    /// 6-DoF 포즈를 채운다. 위치·이등분 벡터는 표시용 폴리라인 샘플에서 선형 보간하고,
    /// 접선은 재샘플 점의 이웃 차분으로 계산한다. 이등분이 퇴화하면 글로벌 평균으로 대체.
    /// </summary>
    public List<TorchPose> ComputePoses(WeldingPathContour contour, CadModelData model, double spacingMm = 1.5)
    {
        var globalBisector = ComputeBisector(contour, model);
        var src = contour.PathPoints;
        contour.PosePoints.Clear();
        contour.TorchDirections.Clear();
        var poses = new List<TorchPose>();
        if (src.Count < 2) return poses;

        spacingMm = Math.Clamp(spacingMm, 0.1, 100.0);

        // 누적 호 길이
        var cum = new double[src.Count];
        for (int i = 1; i < src.Count; i++)
            cum[i] = cum[i - 1] + (src[i] - src[i - 1]).Length;
        double total = cum[^1];
        if (total < 1e-9) return poses;

        // 재샘플 호 길이 위치 — 0, d, 2d, ... 끝점은 항상 포함
        // (마지막 조각이 간격의 30% 미만이면 직전 샘플을 끝점으로 당겨 붙인다)
        var stations = new List<double>();
        for (double s = 0; s < total; s += spacingMm) stations.Add(s);
        if (stations.Count > 1 && total - stations[^1] < spacingMm * 0.3) stations[^1] = total;
        else stations.Add(total);

        // 위치·이등분 보간
        bool hasBis = contour.PointBisectors.Count == src.Count;
        var pts = new List<Point3D>(stations.Count);
        var biss = new List<Vector3D>(stations.Count);
        int seg = 0;
        foreach (var s in stations)
        {
            while (seg < src.Count - 2 && cum[seg + 1] < s) seg++;
            double segLen = cum[seg + 1] - cum[seg];
            double t = segLen > 1e-12 ? (s - cum[seg]) / segLen : 0;
            pts.Add(src[seg] + t * (src[seg + 1] - src[seg]));

            var b = default(Vector3D);
            if (hasBis)
            {
                b = contour.PointBisectors[seg] + t * (contour.PointBisectors[seg + 1] - contour.PointBisectors[seg]);
                if (b.Length > 1e-9) b.Normalize();
            }
            biss.Add(b);
        }

        // 포즈 프레임 (Z=접선, X=이등분 직교화, Y=Z×X)
        for (int i = 0; i < pts.Count; i++)
        {
            int a = Math.Max(0, i - 1), c = Math.Min(pts.Count - 1, i + 1);
            var z = pts[c] - pts[a];
            if (z.Length > 1e-12) z.Normalize();

            var bisector = biss[i].Length > 1e-9 ? biss[i] : globalBisector;
            var x = bisector - Vector3D.DotProduct(bisector, z) * z;
            if (x.Length < 1e-9)
            {
                // 퇴화: 접선과 평행 — 임의 직교 벡터 선택
                x = Math.Abs(z.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                x -= Vector3D.DotProduct(x, z) * z;
            }
            x.Normalize();
            var y = Vector3D.CrossProduct(z, x);

            contour.PosePoints.Add(pts[i]);
            contour.TorchDirections.Add(x);
            var (roll, pitch, yaw) = ToZyxEuler(x, y, z);
            poses.Add(new TorchPose(pts[i].X, pts[i].Y, pts[i].Z, roll, pitch, yaw));
        }
        return poses;
    }

    /// <summary>윤곽 엣지들의 인접 면(최다 2면) 법선 합성 → 토치 이등분 방향.</summary>
    public Vector3D ComputeBisector(WeldingPathContour contour, CadModelData model)
    {
        var faceIds = contour.EdgeIds
            .SelectMany(id => model.Edges.First(e => e.EdgeId == id).AdjacentFaceIds)
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(2)
            .ToList();

        var sum = new Vector3D();
        foreach (var fid in faceIds)
        {
            var mesh = model.FaceMeshes.FirstOrDefault(m => m.FaceId == fid);
            if (mesh != null) sum += mesh.CenterNormal;
        }
        if (sum.Length < 1e-9)
            sum = new Vector3D(0, 0, 1);   // 퇴화(180° 평면 이음) — 기본 상향
        sum.Normalize();
        return sum;
    }

    /// <summary>회전 행렬 R=[X Y Z] (열벡터) → ZYX 오일러 (roll=X축, pitch=Y축, yaw=Z축 회전, deg).</summary>
    private static (double Roll, double Pitch, double Yaw) ToZyxEuler(Vector3D x, Vector3D y, Vector3D z)
    {
        // R = | x.X  y.X  z.X |
        //     | x.Y  y.Y  z.Y |
        //     | x.Z  y.Z  z.Z |
        double r11 = x.X, r21 = x.Y, r31 = x.Z;
        double r32 = y.Z, r33 = z.Z;
        double pitch = Math.Atan2(-r31, Math.Sqrt(r32 * r32 + r33 * r33));
        double yaw, roll;
        if (Math.Abs(Math.Cos(pitch)) < 1e-9)
        {
            // 짐벌락: yaw 를 0 으로 고정
            yaw = 0;
            roll = Math.Atan2(-y.X, y.Y);
        }
        else
        {
            yaw = Math.Atan2(r21, r11);
            roll = Math.Atan2(r32, r33);
        }
        const double toDeg = 180.0 / Math.PI;
        return (roll * toDeg, pitch * toDeg, yaw * toDeg);
    }

    /// <summary>경로 목록을 용접 순서(목록 순서)대로 하나의 JSON 으로 내보낸다.</summary>
    public void ExportJson(string path, List<WeldingPathContour> contours)
    {
        var payload = new
        {
            eulerConvention = "ZYX(deg)",
            frame = "CAD model coordinates (ICP 정합 전 — T_align 적용 필요)",
            pathCount = contours.Count,
            paths = contours.Select((c, i) => new
            {
                order = i + 1,
                pathId = c.PathId,
                totalLength = Math.Round(c.TotalLength, 3),
                pointCount = c.Poses.Count,
                poses = c.Poses.Select(p => new
                {
                    x = Math.Round(p.X, 4), y = Math.Round(p.Y, 4), z = Math.Round(p.Z, 4),
                    roll = Math.Round(p.RollDeg, 3), pitch = Math.Round(p.PitchDeg, 3), yaw = Math.Round(p.YawDeg, 3),
                }),
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
