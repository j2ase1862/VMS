using System;
using System.Diagnostics;
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
        private bool _disposed;

        public OperatorSessionDto? CurrentSession { get; private set; }
        public bool IsLoggedIn => CurrentSession != null && CurrentSession.IsActive;

        public event Action<OperatorSessionDto?>? SessionChanged;

        public OperatorAuthService(string webServerUrl, int clientIndex)
        {
            _webServerUrl = webServerUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            InsecureUrlGuard.Check(_webServerUrl, nameof(OperatorAuthService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(5));
        }

        /// <summary>EmployeeNumber + PIN으로 Web에 로그인. 성공 시 세션 반환.</summary>
        public async Task<(bool ok, OperatorSessionDto? session, string? error)> LoginAsync(
            string employeeNumber, string pin)
        {
            try
            {
                var req = new KioskLoginRequest
                {
                    ClientIndex = _clientIndex,
                    EmployeeNumber = employeeNumber,
                    Pin = pin
                };
                var resp = await _httpClient.PostAsJsonAsync($"{_webServerUrl}/api/kiosk/login", req);

                if (resp.IsSuccessStatusCode)
                {
                    var session = await resp.Content.ReadFromJsonAsync<OperatorSessionDto>(JsonOptions);
                    CurrentSession = session;
                    SessionChanged?.Invoke(session);
                    Debug.WriteLine($"[OperatorAuth] Login OK: {session?.OperatorName} ({session?.EmployeeNumber})");
                    return (true, session, null);
                }

                var errorMsg = resp.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "사번 또는 PIN이 올바르지 않습니다.",
                    System.Net.HttpStatusCode.NotFound => $"ClientIndex {_clientIndex}가 Web에 등록되어 있지 않습니다.",
                    _ => $"로그인 실패: {resp.StatusCode}"
                };
                Debug.WriteLine($"[OperatorAuth] Login failed: {resp.StatusCode}");
                return (false, null, errorMsg);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OperatorAuth] Login error: {ex.Message}");
                return (false, null, $"통신 오류: {ex.Message}");
            }
        }

        /// <summary>현재 세션 로그아웃.</summary>
        public async Task<bool> LogoutAsync()
        {
            try
            {
                var req = new KioskLogoutRequest { ClientIndex = _clientIndex };
                var resp = await _httpClient.PostAsJsonAsync($"{_webServerUrl}/api/kiosk/logout", req);
                CurrentSession = null;
                SessionChanged?.Invoke(null);
                return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NoContent;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OperatorAuth] Logout error: {ex.Message}");
                // 통신 실패해도 로컬 상태는 비움
                CurrentSession = null;
                SessionChanged?.Invoke(null);
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
