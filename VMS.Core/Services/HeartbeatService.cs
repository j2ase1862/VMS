using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>
    /// Web 서버에 주기적으로 heartbeat를 전송하여 연결 상태를 추적하는 서비스.
    /// BODA.VMS의 HeartbeatService와 동일 패턴.
    /// - 5초 간격 heartbeat → Web의 HeartbeatTimeoutSeconds(15~30초) 이내 2~3회 전송
    /// - 404 (미등록) → VisionServer API를 통해 자동 등록 → 재시도
    /// - Dispose 시 graceful disconnect 전송
    /// </summary>
    public class HeartbeatService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _webServerUrl;
        private readonly string _visionServerUrl;
        private readonly int _clientIndex;
        private readonly string _ipAddress;
        private readonly string _hostName;
        private readonly string _swName;
        private readonly string _heartbeatJson;
        private readonly int _heartbeatIntervalSeconds;
        private readonly CancellationTokenSource _cts = new();
        private Task? _backgroundTask;
        private bool _disposed;
        private bool _clientRegistered;
        private bool _isConnected;
        private int _consecutiveFailures;

        /// <summary>
        /// Web 서버 연결 상태가 변경될 때 발생 (bool isConnected)
        /// </summary>
        public event Action<bool>? ConnectionStatusChanged;

        /// <summary>
        /// 현재 Web 서버 연결 상태
        /// </summary>
        public bool IsConnected => _isConnected;

        public HeartbeatService(
            string webServerUrl,
            string visionServerUrl,
            int clientIndex,
            string ipAddress = "",
            string swName = "VMS",
            string clientApiKey = "")
        {
            _webServerUrl = webServerUrl.TrimEnd('/');
            _visionServerUrl = visionServerUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            _ipAddress = ipAddress;
            _hostName = EscapeJson(Environment.MachineName);
            _swName = EscapeJson(swName);
            _heartbeatIntervalSeconds = 5;

            InsecureUrlGuard.Check(_webServerUrl, nameof(HeartbeatService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(5));

            // GS 인증: Web 서버 X-API-Key 인증 (BODA.VMS.Web PR #10). 키가 있으면 모든
            // 요청에 헤더 자동 송신. 빈 키면 서버의 호환 모드(Required=false)에서만 통과.
            if (!string.IsNullOrWhiteSpace(clientApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", clientApiKey);
            }

            _heartbeatJson = $"{{\"clientIndex\":{_clientIndex},\"hostName\":\"{_hostName}\",\"swName\":\"{_swName}\"}}";
        }

        public void Start()
        {
            _backgroundTask = Task.Run(() => RunAsync(_cts.Token));
        }

        private async Task RunAsync(CancellationToken ct)
        {
            // 시작 시 Client 등록 상태 확인
            await EnsureClientRegisteredAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var content = new StringContent(_heartbeatJson, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(
                        $"{_webServerUrl}/api/clients/heartbeat", content, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        _consecutiveFailures = 0;
                        SetConnected(true);
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Client가 Web에 등록되어 있지 않음 → 재등록 시도
                        _clientRegistered = false;
                        Debug.WriteLine($"[Heartbeat] Client Index={_clientIndex} not found, attempting registration...");
                        await EnsureClientRegisteredAsync(ct);
                        if (!_clientRegistered)
                            SetConnected(false);
                    }
                    else
                    {
                        _consecutiveFailures++;
                        if (_consecutiveFailures >= 3)
                            SetConnected(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= 3)
                        SetConnected(false);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_heartbeatIntervalSeconds), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Web API에 Client가 등록되어 있는지 확인하고, 없으면 VisionServer에 자동 생성.
        /// VisionServer에 등록하면 공유 DB에 즉시 반영되므로, 이후 Web의 heartbeat도 성공.
        /// </summary>
        private async Task EnsureClientRegisteredAsync(CancellationToken ct)
        {
            if (_clientRegistered) return;

            try
            {
                // 1. Heartbeat 보내서 등록 여부 확인
                var checkContent = new StringContent(_heartbeatJson, Encoding.UTF8, "application/json");
                var checkResponse = await _httpClient.PostAsync(
                    $"{_webServerUrl}/api/clients/heartbeat", checkContent, ct);

                if (checkResponse.IsSuccessStatusCode)
                {
                    _clientRegistered = true;
                    SetConnected(true);
                    Debug.WriteLine($"[Heartbeat] Client Index={_clientIndex} verified on Web.");
                    return;
                }

                if (checkResponse.StatusCode != HttpStatusCode.NotFound)
                    return;

                // 2. Client 미등록 → VisionServer에 직접 등록 (1차 시도)
                //    VisionServer가 공유 DB에 INSERT하면 Web에서 자동 인식
                Debug.WriteLine($"[Heartbeat] Registering Client Index={_clientIndex} on VisionServer...");

                var registerJson = $"{{\"Name\":\"{_swName}\",\"IpAddress\":\"{EscapeJson(_ipAddress)}\",\"Index\":{_clientIndex}}}";
                bool visionServerOk = false;
                try
                {
                    var registerResponse = await _httpClient.PostAsync(
                        $"{_visionServerUrl}/api/v1/Clients",
                        new StringContent(registerJson, Encoding.UTF8, "application/json"),
                        ct);
                    visionServerOk = registerResponse.IsSuccessStatusCode;
                    if (!visionServerOk)
                        Debug.WriteLine($"[Heartbeat] VisionServer registration failed: {registerResponse.StatusCode}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Heartbeat] VisionServer unreachable: {ex.Message}");
                }

                // 2-fallback. VisionServer 실패 → Web의 self-register 엔드포인트 직접 호출
                //    (Phase 4: VisionServer 없이도 동작 가능하게 함)
                if (!visionServerOk)
                {
                    Debug.WriteLine($"[Heartbeat] Falling back to Web /api/clients/register...");
                    try
                    {
                        var webRegisterJson = $"{{\"clientIndex\":{_clientIndex},\"name\":\"{_swName}\",\"ipAddress\":\"{EscapeJson(_ipAddress)}\"}}";
                        var webRegisterResponse = await _httpClient.PostAsync(
                            $"{_webServerUrl}/api/clients/register",
                            new StringContent(webRegisterJson, Encoding.UTF8, "application/json"),
                            ct);
                        if (webRegisterResponse.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[Heartbeat] Web self-register succeeded for Index={_clientIndex}.");
                            visionServerOk = true;  // 이후 heartbeat 재시도 진행
                        }
                        else
                        {
                            Debug.WriteLine($"[Heartbeat] Web self-register failed: {webRegisterResponse.StatusCode}");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Heartbeat] Web self-register error: {ex.Message}");
                    }
                }

                if (visionServerOk)
                {
                    // 3. 등록 직후 heartbeat 재시도 (Web의 DB 동기화 대기)
                    for (int retry = 0; retry < 3; retry++)
                    {
                        await Task.Delay(1000, ct);
                        var retryContent = new StringContent(_heartbeatJson, Encoding.UTF8, "application/json");
                        var retryResponse = await _httpClient.PostAsync(
                            $"{_webServerUrl}/api/clients/heartbeat", retryContent, ct);

                        if (retryResponse.IsSuccessStatusCode)
                        {
                            _clientRegistered = true;
                            SetConnected(true);
                            Debug.WriteLine($"[Heartbeat] First heartbeat after registration succeeded (attempt {retry + 1}).");
                            return;
                        }

                        Debug.WriteLine($"[Heartbeat] Post-registration heartbeat attempt {retry + 1} returned {retryResponse.StatusCode}");
                    }

                    Debug.WriteLine($"[Heartbeat] Post-registration heartbeat failed after 3 attempts.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Heartbeat] Client registration failed: {ex.Message}");
            }
        }

        private void SetConnected(bool connected)
        {
            if (_disposed) return;

            if (_isConnected != connected)
            {
                _isConnected = connected;
                Debug.WriteLine($"[Heartbeat] Connection status changed: {(connected ? "Connected" : "Disconnected")}");
                ConnectionStatusChanged?.Invoke(connected);
            }
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 이벤트 구독 해제 — Dispose 이후 Dispatcher.Invoke 데드락 방지
            ConnectionStatusChanged = null;

            _cts.Cancel();

            // Graceful disconnect를 별도 HttpClient로 fire-and-forget 전송.
            // _httpClient는 Cancel된 CancellationToken에 묶여 있을 수 있으므로
            // 새 클라이언트를 사용하고 UI 스레드를 블로킹하지 않도록 한다.
            _ = Task.Run(() =>
            {
                try
                {
                    using var client = HttpClientPolicy.Build(TimeSpan.FromSeconds(2));
                    var json = $"{{\"clientIndex\":{_clientIndex}}}";
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    client.PostAsync($"{_webServerUrl}/api/clients/disconnect", content)
                        .Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Server unreachable — ignore
                }
            });

            // 백그라운드 태스크 종료를 기다리지 않음 — _cts.Cancel()이 루프를 종료시킴
            _httpClient.Dispose();

            try { _cts.Dispose(); } catch { }
        }
    }
}
