using System;

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
    }
}
