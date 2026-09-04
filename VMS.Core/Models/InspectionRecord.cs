using System;
using System.Collections.Generic;

namespace VMS.Core.Models
{
    /// <summary>
    /// D8 — VMS 자체 검사 히스토리 1건. Web 의존 없이 작업자가 최근 N건을
    /// 사이드 패널에서 즉시 확인하기 위한 In-Memory 레코드.
    /// </summary>
    public class InspectionRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsPass { get; set; }
        public List<string> NgCodes { get; set; } = new();
        public int RecipeId { get; set; }
        public string? RecipeName { get; set; }
        public int? WorkOrderId { get; set; }
        public string? WorkOrderNo { get; set; }
        public int? LotId { get; set; }
        public string? SerialNumber { get; set; }

        public string NgCodesText => NgCodes.Count == 0 ? "" : string.Join(",", NgCodes);
        public string TimestampShort => Timestamp.ToString("HH:mm:ss");
        public string TimestampFull => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        public string DisplayLabel
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(SerialNumber)) parts.Add(SerialNumber);
                if (!string.IsNullOrEmpty(WorkOrderNo)) parts.Add(WorkOrderNo);
                else if (WorkOrderId.HasValue) parts.Add($"WO#{WorkOrderId}");
                if (!string.IsNullOrEmpty(RecipeName)) parts.Add(RecipeName!);
                if (parts.Count > 0) return string.Join(" · ", parts);
                // RecipeId 0 = 단독 모드/Web 미연동 레시피 — "Recipe#0" 은 오해를 준다.
                return RecipeId > 0 ? $"Recipe#{RecipeId}" : "(레시피 이름 없음)";
            }
        }
    }
}
