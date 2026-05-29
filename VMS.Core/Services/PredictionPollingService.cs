using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.Predictive;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>
    /// Predictive_DefectRate_Plan §5.3 — Web GET /api/predictions/current/{clientIndex} 폴링.
    /// 작업자 메인 화면 위젯("다음 1시간 예상 NG율: X.X%")에 60초 주기로 결과 push.
    ///
    /// 패턴: ParameterSyncService.StartPeriodicSync 와 동일 — Timer + HttpClient.
    /// 다만 디스크 큐는 없음(GET 이라 잃을 데이터 없음). 실패 시 Status="error" 이벤트로 통지.
    /// </summary>
    public class PredictionPollingService : IPredictionPollingService
    {
        private readonly HttpClient _httpClient;
        private readonly int _clientIndex;
        private readonly string _baseUrl;
        private Timer? _timer;
        private bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public event Action<PredictionCurrentDto>? PredictionUpdated;
        public PredictionCurrentDto? Current { get; private set; }

        public PredictionPollingService(string baseUrl, int clientIndex)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            InsecureUrlGuard.Check(_baseUrl, nameof(PredictionPollingService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(10));
        }

        public void StartPeriodicPolling(int intervalSeconds = 60)
        {
            _timer?.Dispose();
            // 시작 직후 즉시 한 번 호출 + 이후 intervalSeconds 주기
            _timer = new Timer(
                async _ => await PollAsync(),
                null,
                TimeSpan.FromSeconds(2),               // 부팅 직후 첫 호출 약간 지연 (네트워크/Web 부팅 대기)
                TimeSpan.FromSeconds(intervalSeconds));
            Debug.WriteLine($"[Prediction] Polling started ({intervalSeconds}s, client={_clientIndex})");
        }

        public async Task PollAsync()
        {
            if (_disposed) return;
            try
            {
                var url = $"{_baseUrl}/api/predictions/current/{_clientIndex}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Emit(new PredictionCurrentDto
                    {
                        ClientIndex = _clientIndex,
                        Status = "error",
                        Message = $"HTTP {(int)response.StatusCode}",
                        ServerUtc = DateTime.UtcNow,
                    });
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var dto = JsonSerializer.Deserialize<PredictionCurrentDto>(json, JsonOptions);
                if (dto != null)
                {
                    // Phase 3b — 위젯 직접 바인딩 DTO sanitize (PredictedNgRate [0,1] clamp).
                    dto.Sanitize();
                    Emit(dto);
                }
            }
            catch (TaskCanceledException)
            {
                Emit(new PredictionCurrentDto
                {
                    ClientIndex = _clientIndex,
                    Status = "error",
                    Message = "Timeout",
                    ServerUtc = DateTime.UtcNow,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Prediction] Poll error: {ex.Message}");
                Emit(new PredictionCurrentDto
                {
                    ClientIndex = _clientIndex,
                    Status = "error",
                    Message = ex.Message,
                    ServerUtc = DateTime.UtcNow,
                });
            }
        }

        private void Emit(PredictionCurrentDto dto)
        {
            Current = dto;
            try { PredictionUpdated?.Invoke(dto); }
            catch (Exception ex) { Debug.WriteLine($"[Prediction] subscriber threw: {ex.Message}"); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _httpClient.Dispose();
        }
    }
}
