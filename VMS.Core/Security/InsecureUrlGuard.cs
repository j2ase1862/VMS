using System;
using System.Diagnostics;

namespace VMS.Core.Security
{
    /// <summary>
    /// 외부 API URL 의 보안 검증. Production 모드에서 http:// 사용 시 즉시 예외 발생,
    /// Development 모드에서는 디버그 로그 경고만.
    ///
    /// 예외: loopback 호스트(localhost / 127.0.0.0/8 / [::1])의 http:// 는 Production 에서도
    /// 허용. 오프라인 단일 PC 구성(Web 서버가 같은 PC 의 Kestrel HTTP)에서 트래픽이 PC 밖으로
    /// 나가지 않으므로 평문 노출 위험이 없고, 공인 인증서 발급도 불가능한 환경이기 때문.
    /// 허용 시 감사 로그(InsecureHttpLoopbackAllowed)로 흔적을 남긴다.
    ///
    /// 사용 예 (서비스 ctor 또는 시작 시점):
    ///   InsecureUrlGuard.Check(webServerUrl, nameof(OperatorAuthService));
    /// </summary>
    public static class InsecureUrlGuard
    {
        /// <summary>
        /// URL 검증. http:// 시작 + Production 모드 = throw (loopback 호스트 제외).
        /// 그 외 (https / 빈 URL / dev / loopback) = 통과 (경고/감사 로그만).
        /// </summary>
        /// <param name="url">검증 대상 URL.</param>
        /// <param name="source">호출 컨텍스트 (서비스 이름 등). 로그/예외 메시지에 포함.</param>
        public static void Check(string? url, string source)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                if (IsLoopback(url))
                {
                    Debug.WriteLine($"[Security] '{source}' is using HTTP on loopback: {url} (allowed)");
                    AuditLogger.Instance.Log(
                        AuditCategory.Security, "InsecureHttpLoopbackAllowed", AuditOutcome.Success,
                        source: source,
                        details: $"URL='{url}' — loopback 호스트라 Production 에서도 HTTP 허용");
                    return;
                }

                if (SecurityOptions.Current.RequireHttps)
                {
                    AuditLogger.Instance.Log(
                        AuditCategory.Security, "InsecureHttpBlocked", AuditOutcome.Denied,
                        source: source,
                        details: $"URL='{url}' — Production 모드에서 HTTP 거부");
                    throw new InvalidOperationException(
                        $"보안 정책 위반 — Production 모드에서 HTTPS 가 필수입니다. " +
                        $"소스 '{source}' URL='{url}'. system_config.json 의 webServerUrl / visionServerUrl 을 " +
                        $"https:// 로 수정하거나 securityMode 를 Development 로 변경하세요.");
                }
                Debug.WriteLine($"[Security] WARN: '{source}' is using insecure HTTP: {url}");
                AuditLogger.Instance.Log(
                    AuditCategory.Security, "InsecureHttpWarning", AuditOutcome.Success,
                    source: source,
                    details: $"URL='{url}' — Development 모드 허용 (운영 전환 시 검토 필요)");
            }
        }

        /// <summary>
        /// URL 호스트가 loopback 인지 판정 — "localhost" / 127.0.0.0/8 / [::1] 리터럴만 인정.
        /// hosts 파일 별칭 등 간접 loopback 은 보수적으로 비허용 (원격 호스트로 취급).
        /// Uri.IsLoopback 단독으로는 부족: .NET 8 에서 "http://LOCALHOST" 처럼 원본이
        /// 대문자면 Host 는 정규화돼도 IsLoopback 이 false 를 반환한다.
        /// </summary>
        private static bool IsLoopback(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.IsLoopback) return true;
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return System.Net.IPAddress.TryParse(uri.Host, out var ip)
                && System.Net.IPAddress.IsLoopback(ip);
        }
    }
}
