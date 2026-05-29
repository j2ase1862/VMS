using System;
using VMS.Core.Security;

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

        /// <summary>
        /// 외부 API 응답 sanitization — Phase 3b.
        /// PredictedNgRate 는 확률값이라 [0,1] 강제 — 위젯 UI 가 % 변환 시 음수/오버플로우 방지.
        /// </summary>
        public PredictionCurrentDto Sanitize()
        {
            const string ctx = nameof(PredictionCurrentDto);
            ClientIndex = DtoValidator.ClampInt(ClientIndex, 0, 999_999, nameof(ClientIndex), ctx);
            InspectionCountThisHour = DtoValidator.ClampInt(
                InspectionCountThisHour, 0, 1_000_000, nameof(InspectionCountThisHour), ctx);
            PredictedNgRate = DtoValidator.ClampDouble(
                PredictedNgRate, 0.0, 1.0, nameof(PredictedNgRate), ctx);
            RecipeName = DtoValidator.Truncate(RecipeName, 200, nameof(RecipeName), ctx);
            ModelName = DtoValidator.Truncate(ModelName, 200, nameof(ModelName), ctx);
            ModelVersion = DtoValidator.Truncate(ModelVersion, 100, nameof(ModelVersion), ctx);
            Status = DtoValidator.Truncate(Status, 50, nameof(Status), ctx) ?? "";
            Message = DtoValidator.Truncate(Message, 500, nameof(Message), ctx);
            return this;
        }
    }
}
