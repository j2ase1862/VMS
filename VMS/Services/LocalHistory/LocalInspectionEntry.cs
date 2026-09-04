using System;
using System.Collections.Generic;

namespace VMS.Services.LocalHistory
{
    /// <summary>기록 단위 — AUTO RUN 사이클 1건("1사이클 = 1개") 또는 수동/트리거 검사 1건.</summary>
    public enum LocalInspectionMode
    {
        Manual = 0,
        Cycle = 1
    }

    /// <summary>도구 1개의 판정 요약 — 로컬 이력 상세 표시용 (측정 Data 전체는 저장하지 않는다).</summary>
    public sealed class LocalToolResult
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolType { get; set; } = string.Empty;
        public bool Success { get; set; }
        public double ExecutionTimeMs { get; set; }
        /// <summary>실패 시 도구 메시지 (성공이면 null 로 두어 JSON 을 작게 유지).</summary>
        public string? Message { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string ResultText => Success ? "OK" : "NG";
    }

    /// <summary>
    /// 로컬 검사 이력 1행. Web 의 InspectionHistory 와 달리 **로컬 레시피 이름·도구 이름 기반**이라
    /// 단독 모드에서도 채워진다. Web 연동 레시피면 RecipeId/WO/Lot 도 함께 남는다.
    /// </summary>
    public sealed class LocalInspectionEntry
    {
        public long Id { get; set; }
        public DateTime InspectedAtUtc { get; set; } = DateTime.UtcNow;
        public bool IsPass { get; set; }
        /// <summary>Web 레시피 ID (연동 시). 단독 모드/미연동이면 0.</summary>
        public int RecipeId { get; set; }
        public string? RecipeName { get; set; }
        /// <summary>Web 파라미터 NG 코드, 없으면 실패한 도구 이름 (Recent Inspections 와 동일 규약).</summary>
        public List<string> NgCodes { get; set; } = new();
        public List<LocalToolResult> ToolResults { get; set; } = new();
        /// <summary>결과 업로드·이미지 저장·Web 이력이 공유하는 상관 키 (이미지 경로 후속 갱신에 사용).</summary>
        public string? CorrelationKey { get; set; }
        /// <summary>InspectionImageSaver 가 저장한 파일 경로 (저장 옵션이 꺼져 있으면 null).</summary>
        public string? ImagePath { get; set; }
        /// <summary>ImagePath 가 NG 이미지인지 — 사이클 내 OK 이미지보다 NG 이미지를 우선 보관.</summary>
        public bool ImageIsNg { get; set; }
        public int? WorkOrderId { get; set; }
        public int? LotId { get; set; }
        public string? SerialNumber { get; set; }
        public int? CycleTimeMs { get; set; }
        public LocalInspectionMode Mode { get; set; } = LocalInspectionMode.Manual;

        public DateTime InspectedAtLocal => InspectedAtUtc.ToLocalTime();
        public string NgCodesText => NgCodes.Count == 0 ? string.Empty : string.Join(",", NgCodes);

        // ─── 표시용 (조회 창 DataGrid 바인딩) ───
        public string VerdictText => IsPass ? "PASS" : "NG";
        public string ModeText => Mode == LocalInspectionMode.Cycle ? "사이클" : "수동";
        public bool HasImage => !string.IsNullOrEmpty(ImagePath);
        /// <summary>WO/Lot/Serial 을 한 칸에 — 단독 모드에서는 비어 있다.</summary>
        public string TraceText
        {
            get
            {
                var parts = new List<string>(3);
                if (!string.IsNullOrEmpty(SerialNumber)) parts.Add(SerialNumber!);
                if (WorkOrderId.HasValue) parts.Add($"WO#{WorkOrderId}");
                if (LotId.HasValue) parts.Add($"Lot#{LotId}");
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>조회 필터 — 모든 조건은 AND. 시각은 로컬 시간(화면 입력값) 기준.</summary>
    public sealed class LocalInspectionQuery
    {
        public DateTime? FromLocal { get; set; }
        /// <summary>배타 상한 (예: 종료일 다음날 00:00).</summary>
        public DateTime? ToLocalExclusive { get; set; }
        public bool? IsPass { get; set; }
        public string? RecipeName { get; set; }
        /// <summary>NG 코드 문자열에 포함(LIKE) — 단독 모드에서는 도구 이름.</summary>
        public string? NgCodeContains { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; } = 200;
    }

    /// <summary>일별 집계 1행 (로컬 날짜 기준).</summary>
    public sealed class LocalInspectionDailySummary
    {
        public DateOnly Date { get; init; }
        public int Total { get; init; }
        public int Pass { get; init; }
        public int Ng { get; init; }
        public double PassRate => Total > 0 ? Math.Round((double)Pass / Total * 100, 1) : 0;
    }

    /// <summary>NG 코드(또는 도구 이름)별 건수 — 파레토용.</summary>
    public sealed class LocalNgCodeCount
    {
        public string Code { get; init; } = string.Empty;
        public int Count { get; init; }
    }
}
