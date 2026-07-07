using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VMS.Core.Security
{
    /// <summary>
    /// VMS 데스크탑이 BODA.VMS.Web 으로 SSO 인증을 위임할지 여부 + Web 서버 URL.
    /// system_config.json 의 "webSso" 객체에서 읽음 — 누락시 비활성 기본값.
    ///
    /// SSO Migration (docs/gs/guides/SSO_Migration_Plan.md):
    /// - PR2 (본 PR): 본 설정 로드 + UserService.AuthenticateViaWebAsync 추가. 호출자 미적용.
    /// - PR4: AppSetup wizard UI 로 활성화 + LoginViewModel 분기
    /// </summary>
    public sealed class WebSsoConfig
    {
        /// <summary>SSO 활성 여부. 기본 false — 현행 로컬 인증 유지.</summary>
        public bool Enabled { get; init; }

        /// <summary>Web 서버 URL. system_config.json:webServerUrl 가 우선 — 본 값은 호환 대비.</summary>
        public string WebServerUrl { get; init; } = string.Empty;

        /// <summary>비활성 기본 인스턴스.</summary>
        public static readonly WebSsoConfig Disabled = new();

        /// <summary>
        /// %LocalAppData%\BODA VISION AI\system_config.json 의 "webSso" 객체에서 로드.
        /// 우선순위: webSso.webServerUrl → 루트 webServerUrl → 빈 문자열.
        /// </summary>
        public static WebSsoConfig LoadFromAppData(string? appDataOverride = null)
        {
            try
            {
                var appData = appDataOverride
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "BODA VISION AI");
                var path = Path.Combine(appData, "system_config.json");
                if (!File.Exists(path)) return Disabled;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                // webSso 객체 우선
                bool enabled = false;
                string url = string.Empty;
                if (root.TryGetProperty("webSso", out var sso) && sso.ValueKind == JsonValueKind.Object)
                {
                    if (sso.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True)
                        enabled = true;
                    if (sso.TryGetProperty("webServerUrl", out var u) && u.ValueKind == JsonValueKind.String)
                        url = u.GetString() ?? string.Empty;
                }

                // url 폴백 — 루트 webServerUrl (기존 운영 설정 호환)
                if (string.IsNullOrWhiteSpace(url)
                    && root.TryGetProperty("webServerUrl", out var rootUrl)
                    && rootUrl.ValueKind == JsonValueKind.String)
                {
                    url = rootUrl.GetString() ?? string.Empty;
                }

                return new WebSsoConfig
                {
                    Enabled = enabled,
                    WebServerUrl = url
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSsoConfig] LoadFromAppData 실패: {ex.Message}");
                return Disabled;
            }
        }
    }
}
