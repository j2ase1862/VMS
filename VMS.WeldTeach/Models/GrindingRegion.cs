namespace VMS.WeldTeach.Models;

/// <summary>
/// 그라인딩 가공 영역 — 전처리 점군의 부분집합 (스캔 명세 Step S2).
/// 목록 순서 = 가공 순서. 커버리지 스캔라인(M3)은 이 영역 단위로 생성된다.
/// </summary>
public class GrindingRegion
{
    public string RegionId { get; set; } = string.Empty;

    /// <summary>전처리 점군(PreprocessedCloud.Points) 내 선택 인덱스.</summary>
    public List<int> PointIndices { get; set; } = new();

    /// <summary>영역 목록 표시용 요약 — M3 에서 라인 수·길이·포즈가 추가된다.</summary>
    public string Summary => $"점 {PointIndices.Count:N0}";
}
