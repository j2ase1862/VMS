using System;
using VMS.Core.Security;

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

        /// <summary>
        /// 외부 API 응답 sanitization — Phase 3b.
        /// Role 은 권한 결정에 직접 사용되므로 화이트리스트 밖 값은 최소권한 "Operator" 로 fallback.
        /// </summary>
        public OperatorSessionDto Sanitize()
        {
            const string ctx = nameof(OperatorSessionDto);
            Id = DtoValidator.ClampInt(Id, 0, 999_999_999, nameof(Id), ctx);
            OperatorId = DtoValidator.ClampInt(OperatorId, 0, 999_999_999, nameof(OperatorId), ctx);
            ClientId = DtoValidator.ClampInt(ClientId, 0, 999_999_999, nameof(ClientId), ctx);
            ClientIndex = DtoValidator.ClampInt(ClientIndex, 0, 999_999, nameof(ClientIndex), ctx);
            OperatorName = DtoValidator.Truncate(OperatorName, 200, nameof(OperatorName), ctx) ?? string.Empty;
            EmployeeNumber = DtoValidator.Truncate(EmployeeNumber, 100, nameof(EmployeeNumber), ctx) ?? string.Empty;
            Department = DtoValidator.Truncate(Department, 200, nameof(Department), ctx);
            ClientName = DtoValidator.Truncate(ClientName, 200, nameof(ClientName), ctx) ?? string.Empty;
            EndReason = DtoValidator.Truncate(EndReason, 500, nameof(EndReason), ctx);
            Role = DtoValidator.EnsureAllowed(
                Role, OperatorRoles.AllRoles, OperatorRoles.Operator, nameof(Role), ctx);
            return this;
        }
    }

    /// <summary>D10: 작업자 등급 상수 — Web 의 OperatorRole 과 동기화.</summary>
    public static class OperatorRoles
    {
        public const string Operator = "Operator";
        public const string Lead = "Lead";
        public const string Supervisor = "Supervisor";

        /// <summary>화이트리스트 — DtoValidator.EnsureAllowed 에서 사용.</summary>
        public static readonly System.Collections.Generic.IReadOnlyCollection<string> AllRoles
            = new[] { Operator, Lead, Supervisor };
    }
}
