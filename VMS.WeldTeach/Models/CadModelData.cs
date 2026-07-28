using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Models;

/// <summary>STEP 로드 결과 — 렌더링 메쉬 + 피킹/체이닝용 엣지 데이터.</summary>
public class CadModelData
{
    public string SourcePath { get; init; } = string.Empty;
    public int SolidCount { get; init; }
    public int FaceCount { get; init; }
    public List<FaceMeshData> FaceMeshes { get; init; } = new();
    public List<CadEdgeInfo> Edges { get; init; } = new();
}

/// <summary>면 하나의 삼각 메쉬 (월드 좌표).</summary>
public class FaceMeshData
{
    public int FaceId { get; init; }
    public List<Point3D> Positions { get; init; } = new();
    public List<int> TriangleIndices { get; init; } = new();
    /// <summary>면 중심에서의 외향 법선 (토치 이등분 계산용).</summary>
    public Vector3D CenterNormal { get; init; }
}

/// <summary>B-Rep 엣지 하나 — 폴리라인 + 위상(인접 면) 정보.</summary>
public class CadEdgeInfo
{
    public int EdgeId { get; init; }
    public List<Point3D> Points { get; init; } = new();
    /// <summary>
    /// Points 와 1:1 — 각 지점에서 인접 면 법선들의 이등분(합성·정규화) 벡터.
    /// 곡면(원통 등) 심에서도 지점마다 올바른 토치 방향을 주기 위해 pcurve UV 로 평가한다.
    /// </summary>
    public List<Vector3D> PointBisectors { get; init; } = new();
    /// <summary>이 엣지를 공유하는 면 Id 목록 (보통 2개).</summary>
    public List<int> AdjacentFaceIds { get; init; } = new();
    public Point3D StartPoint { get; init; }
    public Point3D EndPoint { get; init; }
    /// <summary>시작점에서 곡선 진행 방향 단위 접선.</summary>
    public Vector3D StartTangent { get; init; }
    /// <summary>끝점에서 곡선 진행 방향 단위 접선.</summary>
    public Vector3D EndTangent { get; init; }
    public double Length { get; init; }
    public bool IsClosed { get; init; }
}
