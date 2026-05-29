using System;
using VMS.Core.Security;

namespace VMS.Core.Models.ParameterSync
{
    /// <summary>Web의 LotDto와 와이어 호환 (CamelCase JSON). B1 — WO 선택 시 활성 Lot 자동 채움.</summary>
    public class LotDto
    {
        public int Id { get; set; }
        public string LotNumber { get; set; } = string.Empty;
        public int WorkOrderId { get; set; }
        public string? WorkOrderNo { get; set; }
        public int Sequence { get; set; }
        public int Quantity { get; set; }
        public int PassCount { get; set; }
        public int NgCount { get; set; }
        public string Status { get; set; } = "Open";
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string? Note { get; set; }

        public double NgRate => Quantity > 0
            ? Math.Round((double)NgCount / Quantity * 100, 2)
            : 0;

        /// <summary>
        /// 외부 API 응답 sanitization — Phase 3c.
        /// WO 선택 시 자동 채움되는 Lot 필드 — UI / NgRate 계산 안정성 확보.
        /// </summary>
        public LotDto Sanitize()
        {
            const string ctx = nameof(LotDto);
            Id = DtoValidator.ClampInt(Id, 0, 999_999_999, nameof(Id), ctx);
            WorkOrderId = DtoValidator.ClampInt(WorkOrderId, 0, 999_999_999, nameof(WorkOrderId), ctx);
            Sequence = DtoValidator.ClampInt(Sequence, 0, 999_999, nameof(Sequence), ctx);
            Quantity = DtoValidator.ClampInt(Quantity, 0, 1_000_000, nameof(Quantity), ctx);
            PassCount = DtoValidator.ClampInt(PassCount, 0, 1_000_000, nameof(PassCount), ctx);
            NgCount = DtoValidator.ClampInt(NgCount, 0, 1_000_000, nameof(NgCount), ctx);
            LotNumber = DtoValidator.Truncate(LotNumber, 100, nameof(LotNumber), ctx) ?? string.Empty;
            WorkOrderNo = DtoValidator.Truncate(WorkOrderNo, 100, nameof(WorkOrderNo), ctx);
            Status = DtoValidator.Truncate(Status, 50, nameof(Status), ctx) ?? "Open";
            Note = DtoValidator.Truncate(Note, 500, nameof(Note), ctx);
            return this;
        }
    }
}
