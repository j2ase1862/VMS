using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Models;

/// <summary>체이닝된 용접 경로 윤곽 — 명세서 Step 2 의 데이터 구조.</summary>
public class WeldingPathContour
{
    public string PathId { get; set; } = string.Empty;
    public List<Point3D> PathPoints { get; set; } = new();
    /// <summary>PathPoints 와 1:1 — 지점별 인접 면 법선 이등분 벡터 (토치 방향 원천).</summary>
    public List<Vector3D> PointBisectors { get; set; } = new();
    public List<Vector3D> TangentVectors { get; set; } = new();
    public List<Vector3D> TorchDirections { get; set; } = new();
    public double TotalLength { get; set; }
    /// <summary>이 윤곽을 구성한 엣지 Id 목록 (하이라이트용).</summary>
    public List<int> EdgeIds { get; set; } = new();
    /// <summary>이 윤곽의 6-DoF 토치 포즈 (생성 시 계산되어 보관).</summary>
    public List<TorchPose> Poses { get; set; } = new();

    /// <summary>경로 목록 표시용 요약 (예: "엣지 2 · 314.1 mm · 포즈 135").</summary>
    public string Summary => $"엣지 {EdgeIds.Count} · {TotalLength:F1} mm · 포즈 {Poses.Count}";
}
