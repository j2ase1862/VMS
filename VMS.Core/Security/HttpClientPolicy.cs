using System;
using System.Net.Http;
using System.Security.Authentication;

namespace VMS.Core.Security
{
    /// <summary>
    /// 솔루션 전체 HttpClient 인스턴스의 표준 빌더.
    ///
    /// 기존 `new HttpClient { Timeout = ... }` 패턴을 한 곳으로 모아
    /// TLS 버전·인증서 검증·User-Agent 헤더를 일관 적용한다. SecurityOptions.Current
    /// 의 모드(Development/Production) 에 따라 cert 검증 엄격도가 자동 결정.
    ///
    /// 사용 예:
    ///   _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(5));
    /// </summary>
    public static class HttpClientPolicy
    {
        /// <summary>HttpClient.MaxResponseContentBufferSize 기본값 (10 MB) — JSON DDoS / 메모리 폭탄 방어.</summary>
        public const long DefaultMaxResponseBytes = InputValidator.DefaultMaxResponseBytes;

        /// <summary>
        /// 표준 HttpClient 인스턴스 생성. TLS 1.2 / 1.3 만 활성, User-Agent 자동 설정,
        /// Production 모드면 cert 엄격 검증 (self-signed 거부), 응답 크기 상한 적용.
        /// </summary>
        /// <param name="timeout">요청 타임아웃.</param>
        /// <param name="options">선택적 정책. null 이면 SecurityOptions.Current 사용.</param>
        /// <param name="maxResponseBytes">응답 본문 최대 크기. 큰 모델 파일 다운로드 등 예외 케이스만 override.</param>
        public static HttpClient Build(
            TimeSpan timeout,
            SecurityOptions? options = null,
            long maxResponseBytes = DefaultMaxResponseBytes)
        {
            options ??= SecurityOptions.Current;

            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                       | System.Net.DecompressionMethods.Deflate
            };

            // Production 모드에서는 cert 검증 우회 차단.
            // Development 모드에서만 self-signed / 사설 CA 인증서를 허용 (사내 테스트용).
            if (options.AllowSelfSignedCert && options.Mode == SecurityMode.Development)
            {
                handler.ServerCertificateCustomValidationCallback =
                    (request, cert, chain, sslErrors) =>
                    {
                        // Development 라도 호스트가 'localhost' / '127.0.0.1' / 사내망(10.x, 192.168.x)
                        // 이 아니면 검증 통과시키지 않음 — 임의 도메인의 self-signed 차단.
                        var host = request?.RequestUri?.Host ?? string.Empty;
                        if (IsLocalOrPrivateHost(host)) return true;
                        return sslErrors == System.Net.Security.SslPolicyErrors.None;
                    };
            }
            // Production: 콜백 미설정 → OS 기본 검증 (엄격)

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = timeout,
                // 응답 본문이 이 크기를 초과하면 HttpRequestException 발생 — 메모리 폭탄 차단.
                // 모델 파일 다운로드 등은 maxResponseBytes 인자로 override.
                MaxResponseContentBufferSize = maxResponseBytes
            };
            if (!string.IsNullOrWhiteSpace(options.UserAgent))
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            return client;
        }

        /// <summary>
        /// localhost / 127.0.0.1 / 사설 IP 대역 (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16)
        /// 또는 .local 도메인 판정. Development 모드의 cert 우회를 사내망으로 제한.
        /// </summary>
        private static bool IsLocalOrPrivateHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

            if (System.Net.IPAddress.TryParse(host, out var ip))
            {
                if (System.Net.IPAddress.IsLoopback(ip)) return true;
                var bytes = ip.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    // 10.0.0.0/8
                    if (bytes[0] == 10) return true;
                    // 172.16.0.0/12
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                    // 192.168.0.0/16
                    if (bytes[0] == 192 && bytes[1] == 168) return true;
                }
            }
            return false;
        }
    }
}
