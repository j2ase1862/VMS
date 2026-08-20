using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>
    /// Web 의 /api/lots/active-by-workorder/{woId} 익명 endpoint 호출.
    /// VMS Launcher 의 MainViewModel.OnSelectedWorkOrderChanged 가 사용 — WO 선택 시
    /// 활성 Lot 을 자동으로 LotIdText 에 채움.
    /// </summary>
    public class LotClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _webServerUrl;
        private bool _disposed;

        public LotClient(string webServerUrl)
        {
            _webServerUrl = webServerUrl.TrimEnd('/');
            InsecureUrlGuard.Check(_webServerUrl, nameof(LotClient));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(8));
        }

        /// <summary>WO 의 활성(Open) Lot 1개. 없으면 null. 통신 실패 시도 null.</summary>
        public async Task<LotDto?> GetActiveByWorkOrderAsync(int workOrderId)
        {
            try
            {
                var url = $"{_webServerUrl}/api/lots/active-by-workorder/{workOrderId}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body) || body == "null") return null;
                var dto = JsonSerializer.Deserialize<LotDto>(body, JsonOptions);
                // Phase 3c — Lot 자동 채움 DTO sanitize.
                return dto?.Sanitize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LotClient] GetActiveByWorkOrderAsync failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>WO 의 Open Lot 전체 목록 (발행 순서). 통신 실패 시 null — 호출 측이 기존 목록 유지 판단.</summary>
        public async Task<System.Collections.Generic.List<LotDto>?> GetOpenByWorkOrderAsync(int workOrderId)
        {
            try
            {
                var url = $"{_webServerUrl}/api/lots/open-by-workorder/{workOrderId}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body)) return null;
                var list = JsonSerializer.Deserialize<System.Collections.Generic.List<LotDto>>(body, JsonOptions);
                if (list == null) return null;
                foreach (var lot in list) lot.Sanitize();
                return list;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LotClient] GetOpenByWorkOrderAsync failed: {ex.Message}");
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
