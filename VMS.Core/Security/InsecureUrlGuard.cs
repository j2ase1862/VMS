using System;
using System.Diagnostics;

namespace VMS.Core.Security
{
    /// <summary>
    /// 외부 API URL 의 보안 검증. Production 모드에서 http:// 사용 시 즉시 예외 발생,
    /// Development 모드에서는 디버그 로그 경고만.
    ///
    /// 사용 예 (서비스 ctor 또는 시작 시점):
    ///   InsecureUrlGuard.Check(webServerUrl, nameof(OperatorAuthService));
    /// </summary>
    public static class InsecureUrlGuard
    {
        /// <summary>
        /// URL 검증. http:// 시작 + Production 모드 = throw. 그 외 (https / 빈 URL / dev) = 통과 (경고만).
        /// </summary>
        /// <param name="url">검증 대상 URL.</param>
        /// <param name="source">호출 컨텍스트 (서비스 이름 등). 로그/예외 메시지에 포함.</param>
        public static void Check(string? url, string source)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
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
    }
}
