using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Models;

/// <summary>체이닝된 용접 경로 윤곽 — 명세서 Step 2 의 데이터 구조.</summary>
public class WeldingPathContour
{
    public string PathId { get; set; } = string.Empty;
    public List<Point3D> PathPoints { get; set; } = new();
    public List<Vector3D> TangentVectors { get; set; } = new();
    public List<Vector3D> TorchDirections { get; set; } = new();
    public double TotalLength { get; set; }
    /// <summary>이 윤곽을 구성한 엣지 Id 목록 (하이라이트용).</summary>
    public List<int> EdgeIds { get; set; } = new();
}
