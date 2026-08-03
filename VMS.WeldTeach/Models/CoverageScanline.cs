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

    /// <summary>재샘플된 포즈 위치 (표면 패스 = 0번 패스 기준) — 렌더링·검증용.</summary>
    public List<Point3D> PosePoints { get; set; } = new();

    /// <summary>PosePoints 와 1:1 — 리드/틸트가 적용된 공구 축 방향 (렌더링용).</summary>
    public List<Vector3D> ToolAxes { get; set; } = new();

    /// <summary>
    /// 패스별 6-DoF 포즈 — PassPoses[k] = k번째 패스(표면에서 k·depthPerPassMm 만큼
    /// 법선 반대로 파고든 깊이)의 포즈 목록. 단일 패스면 원소 1개.
    /// </summary>
    public List<List<TorchPose>> PassPoses { get; set; } = new();

    /// <summary>전 패스 포즈 수 합계.</summary>
    public int TotalPoseCount => PassPoses.Sum(p => p.Count);
}
