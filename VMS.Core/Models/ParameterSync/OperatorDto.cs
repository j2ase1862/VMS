using System;

namespace VMS.Core.Models.ParameterSync
{
    /// <summary>Web의 KioskLoginRequest와 일치 — Phase Stage 1.</summary>
    public class KioskLoginRequest
    {
        public int ClientIndex { get; set; }
        public string EmployeeNumber { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
    }

    public class KioskLogoutRequest
    {
        public int ClientIndex { get; set; }
    }

    /// <summary>Web의 OperatorSessionDto 와 와이어 호환 (CamelCase JSON).</summary>
    public class OperatorSessionDto
    {
        public int Id { get; set; }
        public int OperatorId { get; set; }
        public string OperatorName { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public string? Department { get; set; }

        /// <summary>D10: 작업자 등급 — "Operator" / "Lead" / "Supervisor". VMS 메뉴 가시성 제어.</summary>
        public string Role { get; set; } = "Operator";

        public int ClientId { get; set; }
        public int ClientIndex { get; set; }
        public string ClientName { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string? EndReason { get; set; }

        public bool IsActive => !EndedAt.HasValue;
    }

    /// <summary>D10: 작업자 등급 상수 — Web 의 OperatorRole 과 동기화.</summary>
    public static class OperatorRoles
    {
        public const string Operator = "Operator";
        public const string Lead = "Lead";
        public const string Supervisor = "Supervisor";
    }
}
