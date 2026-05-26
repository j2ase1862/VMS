using System;

namespace VMS.Core.Models.Predictive
{
    /// <summary>
    /// Web 의 GET /api/predictions/current/{clientIndex} 응답.
    /// Predictive_DefectRate_Plan §5.3 — VMS 메인 화면 위젯이 60초 주기로 폴링.
    /// 모든 필드 nullable — Status 로 4-state 구분("ok"/"no_model"/"no_data"/"error").
    /// </summary>
    public class PredictionCurrentDto
    {
        public int ClientIndex { get; set; }
        public string? RecipeName { get; set; }
        public DateTime? WindowStart { get; set; }
        public double? PredictedNgRate { get; set; }
        public string? ModelName { get; set; }
        public string? ModelVersion { get; set; }
        public int InspectionCountThisHour { get; set; }
        public string Status { get; set; } = "";
        public string? Message { get; set; }
        public DateTime ServerUtc { get; set; }
    }
}
