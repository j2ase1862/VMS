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
    /// 경로 위 각 점의 토치 방향과 6-DoF 포즈를 채운다.
    /// 지점별 이등분 벡터(pcurve UV 법선 평가)를 사용해 곡면 심에서도 방향이 따라 돈다.
    /// 지점 값이 퇴화하면 윤곽 전체 평균(글로벌) 이등분으로 대체.
    /// </summary>
    public List<TorchPose> ComputePoses(WeldingPathContour contour, CadModelData model)
    {
        var globalBisector = ComputeBisector(contour, model);

        contour.TorchDirections.Clear();
        var poses = new List<TorchPose>(contour.PathPoints.Count);
        for (int i = 0; i < contour.PathPoints.Count; i++)
        {
            var z = contour.TangentVectors[i];
            var bisector = i < contour.PointBisectors.Count && contour.PointBisectors[i].Length > 1e-9
                ? contour.PointBisectors[i]
                : globalBisector;

            // 이등분 벡터를 접선에 직교화 (그람-슈미트)
            var x = bisector - Vector3D.DotProduct(bisector, z) * z;
            if (x.Length < 1e-9)
            {
                // 퇴화: 접선과 평행 — 임의 직교 벡터 선택
                x = Math.Abs(z.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                x -= Vector3D.DotProduct(x, z) * z;
            }
            x.Normalize();
            var y = Vector3D.CrossProduct(z, x);

            contour.TorchDirections.Add(x);
            var p = contour.PathPoints[i];
            var (roll, pitch, yaw) = ToZyxEuler(x, y, z);
            poses.Add(new TorchPose(p.X, p.Y, p.Z, roll, pitch, yaw));
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

    public void ExportJson(string path, WeldingPathContour contour, List<TorchPose> poses)
    {
        var payload = new
        {
            pathId = contour.PathId,
            totalLength = Math.Round(contour.TotalLength, 3),
            pointCount = poses.Count,
            eulerConvention = "ZYX(deg)",
            frame = "CAD model coordinates (ICP 정합 전 — T_align 적용 필요)",
            poses = poses.Select(p => new
            {
                x = Math.Round(p.X, 4), y = Math.Round(p.Y, 4), z = Math.Round(p.Z, 4),
                roll = Math.Round(p.RollDeg, 3), pitch = Math.Round(p.PitchDeg, 3), yaw = Math.Round(p.YawDeg, 3),
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
