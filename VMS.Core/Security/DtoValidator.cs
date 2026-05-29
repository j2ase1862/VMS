using System.Collections.Generic;
using System.Linq;

namespace VMS.Core.Security
{
    /// <summary>
    /// 외부 HTTP API 가 응답한 JSON DTO 의 필드 단위 sanitization.
    ///
    /// InputValidator 는 throw 위주 (호출자가 처리 결정), DtoValidator 는 clamp / truncate /
    /// default 위주 — UI 에 즉시 바인딩되는 폴링 DTO 가 외부에서 손상되어도 운영이 멈추지 않게 함.
    ///
    /// 모든 sanitization 위반은 <see cref="AuditCategory.Security"/> 에 Denied outcome 으로
    /// 기록되어 사후 추적 가능.
    /// </summary>
    public static class DtoValidator
    {
        /// <summary>이름/사번 등 짧은 문자열 기본 상한.</summary>
        public const int DefaultStringMaxLength = 500;

        /// <summary>설명/노트 류 긴 문자열 기본 상한.</summary>
        public const int DefaultLongStringMaxLength = 2000;

        /// <summary>정수 필드를 [min, max] 로 clamp. 범위 밖이면 Security audit + clamp 값 반환.</summary>
        public static int ClampInt(int value, int min, int max, string fieldName, string? context = null)
        {
            if (value >= min && value <= max) return value;
            var clamped = value < min ? min : max;
            AuditLogger.Instance.Log(
                AuditCategory.Security, "DtoFieldClamped", AuditOutcome.Denied,
                source: nameof(DtoValidator),
                details: $"{context ?? "?"}.{fieldName}={value} clamped to [{min},{max}] → {clamped}");
            return clamped;
        }

        /// <summary>
        /// double 필드를 [min, max] 로 clamp. NaN/Infinity 도 min 으로 정상화.
        /// </summary>
        public static double ClampDouble(double value, double min, double max, string fieldName, string? context = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Security, "DtoFieldClamped", AuditOutcome.Denied,
                    source: nameof(DtoValidator),
                    details: $"{context ?? "?"}.{fieldName}={value} → {min} (NaN/Inf)");
                return min;
            }
            if (value >= min && value <= max) return value;
            var clamped = value < min ? min : max;
            AuditLogger.Instance.Log(
                AuditCategory.Security, "DtoFieldClamped", AuditOutcome.Denied,
                source: nameof(DtoValidator),
                details: $"{context ?? "?"}.{fieldName}={value} clamped to [{min},{max}] → {clamped}");
            return clamped;
        }

        /// <summary>
        /// nullable double 필드 — null 은 그대로 통과, 값이 있으면 clamp 적용.
        /// </summary>
        public static double? ClampDouble(double? value, double min, double max, string fieldName, string? context = null)
        {
            if (!value.HasValue) return null;
            return ClampDouble(value.Value, min, max, fieldName, context);
        }

        /// <summary>문자열 상한 초과 시 truncate + Security audit. null 은 그대로 통과.</summary>
        public static string? Truncate(string? value, int maxLength, string fieldName, string? context = null)
        {
            if (value == null || value.Length <= maxLength) return value;
            AuditLogger.Instance.Log(
                AuditCategory.Security, "DtoFieldTruncated", AuditOutcome.Denied,
                source: nameof(DtoValidator),
                details: $"{context ?? "?"}.{fieldName} truncated from {value.Length} to {maxLength} chars");
            return value[..maxLength];
        }

        /// <summary>
        /// 열거형 문자열 필드 — 화이트리스트 밖이면 <paramref name="defaultValue"/> 로 대체 + Security audit.
        /// Role 같은 권한 결정 필드는 항상 최소권한 default 로 fallback 권장.
        /// </summary>
        public static string EnsureAllowed(
            string? value,
            IReadOnlyCollection<string> allowed,
            string defaultValue,
            string fieldName,
            string? context = null)
        {
            if (value != null && allowed.Contains(value)) return value;
            AuditLogger.Instance.Log(
                AuditCategory.Security, "DtoFieldRejected", AuditOutcome.Denied,
                source: nameof(DtoValidator),
                details: $"{context ?? "?"}.{fieldName}='{value}' not in allowed, defaulted to '{defaultValue}'");
            return defaultValue;
        }
    }
}
