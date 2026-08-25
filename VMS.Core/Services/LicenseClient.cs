using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Security;
using VMS.Core.Security.Licensing;

namespace VMS.Core.Services
{
    /// <summary>
    /// Web 서버 좌석 임대 클라이언트 (spec §5b) — 로컬 license.lic 이 없는 클라이언트 PC 가
    /// 서버 라이선스의 좌석을 주기 갱신으로 임대한다. 응답은 SeatLeaseCache 로 저장되어
    /// 서버 다운 시 유예 운전의 근거가 된다.
    ///
    /// HeartbeatService 와 동일 패턴: Start() 로 백그라운드 루프 시작, 실패는 조용히
    /// 재시도 (좌석 임대 실패가 운전을 방해해서는 안 됨 — 전환기 경고 전용).
    /// </summary>
    public class LicenseClient : IDisposable
    {
        /// <summary>성공 시 다음 갱신까지 간격 — 서버 유예(72h) 대비 충분히 잦게.</summary>
        internal static readonly TimeSpan RenewInterval = TimeSpan.FromHours(8);
        /// <summary>실패 시 재시도 간격.</summary>
        internal static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);

        private readonly HttpClient _httpClient;
        private readonly string _webServerUrl;
        private readonly int _clientIndex;
        private readonly string? _leaseCachePath;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public LicenseClient(string webServerUrl, int clientIndex, string clientApiKey = "",
            string? leaseCachePath = null)
        {
            if (string.IsNullOrWhiteSpace(webServerUrl))
                throw new ArgumentException("webServerUrl 이 비어 있습니다.", nameof(webServerUrl));

            _webServerUrl = webServerUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            _leaseCachePath = leaseCachePath;

            InsecureUrlGuard.Check(_webServerUrl, nameof(LicenseClient));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(10));
            if (!string.IsNullOrWhiteSpace(clientApiKey))
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", clientApiKey);
        }

        // 테스트 전용 — HttpClient(가짜 핸들러)/캐시 경로 주입 (InternalsVisibleTo)
        internal LicenseClient(HttpClient httpClient, string webServerUrl, int clientIndex,
            string? leaseCachePath)
        {
            _httpClient = httpClient;
            _webServerUrl = webServerUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            _leaseCachePath = leaseCachePath;
        }

        /// <summary>즉시 1회 임대 후 주기 갱신 루프 시작.</summary>
        public void Start()
        {
            _ = Task.Run(() => RunAsync(_cts.Token));
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var lease = await LeaseAsync(ct);
                try
                {
                    await Task.Delay(lease != null ? RenewInterval : RetryInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 좌석 임대 1회 시도 — 성공 시 캐시 갱신 후 결과 반환, 실패(미도달/거부)는 null.
        /// </summary>
        public async Task<SeatLeaseCache?> LeaseAsync(CancellationToken ct = default)
        {
            try
            {
                var request = JsonSerializer.Serialize(new
                {
                    clientIndex = _clientIndex,
                    machineFingerprint = MachineFingerprint.GetCode(),
                    hostName = Environment.MachineName,
                });
                var response = await _httpClient.PostAsync(
                    $"{_webServerUrl}/api/license/lease",
                    new StringContent(request, Encoding.UTF8, "application/json"), ct);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[LicenseClient] lease 거부: HTTP {(int)response.StatusCode}");
                    return null;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;

                // granted=false 는 현 단계 서버가 보내지 않지만 (전환기: 만석도 경고+허용),
                // 강제 모드 서버와의 전방 호환으로 캐시를 남기지 않는다.
                if (root.TryGetProperty("granted", out var granted) && granted.ValueKind == JsonValueKind.False)
                {
                    Debug.WriteLine("[LicenseClient] 좌석 거부됨 (granted=false)");
                    return null;
                }

                var lease = new SeatLeaseCache
                {
                    LicenseId = GetString(root, "licenseId"),
                    Customer = GetString(root, "customer"),
                    SeatsUsed = GetInt(root, "seatsUsed"),
                    MaxClients = GetInt(root, "maxClients"),
                    OverCapacity = root.TryGetProperty("overCapacity", out var oc) && oc.ValueKind == JsonValueKind.True,
                    ValidUntilUtc = root.TryGetProperty("validUntilUtc", out var vu) && vu.TryGetDateTime(out var dt)
                        ? dt.ToUniversalTime()
                        : DateTime.UtcNow,
                    LeasedAtUtc = DateTime.UtcNow,
                    ServerUrl = _webServerUrl,
                };
                SeatLeaseCache.Save(lease, _leaseCachePath);
                return lease;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LicenseClient] lease 실패: {ex.GetType().Name} {ex.Message}");
                return null;
            }
        }

        private static string? GetString(JsonElement root, string name) =>
            root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static int GetInt(JsonElement root, string name) =>
            root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _httpClient.Dispose();
            try { _cts.Dispose(); } catch { }
        }
    }
}
