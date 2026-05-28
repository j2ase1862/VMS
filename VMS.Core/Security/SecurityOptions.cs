using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VMS.Core.Security
{
    /// <summary>
    /// 애플리케이션 보안 모드. 운영 환경에서는 Production 으로 강제.
    /// </summary>
    public enum SecurityMode
    {
        /// <summary>개발 모드 — HTTP 허용, self-signed cert 우회 가능. 로컬/사내 테스트만.</summary>
        Development,

        /// <summary>운영 모드 — HTTPS 강제, cert 엄격 검증. 출하 빌드 기본값.</summary>
        Production
    }

    /// <summary>
    /// 솔루션 전체 HttpClient / SignalR 의 보안 정책을 한 곳에서 관리.
    ///
    /// 사용 패턴:
    ///   var http = HttpClientPolicy.Build(TimeSpan.FromSeconds(5));
    /// 이렇게 하면 SecurityOptions.Current 의 모드에 따라 TLS / cert / UA 가 자동 적용.
    ///
    /// 시작 시 한 번 <see cref="LoadFromAppData"/> 를 호출해 system_config.json 의
    /// "securityMode" 키를 읽어 정책을 결정. 키가 없으면 Development 기본값.
    /// </summary>
    public sealed class SecurityOptions
    {
        /// <summary>보안 모드 (Development / Production).</summary>
        public SecurityMode Mode { get; init; } = SecurityMode.Development;

        /// <summary>HTTP URL 사용 시 예외 발생 강제 여부 — Production 에서만 true.</summary>
        public bool RequireHttps { get; init; }

        /// <summary>self-signed / 사설 CA 인증서 허용 — Development 한정.</summary>
        public bool AllowSelfSignedCert { get; init; }

        /// <summary>HTTP 요청 헤더의 User-Agent. 모든 HttpClient 가 공통으로 사용.</summary>
        public string UserAgent { get; init; } = "BODA-VMS/1.1.0";

        /// <summary>Development 프리셋 — HTTP 허용, self-signed 우회. 기존 동작 호환.</summary>
        public static SecurityOptions Development => new()
        {
            Mode = SecurityMode.Development,
            RequireHttps = false,
            AllowSelfSignedCert = true
        };

        /// <summary>Production 프리셋 — HTTPS 강제, cert 엄격 검증.</summary>
        public static SecurityOptions Production => new()
        {
            Mode = SecurityMode.Production,
            RequireHttps = true,
            AllowSelfSignedCert = false
        };

        // ─── 전역 접근자 ────────────────────────────────────────────────

        private static SecurityOptions? _current;

        /// <summary>
        /// 현재 활성 정책. App 시작 시 <see cref="LoadFromAppData"/> 가 set.
        /// 명시 set 없으면 Development 폴백.
        /// </summary>
        public static SecurityOptions Current
        {
            get => _current ?? Development;
            set => _current = value;
        }

        /// <summary>
        /// %LocalAppData%\BODA VISION AI\system_config.json 의 "securityMode" 키
        /// ("Production" / "Development") 를 읽어 Current 에 set. 키 누락 / 파일 없음
        /// 시 Development 기본값.
        /// </summary>
        public static void LoadFromAppData()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var path = Path.Combine(appData, "BODA VISION AI", "system_config.json");
                if (!File.Exists(path))
                {
                    Current = Development;
                    Debug.WriteLine("[Security] system_config.json 없음 — Development 모드.");
                    return;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("securityMode", out var prop)
                    && Enum.TryParse<SecurityMode>(prop.GetString(), ignoreCase: true, out var mode))
                {
                    Current = mode == SecurityMode.Production ? Production : Development;
                    Debug.WriteLine($"[Security] 모드 = {Current.Mode} (system_config.json).");
                }
                else
                {
                    Current = Development;
                    Debug.WriteLine("[Security] securityMode 키 누락 — Development 기본값.");
                }
            }
            catch (Exception ex)
            {
                Current = Development;
                Debug.WriteLine($"[Security] LoadFromAppData 실패 — Development 폴백: {ex.Message}");
            }
        }
    }
}
