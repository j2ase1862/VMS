using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Models;

/// <summary>체이닝된 용접 경로 윤곽 — 명세서 Step 2 의 데이터 구조.</summary>
public class WeldingPathContour
{
    public string PathId { get; set; } = string.Empty;
    /// <summary>표시/피킹용 경로 폴리라인 (CAD 엣지 샘플 — 포즈와 별개).</summary>
    public List<Point3D> PathPoints { get; set; } = new();
    /// <summary>PathPoints 와 1:1 — 지점별 인접 면 법선 이등분 벡터 (토치 방향 원천).</summary>
    public List<Vector3D> PointBisectors { get; set; } = new();
    public List<Vector3D> TangentVectors { get; set; } = new();
    /// <summary>포즈 간격(mm)으로 균일 재샘플링된 실제 포즈 위치 — Poses 와 1:1.</summary>
    public List<Point3D> PosePoints { get; set; } = new();
    /// <summary>PosePoints 와 1:1 — 각 포즈의 토치 방향(X축). 재샘플 후 채워진다.</summary>
    public List<Vector3D> TorchDirections { get; set; } = new();
    public double TotalLength { get; set; }
    /// <summary>이 윤곽을 구성한 엣지 Id 목록 (하이라이트용).</summary>
    public List<int> EdgeIds { get; set; } = new();
    /// <summary>이 윤곽의 6-DoF 토치 포즈 (생성 시 계산되어 보관).</summary>
    public List<TorchPose> Poses { get; set; } = new();

    /// <summary>이 경로의 포즈 간격(mm) — 경로마다 다르게 설정 가능 (직선 넓게 / 곡선 좁게).</summary>
    public double SpacingMm { get; set; } = 1.5;

    /// <summary>곡률 적응 간격 — 켜면 SpacingMm 은 최대 간격이 되고, 곡선 구간은 현 오차 기준으로 촘촘해진다.</summary>
    public bool Adaptive { get; set; }

    /// <summary>경로 목록 표시용 요약 (예: "엣지 2 · 314.1 mm · 포즈 211 @1.5mm·적응").</summary>
    public string Summary =>
        $"엣지 {EdgeIds.Count} · {TotalLength:F1} mm · 포즈 {Poses.Count} @{SpacingMm:0.#}mm{(Adaptive ? "·적응" : "")}";
}
