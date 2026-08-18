using VMS.Core.Security;

namespace VMS.Core.Models.ParameterSync
{
    /// <summary>
    /// Web 의 /api/parameters/results 응답에 포함된 작업지시 진행률 스냅샷.
    /// Stage 3 — 헤더 WO 칩 실시간 갱신 + 계획 수량 도달 알람용.
    /// </summary>
    public class WorkOrderProgressDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = "";
        public int PlannedQuantity { get; set; }
        public int ProducedQuantity { get; set; }
        public int PassQuantity { get; set; }
        public int NgQuantity { get; set; }
        public string Status { get; set; } = "";

        /// <summary>이 업로드로 계획 수량에 도달해 막 Completed 전이된 경우 true.</summary>
        public bool Completed { get; set; }

        /// <summary>
        /// 완료 기준 — "Pass"(양품 수량 기준) / "Produced"(총 생산 수량 기준).
        /// 구버전 Web 서버는 미전송 → 기본 Produced (기존 표시 유지).
        /// </summary>
        public string CompletionBasis { get; set; } = "Produced";

        /// <summary>완료 기준에 따른 진행 수량</summary>
        public int ProgressQuantity => CompletionBasis == "Pass" ? PassQuantity : ProducedQuantity;

        public double Progress => PlannedQuantity > 0
            ? System.Math.Round((double)ProgressQuantity / PlannedQuantity * 100, 1)
            : 0;

        public double PassRate => ProducedQuantity > 0
            ? System.Math.Round((double)PassQuantity / ProducedQuantity * 100, 1)
            : 0;

        /// <summary>
        /// 외부 API 응답 sanitization — Phase 3b.
        /// 음수 수량 / 비현실적 큰 값 / 과길이 문자열을 안전 범위로 정상화.
        /// 상한 1,000,000 은 한 작업지시당 일반 생산량 상한을 충분히 초과 — 위반 시 외부 손상으로 판단.
        /// </summary>
        public WorkOrderProgressDto Sanitize()
        {
            const string ctx = nameof(WorkOrderProgressDto);
            Id = DtoValidator.ClampInt(Id, 0, 999_999_999, nameof(Id), ctx);
            PlannedQuantity = DtoValidator.ClampInt(PlannedQuantity, 0, 1_000_000, nameof(PlannedQuantity), ctx);
            ProducedQuantity = DtoValidator.ClampInt(ProducedQuantity, 0, 1_000_000, nameof(ProducedQuantity), ctx);
            PassQuantity = DtoValidator.ClampInt(PassQuantity, 0, 1_000_000, nameof(PassQuantity), ctx);
            NgQuantity = DtoValidator.ClampInt(NgQuantity, 0, 1_000_000, nameof(NgQuantity), ctx);
            OrderNo = DtoValidator.Truncate(OrderNo, 100, nameof(OrderNo), ctx) ?? "";
            Status = DtoValidator.Truncate(Status, 50, nameof(Status), ctx) ?? "";
            CompletionBasis = DtoValidator.Truncate(CompletionBasis, 20, nameof(CompletionBasis), ctx) ?? "Produced";
            return this;
        }
    }
}
