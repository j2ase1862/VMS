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

    /// <summary>현재 Current 가 결정된 출처 — startup health check / 감사 추적에 사용.</summary>
    public enum SecurityModeSource
    {
        /// <summary>아직 LoadFromAppData 미호출 — 코드 디폴트(Development) 사용 중.</summary>
        Default,

        /// <summary>BODA_VMS_SECURITY_MODE 환경변수에서 결정 (최우선 신호).</summary>
        Environment,

        /// <summary>system_config.json 의 securityMode 키에서 결정.</summary>
        ConfigFile,

        /// <summary>
        /// 환경변수도 config 도 없거나 파싱 실패 — Development 폴백.
        /// GS 보안성 항목에서 자동 다운그레이드가 발생한 상태 — AuditLog 기록 + UI 경고 필요.
        /// </summary>
        FallbackOnError
    }

    /// <summary>config 누락/오류 시 어떻게 처리할지 정책.</summary>
    public enum SecurityLoadPolicy
    {
        /// <summary>
        /// 현행 호환 — 누락/오류 시 Development 폴백 + 디버그 로그. AuditLogger 가 사용 가능하면
        /// Security 카테고리 Failure 로 기록해 다운그레이드를 감사 추적.
        /// </summary>
        WarnOnFallback,

        /// <summary>
        /// 운영 강제 모드 — config / 환경변수 둘 다 명시되지 않거나 파싱 실패하면
        /// InvalidOperationException 으로 부팅 중단. MSI 배포 후 system_config.json 손상시
        /// 보안 모드가 조용히 Development 로 다운그레이드되는 위험을 차단.
        /// 운영자가 환경변수 BODA_VMS_SECURITY_MODE 또는 명시 config 로 결정해야 함.
        /// </summary>
        RequireExplicit
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
        /// Current 가 결정된 출처. StartupHealthCheck / AuditLog 에서 활용.
        /// LoadFromAppData 미호출 시 <see cref="SecurityModeSource.Default"/>.
        /// </summary>
        public static SecurityModeSource CurrentSource { get; private set; } = SecurityModeSource.Default;

        /// <summary>
        /// BODA_VMS_SECURITY_MODE 환경변수가 명시되면 (최우선) 그 값으로 set.
        /// 그 다음 system_config.json 의 securityMode 키 사용.
        /// 둘 다 없으면 정책에 따라 Development 폴백 또는 InvalidOperationException.
        ///
        /// 우선순위 (high → low):
        ///   1. 환경변수 BODA_VMS_SECURITY_MODE (운영자가 MSI 배포 후 강제 설정 가능)
        ///   2. %LocalAppData%\BODA VISION AI\system_config.json 의 securityMode 키
        ///   3. policy=WarnOnFallback → Development (현행 호환)
        ///      policy=RequireExplicit → throw (보안 다운그레이드 차단)
        /// </summary>
        /// <param name="policy">config 누락/오류 시 처리 정책.</param>
        /// <param name="appDataOverride">
        /// 테스트 전용 — null 이면 Environment.GetFolderPath(LocalApplicationData) 사용.
        /// 운영 코드는 절대 명시하지 말 것 (실제 사용자 AppData 만 사용).
        /// </param>
        public static void LoadFromAppData(
            SecurityLoadPolicy policy = SecurityLoadPolicy.WarnOnFallback,
            string? appDataOverride = null)
        {
            // 1. 환경변수 (가장 강한 신호)
            var envValue = Environment.GetEnvironmentVariable("BODA_VMS_SECURITY_MODE");
            if (!string.IsNullOrWhiteSpace(envValue)
                && Enum.TryParse<SecurityMode>(envValue, ignoreCase: true, out var envMode))
            {
                Current = envMode == SecurityMode.Production ? Production : Development;
                CurrentSource = SecurityModeSource.Environment;
                Debug.WriteLine($"[Security] 모드 = {Current.Mode} (BODA_VMS_SECURITY_MODE 환경변수).");
                return;
            }

            // 2. system_config.json
            try
            {
                var appData = appDataOverride
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var path = Path.Combine(appData, "BODA VISION AI", "system_config.json");
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("securityMode", out var prop)
                        && Enum.TryParse<SecurityMode>(prop.GetString(), ignoreCase: true, out var mode))
                    {
                        Current = mode == SecurityMode.Production ? Production : Development;
                        CurrentSource = SecurityModeSource.ConfigFile;
                        Debug.WriteLine($"[Security] 모드 = {Current.Mode} (system_config.json).");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Security] system_config.json 파싱 실패: {ex.Message}");
                // 다음 단계로 폴백 (try/catch 만으로는 명시적 정책 결정 불가)
            }

            // 3. 폴백 — 정책에 따라 다른 동작
            if (policy == SecurityLoadPolicy.RequireExplicit)
            {
                throw new InvalidOperationException(
                    "보안 모드가 명시되지 않았습니다. " +
                    "운영 배포에서는 다음 중 하나로 모드를 명시해야 합니다:\n" +
                    "  - 환경변수: setx BODA_VMS_SECURITY_MODE Production /M\n" +
                    "  - %LocalAppData%\\BODA VISION AI\\system_config.json 의 \"securityMode\" 키\n" +
                    "Development 자동 폴백은 GS 보안성 항목에서 자동 다운그레이드로 감점 사유입니다.");
            }

            // WarnOnFallback — 다운그레이드를 AuditLog 에 기록해 사후 추적 가능
            Current = Development;
            CurrentSource = SecurityModeSource.FallbackOnError;
            Debug.WriteLine("[Security] 환경변수 / system_config.json 미명시 — Development 폴백.");
            try
            {
                AuditLogger.Instance.Log(
                    AuditCategory.Security,
                    "Security mode fallback to Development",
                    AuditOutcome.Failure,
                    source: nameof(SecurityOptions),
                    details: "BODA_VMS_SECURITY_MODE 환경변수와 system_config.json:securityMode 둘 다 명시되지 않음. " +
                             "운영 환경이면 환경변수 또는 config 로 Production 명시 권장.");
            }
            catch
            {
                // AuditLogger 자체가 실패해도 보안 정책 결정은 진행 (best-effort)
            }
        }
    }
}
