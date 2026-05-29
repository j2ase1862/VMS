using System;
using VMS.Core.Security;

namespace VMS.Core.Models.ParameterSync
{
    /// <summary>Web의 WorkOrderDto와 와이어 호환 (CamelCase JSON). Stage 2.</summary>
    public class WorkOrderDto
    {
        public int Id { get; set; }
        public string OrderNo { get; set; } = string.Empty;

        public int ProductId { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }

        public int ClientId { get; set; }
        public string? ClientName { get; set; }
        public int? ClientIndex { get; set; }

        public int RecipeId { get; set; }
        public string? RecipeName { get; set; }

        public int PlannedQuantity { get; set; }
        public int ProducedQuantity { get; set; }
        public int PassQuantity { get; set; }
        public int NgQuantity { get; set; }

        public string Status { get; set; } = "Planned";

        public DateTime? PlannedStartAt { get; set; }
        public DateTime? ActualStartAt { get; set; }
        public DateTime? ActualEndAt { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // UI 표시용 (Web에 동일 로직 있음)
        public double PassRate => ProducedQuantity > 0
            ? Math.Round((double)PassQuantity / ProducedQuantity * 100, 2)
            : 0;

        public double Progress => PlannedQuantity > 0
            ? Math.Round((double)ProducedQuantity / PlannedQuantity * 100, 2)
            : 0;

        public string ProgressText => $"{ProducedQuantity} / {PlannedQuantity}";

        /// <summary>
        /// 외부 API 응답 sanitization — Phase 3c.
        /// WO 드롭다운 / 헤더 칩에 직접 바인딩되므로 음수 / 오버플로우 / 과길이 텍스트를 정상화.
        /// </summary>
        public WorkOrderDto Sanitize()
        {
            const string ctx = nameof(WorkOrderDto);
            Id = DtoValidator.ClampInt(Id, 0, 999_999_999, nameof(Id), ctx);
            ProductId = DtoValidator.ClampInt(ProductId, 0, 999_999_999, nameof(ProductId), ctx);
            ClientId = DtoValidator.ClampInt(ClientId, 0, 999_999_999, nameof(ClientId), ctx);
            RecipeId = DtoValidator.ClampInt(RecipeId, 0, 999_999_999, nameof(RecipeId), ctx);
            if (ClientIndex.HasValue)
                ClientIndex = DtoValidator.ClampInt(ClientIndex.Value, 0, 999_999, nameof(ClientIndex), ctx);
            PlannedQuantity = DtoValidator.ClampInt(PlannedQuantity, 0, 1_000_000, nameof(PlannedQuantity), ctx);
            ProducedQuantity = DtoValidator.ClampInt(ProducedQuantity, 0, 1_000_000, nameof(ProducedQuantity), ctx);
            PassQuantity = DtoValidator.ClampInt(PassQuantity, 0, 1_000_000, nameof(PassQuantity), ctx);
            NgQuantity = DtoValidator.ClampInt(NgQuantity, 0, 1_000_000, nameof(NgQuantity), ctx);
            OrderNo = DtoValidator.Truncate(OrderNo, 100, nameof(OrderNo), ctx) ?? string.Empty;
            ProductCode = DtoValidator.Truncate(ProductCode, 100, nameof(ProductCode), ctx);
            ProductName = DtoValidator.Truncate(ProductName, 200, nameof(ProductName), ctx);
            ClientName = DtoValidator.Truncate(ClientName, 200, nameof(ClientName), ctx);
            RecipeName = DtoValidator.Truncate(RecipeName, 200, nameof(RecipeName), ctx);
            Status = DtoValidator.Truncate(Status, 50, nameof(Status), ctx) ?? "Planned";
            Note = DtoValidator.Truncate(Note, 500, nameof(Note), ctx);
            return this;
        }
    }
}
