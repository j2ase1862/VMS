using System;
using System.Collections.Generic;

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
    }

    /// <summary>
    /// 레시피 요약 정보
    /// </summary>
    public class RecipeSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
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
    }
}
