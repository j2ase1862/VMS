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
    /// 검사 결과 업로드 요청
    /// </summary>
    public class ParameterResultUploadRequest
    {
        public int ClientIndex { get; set; }
        public int RecipeId { get; set; }
        public List<ParameterResultDto> Results { get; set; } = new();
    }
}
