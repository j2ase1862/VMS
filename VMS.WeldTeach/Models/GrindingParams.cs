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

    /// <summary>
    /// 표면 모델 옵션 — true 면 높이·법선을 그리드 쌍선형 대신 <b>서브패치 다항식
    /// 피팅</b>(SurfacePatchFitService)에서 얻는다. 해석적 매끄러운 법선(노이즈 저감).
    /// 유효 마스크·2.5D 위반 판정은 여전히 높이맵 점유 기반(구멍 외삽 방지).
    /// </summary>
    public bool UseSurfaceFit { get; set; }

    /// <summary>곡면 피팅 허용 RMSE(mm) — 패치 세분화 종료 임계.</summary>
    public double FitRmseMm { get; set; } = 0.1;

    // ──────────── Step S4: 공구 포즈 ────────────

    /// <summary>리드각(°) — 표면 법선을 진행 방향으로 기울이는 각도.</summary>
    public double LeadAngleDeg { get; set; } = 10;

    /// <summary>틸트각(°) — 표면 법선을 측면(진행 축 둘레)으로 기울이는 각도.</summary>
    public double TiltAngleDeg { get; set; }

    /// <summary>다층 가공 패스 수 — 2 이상이면 패스마다 법선 반대로 파고든다.</summary>
    public int PassCount { get; set; } = 1;

    /// <summary>패스당 절입 깊이(mm) — 0 이면 모든 패스가 표면을 추종한다.</summary>
    public double DepthPerPassMm { get; set; }

    /// <summary>포즈 재샘플 간격(mm) — 스캔라인 호 길이 기준.</summary>
    public double PoseSpacingMm { get; set; } = 2.0;

    /// <summary>곡률 적응 재샘플 — 곡선 구간에서 간격을 자동으로 좁힌다.</summary>
    public bool AdaptivePoseSpacing { get; set; } = true;

    // ──────────── 실행 메타 (로봇단 전달용 — 경로 생성에는 쓰이지 않음) ────────────

    /// <summary>이송 속도(mm/s).</summary>
    public double FeedRateMmS { get; set; } = 20;

    /// <summary>목표 접촉력(N) — 힘 제어는 로봇단 책임.</summary>
    public double TargetForceN { get; set; } = 15;

    public GrindingParams Clone() => (GrindingParams)MemberwiseClone();
}
