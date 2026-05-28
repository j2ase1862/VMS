using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.Predictive;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>
    /// Predictive_DefectRate_Plan §5.2 — 환경 센서 시계열을 Web 으로 POST.
    /// Timer + IEnvironmentSensorReader. Heartbeat 와 동일한 짧은 주기(5초).
    ///
    /// 동작:
    ///   1. Reader.Read() 호출 (PLC/센서 모듈에서 현재값)
    ///   2. HasAnyReading=false 면 송신 skip — 센서 미연결 환경의 무부하 보장
    ///   3. ClientIndex + Timestamp 채워 POST /api/sensors/readings
    ///   4. 실패 시 silent — 시계열 데이터라 손실 OK, 다음 tick 에 새 데이터
    ///
    /// 디스크 큐 없음 — 5초 후 새 측정값이 들어오므로 큐가 무의미.
    /// </summary>
    public class SensorPollingService : ISensorPollingService
    {
        private readonly HttpClient _httpClient;
        private readonly IEnvironmentSensorReader _reader;
        private readonly int _clientIndex;
        private readonly string _baseUrl;
        private Timer? _timer;
        private bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public SensorPollingService(string baseUrl, int clientIndex, IEnvironmentSensorReader reader)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            _reader = reader;
            InsecureUrlGuard.Check(_baseUrl, nameof(SensorPollingService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(5));
        }

        public void StartPeriodicPolling(int intervalSeconds = 5)
        {
            _timer?.Dispose();
            _timer = new Timer(
                async _ => await PollOnceAsync(),
                null,
                TimeSpan.FromSeconds(intervalSeconds),  // 첫 호출도 한 tick 후 — 부팅 직후 PLC init 시간 확보
                TimeSpan.FromSeconds(intervalSeconds));
            Debug.WriteLine($"[Sensor] Polling started ({intervalSeconds}s, client={_clientIndex})");
        }

        public async Task PollOnceAsync()
        {
            if (_disposed) return;
            try
            {
                var reading = _reader.Read();
                if (!reading.HasAnyReading)
                {
                    // 센서 미연결 — 송신 skip (Web 이 400 으로 reject 하므로 무의미한 호출)
                    return;
                }

                reading.ClientIndex = _clientIndex;
                if (!reading.Timestamp.HasValue)
                    reading.Timestamp = DateTime.UtcNow;

                var url = $"{_baseUrl}/api/sensors/readings";
                var response = await _httpClient.PostAsJsonAsync(url, reading, JsonOptions);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[Sensor] POST failed: {(int)response.StatusCode}");
                }
            }
            catch (TaskCanceledException)
            {
                // Timeout — 다음 tick 재시도, 5초마다 호출이라 손실 무해
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sensor] Poll error: {ex.Message}");
            }
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
