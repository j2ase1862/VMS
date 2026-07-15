using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VMS.Core.Services
{
    /// <summary>
    /// VMS 데스크탑이 BODA.VMS.Web 의 /api/auth/login 을 호출해 JWT 를 받는 클라이언트.
    /// SSO 마이그레이션 (docs/gs/guides/SSO_Migration_Plan.md) 의 인프라 컴포넌트 — 본 클래스는
    /// 호출 책임만 가지며, UserService 통합은 PR2 에서 수행.
    ///
    /// 결과 분류 (WebAuthResultKind):
    /// - Success    — JWT 발급, Token / DisplayName / Role 반환
    /// - InvalidCredentials — Web 이 401 반환 (사용자에게 표시)
    /// - WebUnreachable — 네트워크 오류 / 5xx / timeout — 호출자는 비상 폴백 정책 판단
    /// - ServerError — Web 이 예상치 못한 응답 (4xx 외 / 빈 응답) — 가이드 메시지 표시
    /// </summary>
    public sealed class WebAuthClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly Uri _loginUrl;

        public WebAuthClient(string webServerUrl, HttpClient? httpClient = null, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(webServerUrl))
                throw new ArgumentException("webServerUrl 비어 있음", nameof(webServerUrl));

            // 자격증명이 평문으로 실리는 유일한 VMS→Web 경로 — 다른 Web 연동 서비스와
            // 동일하게 보안 정책 적용 (Production 은 원격 http 거부, loopback 은 허용).
            Security.InsecureUrlGuard.Check(webServerUrl, nameof(WebAuthClient));

            _loginUrl = new Uri(new Uri(webServerUrl.TrimEnd('/') + "/"), "api/auth/login");

            if (httpClient is null)
            {
                // TLS 1.2/1.3 + Production cert 엄격 검증 + UA + 응답 크기 상한.
                _http = Security.HttpClientPolicy.Build(timeout ?? TimeSpan.FromSeconds(5));
                _ownsHttp = true;
            }
            else
            {
                _http = httpClient;
                _ownsHttp = false;
            }
        }

        public async Task<WebAuthResult> LoginAsync(
            string username,
            string password,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                return WebAuthResult.Invalid("username 비어 있음");
            if (string.IsNullOrWhiteSpace(password))
                return WebAuthResult.Invalid("password 비어 있음");

            HttpResponseMessage resp;
            try
            {
                var body = new LoginRequestDto { Username = username, Password = password };
                resp = await _http.PostAsJsonAsync(_loginUrl, body, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                // HttpClient timeout — Web 도달 불가 또는 응답 지연
                return WebAuthResult.Unreachable($"timeout after {_http.Timeout.TotalSeconds:F0}s: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                return WebAuthResult.Unreachable($"network: {ex.Message}");
            }
            catch (Exception ex)
            {
                return WebAuthResult.Unreachable($"unexpected: {ex.GetType().Name} {ex.Message}");
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                return WebAuthResult.Invalid("Web rejected credentials (401)");
            }
            if ((int)resp.StatusCode >= 500)
            {
                return WebAuthResult.Unreachable($"server 5xx: {(int)resp.StatusCode}");
            }
            if (!resp.IsSuccessStatusCode)
            {
                return WebAuthResult.ServerError($"unexpected status: {(int)resp.StatusCode}");
            }

            LoginResponseDto? dto;
            try
            {
                dto = await resp.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return WebAuthResult.ServerError($"response parse failed: {ex.Message}");
            }

            if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
            {
                return WebAuthResult.ServerError("Web returned empty token");
            }

            return WebAuthResult.Ok(dto.Token, dto.Username, dto.DisplayName, dto.Role);
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ─── DTOs ─────────────────────────────────────────────────
        // BODA.VMS.Web.Client.Models.LoginRequest / LoginResponse 와 동일 schema.
        // 별도 정의 — VMS.Core 가 Web Client 어셈블리에 의존하지 않도록 격리.

        private sealed class LoginRequestDto
        {
            [JsonPropertyName("username")]
            public string Username { get; set; } = string.Empty;
            [JsonPropertyName("password")]
            public string Password { get; set; } = string.Empty;
        }

        private sealed class LoginResponseDto
        {
            [JsonPropertyName("token")]
            public string Token { get; set; } = string.Empty;
            [JsonPropertyName("username")]
            public string Username { get; set; } = string.Empty;
            [JsonPropertyName("displayName")]
            public string DisplayName { get; set; } = string.Empty;
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;
        }
    }

    /// <summary>WebAuthClient.LoginAsync 결과 분류.</summary>
    public enum WebAuthResultKind
    {
        /// <summary>JWT 발급 성공.</summary>
        Success,
        /// <summary>Web 이 401 반환 또는 입력값 자체가 비어 있음 — 사용자에게 표시.</summary>
        InvalidCredentials,
        /// <summary>네트워크 / 5xx / timeout — 호출자가 비상 폴백 판단.</summary>
        WebUnreachable,
        /// <summary>Web 이 예상치 못한 응답 형식 — 운영 가이드 메시지 표시.</summary>
        ServerError
    }

    /// <summary>WebAuthClient.LoginAsync 결과.</summary>
    public sealed class WebAuthResult
    {
        public WebAuthResultKind Kind { get; init; }
        public string? Token { get; init; }
        public string? Username { get; init; }
        public string? DisplayName { get; init; }
        public string? Role { get; init; }
        public string? ErrorDetail { get; init; }

        public bool IsSuccess => Kind == WebAuthResultKind.Success;

        public static WebAuthResult Ok(string token, string username, string displayName, string role)
            => new()
            {
                Kind = WebAuthResultKind.Success,
                Token = token,
                Username = username,
                DisplayName = displayName,
                Role = role
            };

        public static WebAuthResult Invalid(string detail)
            => new() { Kind = WebAuthResultKind.InvalidCredentials, ErrorDetail = detail };

        public static WebAuthResult Unreachable(string detail)
            => new() { Kind = WebAuthResultKind.WebUnreachable, ErrorDetail = detail };

        public static WebAuthResult ServerError(string detail)
            => new() { Kind = WebAuthResultKind.ServerError, ErrorDetail = detail };
    }
}
