using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>
    /// VMS의 작업자 키오스크 인증 클라이언트.
    /// Web의 /api/kiosk/{login,logout,current/{idx}} 익명 endpoint 사용.
    /// 응답은 OperatorSessionDto — 성공 시 OperatorId/Name/EmployeeNumber 반환.
    /// </summary>
    public class OperatorAuthService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _webServerUrl;
        private readonly int _clientIndex;
        private readonly bool _hasApiKey;
        private bool _disposed;

        public OperatorSessionDto? CurrentSession { get; private set; }
        public bool IsLoggedIn => CurrentSession != null && CurrentSession.IsActive;

        public event Action<OperatorSessionDto?>? SessionChanged;

        public OperatorAuthService(string webServerUrl, int clientIndex, string clientApiKey = "")
        {
            _hasApiKey = !string.IsNullOrWhiteSpace(clientApiKey);
            _webServerUrl = webServerUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            InsecureUrlGuard.Check(_webServerUrl, nameof(OperatorAuthService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(5));

            // GS 인증: Web 서버 X-API-Key 인증 (BODA.VMS.Web PR #10). 키가 있으면 모든
            // 요청에 헤더 자동 송신. 빈 키면 서버의 호환 모드(Required=false)에서만 통과.
            if (!string.IsNullOrWhiteSpace(clientApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", clientApiKey);
            }
        }

        /// <summary>EmployeeNumber + PIN으로 Web에 로그인. 성공 시 세션 반환.</summary>
        public async Task<(bool ok, OperatorSessionDto? session, string? error)> LoginAsync(
            string employeeNumber, string pin)
        {
            // 1) 입력 검증 — DoS / control-char 주입 방어. 비정상 입력은 네트워크 호출 없이 즉시 거부.
            try
            {
                CredentialGuard.ValidateIdentifier(employeeNumber, nameof(employeeNumber));
                CredentialGuard.ValidateSecret(pin, nameof(pin));
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"[OperatorAuth] Invalid input: {ex.Message}");
                return (false, null, "입력 형식이 올바르지 않습니다.");
            }

            // 2) PIN 을 담은 request 객체 송신 후 finally 에서 즉시 비움 — 메모리 잔존 시간 최소화.
            var req = new KioskLoginRequest
            {
                ClientIndex = _clientIndex,
                EmployeeNumber = employeeNumber,
                Pin = pin
            };
            try
            {
                var resp = await _httpClient.PostAsJsonAsync($"{_webServerUrl}/api/kiosk/login", req);

                if (resp.IsSuccessStatusCode)
                {
                    var session = await resp.Content.ReadFromJsonAsync<OperatorSessionDto>(JsonOptions);
                    // Phase 3b — Role 화이트리스트 + 문자열 길이 sanitize (권한 결정 직전).
                    session?.Sanitize();
                    CurrentSession = session;
                    SessionChanged?.Invoke(session);
                    Debug.WriteLine($"[OperatorAuth] Login OK: {session?.OperatorName} ({session?.EmployeeNumber})");
                    AuditLogger.Instance.Log(
                        AuditCategory.Authentication, "OperatorLogin", AuditOutcome.Success,
                        userName: session?.EmployeeNumber, source: nameof(OperatorAuthService),
                        details: $"OperatorName={session?.OperatorName}, Role={session?.Role}");
                    return (true, session, null);
                }

                var errorMsg = resp.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => DescribeUnauthorized(resp),
                    System.Net.HttpStatusCode.NotFound => $"ClientIndex {_clientIndex}가 Web에 등록되어 있지 않습니다.",
                    _ => $"로그인 실패: {resp.StatusCode}"
                };
                Debug.WriteLine($"[OperatorAuth] Login failed: {resp.StatusCode}");
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "OperatorLogin",
                    resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? AuditOutcome.Denied : AuditOutcome.Failure,
                    userName: employeeNumber, source: nameof(OperatorAuthService),
                    details: $"HTTP {(int)resp.StatusCode}");
                return (false, null, errorMsg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OperatorAuth] Login error: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "OperatorLogin", AuditOutcome.Failure,
                    userName: employeeNumber, source: nameof(OperatorAuthService),
                    details: $"Exception: {ex.GetType().Name}: {ex.Message}");
                return (false, null, $"통신 오류: {ex.Message}");
            }
            finally
            {
                // string 은 immutable 이라 완전한 zero-clearing 불가하나, 참조 끊기로 GC 후보화.
                // req 는 LoginAsync 끝나면 GC 대상이지만 명시적으로 비워 송신 완료 직후 잔존 시간 단축.
                req.Pin = string.Empty;
            }
        }

        /// <summary>
        /// 401 은 두 가지다 — PIN 이 틀렸거나, 서버의 API 키 게이트에 막혔거나.
        /// 둘 다 본문이 비어 있어 응답만으로는 구분이 안 되므로, 서버가 붙여 주는
        /// <c>WWW-Authenticate: ApiKey</c> 와 이 PC 의 키 설정 유무로 갈라 준다.
        ///
        /// <para>이 구분이 없으면 서버를 <c>ClientApiKey:Required=true</c> 로 바꾼 날
        /// 현장은 올바른 사번·PIN 을 넣고도 "사번 또는 PIN이 올바르지 않습니다" 만 보게 되고,
        /// 원인을 찾을 단서가 화면에도 로그에도 남지 않는다.</para>
        /// </summary>
        private string DescribeUnauthorized(HttpResponseMessage resp)
        {
            var apiKeyGate = resp.Headers.WwwAuthenticate
                .Any(h => h.Scheme.Equals("ApiKey", StringComparison.OrdinalIgnoreCase));
            if (apiKeyGate)
                return "Web 서버가 이 PC 의 API 키를 거부했습니다. 시스템 설정의 [Web API 키]를 확인하세요.";

            return _hasApiKey
                ? "사번 또는 PIN이 올바르지 않습니다."
                : "사번 또는 PIN이 올바르지 않습니다. (이 PC 에 Web API 키가 설정돼 있지 않습니다 — "
                  + "서버가 키를 요구하도록 바뀌었다면 같은 증상이 납니다)";
        }

        /// <summary>
        /// 로그아웃 요청 조립 — <b>내 세션임을 밝힌다</b>.
        ///
        /// <para>예전에는 라인 번호만 담았고, 서버는 그것만 보고 해당 라인의 작업자 세션을
        /// 끝냈다. 라인 번호 0~99 를 훑는 것만으로 전 라인의 작업자를 반복 로그아웃시킬 수
        /// 있었고, 그 뒤 올라오는 검사 이력의 작업자가 비어 추적성이 끊겼다. 서버는 이제
        /// 세션 id·사번을 현재 열린 세션과 대조한다(BODA.VMS.Web W-005).</para>
        /// </summary>
        internal static KioskLogoutRequest BuildLogoutRequest(int clientIndex, OperatorSessionDto? session) =>
            new()
            {
                ClientIndex = clientIndex,
                SessionId = session?.Id,
                EmployeeNumber = session?.EmployeeNumber
            };

        /// <summary>현재 세션 로그아웃.</summary>
        public async Task<bool> LogoutAsync()
        {
            var loggedOutUser = CurrentSession?.EmployeeNumber;
            try
            {
                var req = BuildLogoutRequest(_clientIndex, CurrentSession);
                var resp = await _httpClient.PostAsJsonAsync($"{_webServerUrl}/api/kiosk/logout", req);
                CurrentSession = null;
                SessionChanged?.Invoke(null);
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "OperatorLogout", AuditOutcome.Success,
                    userName: loggedOutUser, source: nameof(OperatorAuthService));
                return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NoContent;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OperatorAuth] Logout error: {ex.Message}");
                // 통신 실패해도 로컬 상태는 비움
                CurrentSession = null;
                SessionChanged?.Invoke(null);
                AuditLogger.Instance.Log(
                    AuditCategory.Authentication, "OperatorLogout", AuditOutcome.Failure,
                    userName: loggedOutUser, source: nameof(OperatorAuthService),
                    details: $"Exception: {ex.GetType().Name}: {ex.Message} (로컬 세션은 비움)");
                return false;
            }
        }

        /// <summary>Web에 저장된 현재 활성 세션이 있는지 확인 (앱 재시작 시 복원용).</summary>
        public async Task<OperatorSessionDto?> FetchCurrentSessionAsync()
        {
            try
            {
                var resp = await _httpClient.GetAsync($"{_webServerUrl}/api/kiosk/current/{_clientIndex}");
                if (!resp.IsSuccessStatusCode) return null;
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

                var session = await resp.Content.ReadFromJsonAsync<OperatorSessionDto>(JsonOptions);
                // Phase 3b — 세션 복원도 외부 입력 → sanitize.
                session?.Sanitize();
                if (session != null && session.IsActive)
                {
                    CurrentSession = session;
                    SessionChanged?.Invoke(session);
                }
                return session;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OperatorAuth] FetchCurrentSession error: {ex.Message}");
                return null;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
