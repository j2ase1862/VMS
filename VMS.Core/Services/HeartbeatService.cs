using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        /// <summary>종료 통지처럼 별도 HttpClient 를 쓰는 경로에서도 같은 키를 붙이기 위해 보관한다.</summary>
        private readonly string _clientApiKey;
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
            _hostName = Environment.MachineName;
            _swName = swName;
            _heartbeatIntervalSeconds = 5;

            InsecureUrlGuard.Check(_webServerUrl, nameof(HeartbeatService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(5));
            _clientApiKey = clientApiKey ?? string.Empty;

            // GS 인증: Web 서버 X-API-Key 인증 (BODA.VMS.Web PR #10). 키가 있으면 모든
            // 요청에 헤더 자동 송신. 빈 키면 서버의 호환 모드(Required=false)에서만 통과.
            if (!string.IsNullOrWhiteSpace(clientApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", clientApiKey);
            }

            // 직렬화기에 맡긴다. 예전에는 문자열을 이어 붙이면서 역슬래시와 큰따옴표만
            // 바꿨는데, JSON 문자열 안의 제어문자(줄바꿈·탭 등)는 규격 위반이라 서버가 400 으로
            // 거절한다. swName 의 출처는 AppSetup 에서 사람이 편집하는 값이고, 이 문자열은
            // 생성자에서 한 번 만들어 계속 재사용되므로 — 한 번 잘못 들어가면 5초마다 400 이
            // 영원히 반복되고 화면에는 "Web 연결 끊김" 만 뜬다.
            _heartbeatJson = JsonSerializer.Serialize(new
            {
                clientIndex = _clientIndex,
                hostName = _hostName,
                swName = _swName
            });
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
                        // 4xx 는 "서버가 못 알아들었다" 는 뜻이라 다시 보내도 같은 결과다.
                        // 예전에는 상태 코드조차 남기지 않아, 화면에 "Web 연결 끊김" 만 뜨고
                        // 왜 끊겼는지 알 방법이 없었다. 본문 앞부분까지 남긴다.
                        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                        {
                            var body = string.Empty;
                            try
                            {
                                body = await response.Content.ReadAsStringAsync();
                                if (body.Length > 200) body = body.Substring(0, 200);
                            }
                            catch { /* 본문을 못 읽어도 상태 코드는 남긴다 */ }

                            Debug.WriteLine(
                                $"[Heartbeat] 서버가 요청을 거절했습니다({(int)response.StatusCode}) — {body}");
                        }

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
        /// <summary>응답 본문을 짧게 읽는다 — 실패해도 등록 흐름을 막지 않는다.</summary>
        private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return string.IsNullOrWhiteSpace(body)
                    ? response.StatusCode.ToString()
                    : body.Trim().Substring(0, Math.Min(200, body.Trim().Length));
            }
            catch
            {
                return response.StatusCode.ToString();
            }
        }

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

                var registerJson = JsonSerializer.Serialize(new
                {
                    Name = _swName,
                    IpAddress = _ipAddress,
                    Index = _clientIndex
                });
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
                        var webRegisterJson = JsonSerializer.Serialize(new
                        {
                            clientIndex = _clientIndex,
                            name = _swName,
                            ipAddress = _ipAddress
                        });
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

                            // 400 은 재시도해도 영원히 실패하는 구성 오류다(대표적으로 라인 번호가
                            // 허용 범위 0~99 밖). 그런데 화면에는 "Web 연결 끊김" 만 보이고, 그 사이
                            // 올라가는 검사 결과는 전부 404 를 받아 rejected/ 로 폐기된다.
                            // 디버그 로그는 배포본에서 보이지 않으므로 감사 로그에 남긴다.
                            if (webRegisterResponse.StatusCode == HttpStatusCode.BadRequest)
                            {
                                var reason = await SafeReadAsync(webRegisterResponse, ct);
                                AuditLogger.Instance.Log(
                                    AuditCategory.System, "ClientRegisterRejected", AuditOutcome.Failure,
                                    source: nameof(HeartbeatService),
                                    details: $"라인 번호={_clientIndex}, 서버 응답={reason} — " +
                                             "설정 마법사에서 라인 번호를 확인하세요(허용 0~99). " +
                                             "등록되지 않는 동안 검사 결과는 Web 에 쌓이지 않습니다.");
                            }
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

        /// <summary>
        /// 종료 통지 전용 HttpClient. 정상 경로와 <b>같은 X-API-Key</b> 를 달아 준다.
        ///
        /// <para>예전에는 이 요청만 키가 없어서, 서버가 키 강제(<c>ClientApiKey:Required=true</c>)로
        /// 전환되면 401 로 거부됐다. 그러면 ① 대시보드에 라인이 한동안 '접속 중' 으로 남고
        /// ② 서버가 하는 작업자 세션 자동 종료가 실행되지 않아, 정상 종료했는데도 다음 검사가
        /// 이미 퇴근한 작업자에게 귀속된다.</para>
        ///
        /// <para>Dispose 는 취소된 토큰에 묶인 <c>_httpClient</c> 대신 이 새 클라이언트를 쓴다.</para>
        /// </summary>
        /// <summary>
        /// 5초마다 그대로 다시 보내는 heartbeat 본문 — 생성자에서 한 번 만들어 재사용한다.
        /// 테스트가 "규격에 맞는 JSON 인가" 를 직접 확인할 수 있게 열어 둔다.
        /// </summary>
        internal string HeartbeatPayload => _heartbeatJson;

        internal HttpClient CreateDisconnectClient()
        {
            var client = HttpClientPolicy.Build(TimeSpan.FromSeconds(2));
            if (!string.IsNullOrWhiteSpace(_clientApiKey))
            {
                client.DefaultRequestHeaders.Add("X-API-Key", _clientApiKey);
            }
            return client;
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
                    using var client = CreateDisconnectClient();

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
