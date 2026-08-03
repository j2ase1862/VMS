namespace VMS.WeldTeach.Models;

/// <summary>
/// 그라인딩 가공 영역 — 전처리 점군의 부분집합 (스캔 명세 Step S2).
/// 목록 순서 = 가공 순서. 커버리지 스캔라인(Step S3)은 이 영역 단위로 생성된다.
/// </summary>
public class GrindingRegion
{
    public string RegionId { get; set; } = string.Empty;

    /// <summary>전처리 점군(PreprocessedCloud.Points) 내 선택 인덱스.</summary>
    public List<int> PointIndices { get; set; } = new();

    /// <summary>이 영역의 커버리지 생성에 쓰인 파라미터 (생성 시 전역 기본값 복사).</summary>
    public GrindingParams? Params { get; set; }

    /// <summary>생성된 커버리지 스캔라인 (구멍/경계 분할 세그먼트 포함).</summary>
    public List<CoverageScanline> Scanlines { get; set; } = new();

    public double TotalLengthMm { get; set; }

    /// <summary>유효 마스크 셀 면적 합 (검증용 추정 커버 면적, mm²).</summary>
    public double CoveredAreaMm2 { get; set; }

    /// <summary>영역 목록 표시용 요약 — 경로 생성 후 라인 수·길이가 추가된다.</summary>
    public string Summary
    {
        get
        {
            if (Scanlines.Count == 0) return $"점 {PointIndices.Count:N0}";
            int lineCount = Scanlines.Select(s => s.LineIndex).Distinct().Count();
            return $"점 {PointIndices.Count:N0} · 라인 {lineCount} · {TotalLengthMm:F0} mm" +
                   $"{(Params != null ? $" @{Params.StepoverMm:0.#}mm" : "")}";
        }
    }
}
