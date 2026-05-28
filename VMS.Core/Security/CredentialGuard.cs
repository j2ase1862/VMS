using System;
using System.Linq;

namespace VMS.Core.Security
{
    /// <summary>
    /// 자격증명(사번/PIN/비밀번호) 의 입력 검증 + 메모리 처리 헬퍼.
    ///
    /// **string 메모리 정리의 한계**: .NET string 은 immutable + interning 가능성이 있어
    /// 완벽한 zero-clearing 이 불가능합니다. SecureString 은 cross-platform 미지원으로
    /// .NET 8 에서 사실상 deprecated. 따라서 이 헬퍼는:
    ///   1. **참조 해제** — 명시적 null 대입으로 GC 후보로 만들어 잔존 시간 최소화
    ///   2. **JSON 직렬화 객체 정리** — DTO 의 자격증명 필드를 송신 직후 비움
    /// 두 가지 방어선만 제공합니다. 완벽한 보호는 토큰 기반 인증 도입 (별도 PR) 필요.
    /// </summary>
    public static class CredentialGuard
    {
        /// <summary>사번/사용자 ID 의 일반적 길이 상한 — DoS 방어용.</summary>
        public const int MaxIdentifierLength = 64;

        /// <summary>PIN/비밀번호 길이 상한.</summary>
        public const int MaxSecretLength = 128;

        /// <summary>
        /// 자격증명 식별자(사번/사용자 ID) 검증. null/빈/너무 김/제어문자 포함 시 ArgumentException.
        /// </summary>
        public static void ValidateIdentifier(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("식별자가 비어 있습니다.", paramName);
            if (value.Length > MaxIdentifierLength)
                throw new ArgumentException($"식별자가 너무 깁니다 ({MaxIdentifierLength}자 초과).", paramName);
            if (ContainsControlChar(value))
                throw new ArgumentException("식별자에 제어문자가 포함될 수 없습니다.", paramName);
        }

        /// <summary>
        /// PIN / 비밀번호 검증. null/빈/너무 김/제어문자(개행/탭 등) 포함 시 ArgumentException.
        /// 길이 / 복잡도 정책은 인증 정책마다 다르므로 여기서는 안전성만 검사 (storage 안전).
        /// </summary>
        public static void ValidateSecret(string? value, string paramName, int minLength = 1)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("암호가 비어 있습니다.", paramName);
            if (value.Length < minLength)
                throw new ArgumentException($"암호는 최소 {minLength}자 이상이어야 합니다.", paramName);
            if (value.Length > MaxSecretLength)
                throw new ArgumentException($"암호가 너무 깁니다 ({MaxSecretLength}자 초과).", paramName);
            if (ContainsControlChar(value))
                throw new ArgumentException("암호에 제어문자가 포함될 수 없습니다.", paramName);
        }

        /// <summary>
        /// DTO 의 자격증명 필드를 즉시 빈 문자열로 덮어써 GC 후보로 만듦.
        /// 호출자가 송신 직후 finally 에서 사용.
        ///
        /// 예: <code>
        /// var req = new KioskLoginRequest { Pin = pin };
        /// try { await post(req); }
        /// finally { CredentialGuard.ClearSecretField(ref req.Pin); }
        /// </code>
        ///
        /// 주의: ref param 의 string 도 immutable 이라 실제 메모리 zero 보장 안 됨.
        /// 참조 끊기 + GC 압력 줄이기만 보장.
        /// </summary>
        public static void ClearSecretField(ref string field)
        {
            field = string.Empty;
        }

        /// <summary>byte[] 형태의 자격증명을 0 으로 덮어씀. byte[] 는 mutable 이라 실효성 있음.</summary>
        public static void ZeroFill(byte[]? buffer)
        {
            if (buffer == null) return;
            Array.Clear(buffer, 0, buffer.Length);
        }

        private static bool ContainsControlChar(string value)
        {
            // 제어문자 (개행/탭/NUL/escape) — 이중 호출/로그 위조 / SQL 등 공격 벡터 차단.
            return value.Any(c => char.IsControl(c));
        }
    }
}
