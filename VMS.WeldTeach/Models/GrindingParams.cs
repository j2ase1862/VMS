namespace VMS.WeldTeach.Models;

/// <summary>그라인딩 공정 파라미터 — 스캔 명세 Step S3 표. 전역 기본값 → 영역별 복사본.</summary>
public class GrindingParams
{
    /// <summary>그라인딩 디스크/패드 유효 폭 (mm).</summary>
    public double ToolDiameterMm { get; set; } = 50;

    /// <summary>인접 스캔라인 겹침 비율 (%).</summary>
    public double OverlapPct { get; set; } = 30;

    /// <summary>스텝오버 (파생) = 공구 직경 × (1 − 겹침률).</summary>
    public double StepoverMm => Math.Max(0.5, ToolDiameterMm * (1 - OverlapPct / 100.0));

    /// <summary>래스터 방향 회전각(°) — 0 = PCA 최장축(자동).</summary>
    public double RasterAngleDeg { get; set; }

    /// <summary>왕복(지그재그) 패턴 — 홀수 라인 역방향. false = 편도.</summary>
    public bool Zigzag { get; set; } = true;

    /// <summary>영역 경계에서 안쪽으로 띄우는 거리 (mm) — 마스크 침식으로 적용.</summary>
    public double MarginMm { get; set; }

    /// <summary>
    /// 높이맵 그리드 셀 크기 (mm) — 전처리 복셀 크기의 2배 수준 권장 (셀당 점 ≥4개
    /// 확보로 빈 셀 최소화). 복셀과 같게 잡으면 빈 셀이 ~30% 생겨 라인이 조각난다.
    /// </summary>
    public double GridCellMm { get; set; } = 2.0;

    public GrindingParams Clone() => (GrindingParams)MemberwiseClone();
}
