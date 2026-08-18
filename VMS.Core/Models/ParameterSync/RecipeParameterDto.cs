using System;
using System.Collections.Generic;
using VMS.Core.Security;

namespace VMS.Core.Models.ParameterSync
{
    /// <summary>
    /// Web에서 관리되는 레시피 파라미터 (ParamCode → ParamValue)
    /// </summary>
    public class RecipeParameterDto
    {
        public int Id { get; set; }
        public int RecipeId { get; set; }
        public int ParamCode { get; set; }
        public double ParamValue { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 외부 API 응답 sanitization — Phase 3c.
        /// ParamValue 는 자유 도메인(온도/임계/좌표 등) 이라 ±1e9 범위 — NaN/Inf 만 차단.
        /// </summary>
        public RecipeParameterDto Sanitize()
        {
            const string ctx = nameof(RecipeParameterDto);
            Id = DtoValidator.ClampInt(Id, 0, 999_999_999, nameof(Id), ctx);
            RecipeId = DtoValidator.ClampInt(RecipeId, 0, 999_999_999, nameof(RecipeId), ctx);
            ParamCode = DtoValidator.ClampInt(ParamCode, 0, 999_999, nameof(ParamCode), ctx);
            ParamValue = DtoValidator.ClampDouble(ParamValue, -1e9, 1e9, nameof(ParamValue), ctx);
            Description = DtoValidator.Truncate(Description, 500, nameof(Description), ctx) ?? string.Empty;
            Category = DtoValidator.Truncate(Category, 100, nameof(Category), ctx) ?? string.Empty;
            Unit = DtoValidator.Truncate(Unit, 50, nameof(Unit), ctx) ?? string.Empty;
            return this;
        }
    }

    /// <summary>
    /// 레시피 요약 정보
    /// </summary>
    public class RecipeSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 외부 API 응답 sanitization — Phase 3c. 레시피 드롭다운 표시 안정성.
        /// </summary>
        public RecipeSummaryDto Sanitize()
        {
            const string ctx = nameof(RecipeSummaryDto);
            Id = DtoValidator.ClampInt(Id, 0, 999_999_999, nameof(Id), ctx);
            Name = DtoValidator.Truncate(Name, 200, nameof(Name), ctx) ?? string.Empty;
            Description = DtoValidator.Truncate(Description, 2000, nameof(Description), ctx) ?? string.Empty;
            return this;
        }
    }

    /// <summary>
    /// 검사 결과 단일 항목
    /// </summary>
    public class ParameterResultDto
    {
        public int ParamCode { get; set; }
        public double MeasuredValue { get; set; }
        public string Judgment { get; set; } = "OK";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 검사 결과 업로드 요청.
    /// Phase 3 추적성: 검사 결과를 작업지시/Lot/작업자/시리얼 컨텍스트와 함께 업로드.
    /// 모든 추적 필드는 nullable — 컨텍스트 미선택 상태에서도 업로드 가능.
    /// </summary>
    public class ParameterResultUploadRequest
    {
        public int ClientIndex { get; set; }
        public int RecipeId { get; set; }
        public List<ParameterResultDto> Results { get; set; } = new();

        // ─── Phase 3: 추적성 필드 (Integration_Plan_VMS_Web.md 3.4) ───
        /// <summary>작업지시 ID (선택). 미선택 시 null.</summary>
        public int? WorkOrderId { get; set; }
        /// <summary>현재 활성 Lot ID (선택). 미선택 시 null.</summary>
        public int? LotId { get; set; }
        /// <summary>출근/로그인된 작업자 ID (선택). 키오스크/PIN 입력 시 채워짐.</summary>
        public int? OperatorId { get; set; }
        /// <summary>바코드/QR 스캔으로 얻은 제품 시리얼 (선택).</summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// 이미지 업로드와 동일하게 싣는 상관 키. Web 이 InspectionHistory 행과
        /// 이미지(/api/inspection-images)를 순서무관 매칭하는 데 사용. null 이면 미연동.
        /// </summary>
        public string? CorrelationKey { get; set; }

        /// <summary>
        /// 사이클 전체 판정 (AUTO RUN "1사이클 = 1개" 집계, Web v1.1.1+).
        /// 이 값이 있으면 Results 가 비어 있어도 판정 전용 업로드로 접수된다 —
        /// 파라미터 연동 툴이 없는 레시피도 WO 수량·검사 이력이 집계되도록.
        /// </summary>
        public bool? OverallPass { get; set; }

        // ─── Predictive_DefectRate_Plan §5.1 (V1/V2/V3): 예측 모델용 피처 ───
        // 모두 nullable — Web 측 후방호환 유지(미지원 VMS 빌드 시 자연스럽게 NULL).
        /// <summary>V2: 검사 1회 소요 시간(ms). 가동 페이스 둔화 = 품질 저하 선행 신호.</summary>
        public int? CycleTimeMs { get; set; }
        /// <summary>V1: 이미지 평균 밝기(0~255).</summary>
        public double? Brightness { get; set; }
        /// <summary>V1: 이미지 명암 표준편차(Contrast).</summary>
        public double? ContrastStd { get; set; }
        /// <summary>V1: Laplacian variance — 초점/선명도 점수.</summary>
        public double? FocusScore { get; set; }
        /// <summary>V1: Otsu 이진화 + connected components 로 산출한 blob 개수.</summary>
        public int? BlobCount { get; set; }
        /// <summary>V1: 가장 큰 blob 면적(px).</summary>
        public double? MaxBlobAreaPx { get; set; }
        /// <summary>V3: DL 분류/검출 모델의 신뢰도 점수(0~1). 임계 근접일수록 잠재 NG.</summary>
        public double? DlConfidence { get; set; }
        /// <summary>V3: 사용된 DL 모델 버전(피처 분포 변화 추적용).</summary>
        public string? DlModelVersion { get; set; }
    }

    /// <summary>
    /// Predictive_DefectRate_Plan §5.1 — 검사 1건의 예측 피처 묶음.
    /// InspectionService → ParameterSyncService 로 피처를 전달할 때 사용.
    /// </summary>
    public class InspectionFeatureMetrics
    {
        public int? CycleTimeMs { get; set; }
        public double? Brightness { get; set; }
        public double? ContrastStd { get; set; }
        public double? FocusScore { get; set; }
        public int? BlobCount { get; set; }
        public double? MaxBlobAreaPx { get; set; }
        public double? DlConfidence { get; set; }
        public string? DlModelVersion { get; set; }
    }
}
