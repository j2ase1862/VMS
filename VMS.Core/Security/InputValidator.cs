using System;
using System.IO;
using System.Linq;

namespace VMS.Core.Security
{
    /// <summary>
    /// 외부 입력(파일 경로 / PLC 주소 / JSON 응답 등) 검증 표준화 헬퍼.
    ///
    /// 위치: VMS.Core/Security/ — CredentialGuard 와 같은 폴더.
    /// 정책: 검증 실패는 즉시 ArgumentException / InvalidOperationException 발생 — 호출자가
    /// catch + 사용자 메시지 표시 또는 fail-fast 결정.
    /// </summary>
    public static class InputValidator
    {
        // ─── 파일 경로 ─────────────────────────────────────────────────

        /// <summary>
        /// Path traversal 방어 — 정규화된 절대 경로가 허용된 디렉토리 내부인지 검증.
        /// '../../windows/system32' 또는 절대 경로 점프 차단.
        /// </summary>
        /// <returns>true: 허용 디렉토리 내부 / false: 외부 또는 정규화 실패.</returns>
        public static bool IsPathWithinDirectory(string filePath, string allowedDirectory)
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(allowedDirectory))
                return false;
            try
            {
                var fullFile = Path.GetFullPath(filePath);
                var fullDir = Path.GetFullPath(allowedDirectory);
                // 디렉토리 구분자 보장 — '/foo' 가 '/foobar' 의 접두라 매치하는 false-positive 차단.
                if (!fullDir.EndsWith(Path.DirectorySeparatorChar))
                    fullDir += Path.DirectorySeparatorChar;
                return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Path traversal 방어 — 위반 시 ArgumentException 발생.
        /// </summary>
        public static void EnsurePathInside(string filePath, string allowedDirectory, string paramName)
        {
            if (!IsPathWithinDirectory(filePath, allowedDirectory))
                throw new ArgumentException(
                    $"경로 '{filePath}' 가 허용 디렉토리 '{allowedDirectory}' 밖에 있습니다.", paramName);
        }

        /// <summary>
        /// 파일 확장자 화이트리스트 검증. 확장자 인자는 점 포함('.json', '.onnx') 또는 미포함 모두 허용.
        /// </summary>
        public static bool HasAllowedExtension(string filePath, params string[] allowedExtensions)
        {
            if (string.IsNullOrWhiteSpace(filePath) || allowedExtensions == null || allowedExtensions.Length == 0)
                return false;
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            return allowedExtensions.Any(e =>
            {
                var normalized = e.StartsWith('.') ? e : "." + e;
                return ext.Equals(normalized, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// 확장자 화이트리스트 위반 시 ArgumentException 발생.
        /// </summary>
        public static void EnsureAllowedExtension(string filePath, string paramName, params string[] allowedExtensions)
        {
            if (!HasAllowedExtension(filePath, allowedExtensions))
                throw new ArgumentException(
                    $"허용되지 않은 파일 확장자 — 허용: {string.Join(", ", allowedExtensions)}. 입력: '{filePath}'", paramName);
        }

        // ─── 문자열 / 숫자 일반 검증 ─────────────────────────────────

        /// <summary>문자열 길이가 [min, max] 범위 내인지.</summary>
        public static bool IsStringLengthValid(string? value, int minLength, int maxLength)
        {
            if (value == null) return minLength == 0;
            return value.Length >= minLength && value.Length <= maxLength;
        }

        /// <summary>숫자가 [min, max] 범위 내인지.</summary>
        public static bool IsInRange<T>(T value, T min, T max) where T : IComparable<T>
        {
            return value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0;
        }

        // ─── HTTP 응답 크기 ───────────────────────────────────────

        /// <summary>HTTP 응답 본문 기본 크기 상한 (10 MB) — JSON DDoS / 메모리 폭탄 방어.</summary>
        public const int DefaultMaxResponseBytes = 10 * 1024 * 1024;

        /// <summary>응답 ContentLength 가 상한 내인지 검증 (null=알 수 없음=통과).</summary>
        public static bool IsResponseSizeValid(long? contentLength, int maxBytes = DefaultMaxResponseBytes)
        {
            if (!contentLength.HasValue) return true;  // 서버가 Content-Length 안 보낸 경우
            return contentLength.Value <= maxBytes;
        }
    }
}
