using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Models;

/// <summary>
/// 커버리지 스캔라인 한 세그먼트 — 구멍/경계에서 분할되면 같은 LineIndex 에 여러 세그먼트가 생긴다.
/// WeldingPathContour 와 동형(폴리라인 + 지점별 방향 벡터)이되, 이등분 대신 표면 법선을 담는다.
/// 포즈(PosePoints/Poses)는 M5 에서 채워진다.
/// </summary>
public class CoverageScanline
{
    /// <summary>래스터 라인 순번 (스텝오버 방향).</summary>
    public int LineIndex { get; set; }

    /// <summary>같은 라인 내 세그먼트 순번 (구멍 분할).</summary>
    public int SegmentIndex { get; set; }

    /// <summary>지그재그 홀수 라인 — 진행 방향이 역전됨.</summary>
    public bool Reversed { get; set; }

    /// <summary>스캔라인 3D 폴리라인 (스캔 좌표계).</summary>
    public List<Point3D> PathPoints { get; set; } = new();

    /// <summary>PathPoints 와 1:1 — 지점별 표면 법선 (공구 축 원천).</summary>
    public List<Vector3D> PointNormals { get; set; } = new();

    public double LengthMm { get; set; }
}
