using System;
using System.Collections.Generic;
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
    /// Web 의 /api/workorders/by-client/{clientIndex} 익명 endpoint 호출.
    /// VMS Launcher 의 MainViewModel.OpenWorkOrderList 가 사용.
    /// </summary>
    public class WorkOrderClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _webServerUrl;
        private readonly int _clientIndex;
        private bool _disposed;

        public WorkOrderClient(string webServerUrl, int clientIndex, string clientApiKey = "")
        {
            _webServerUrl = webServerUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            InsecureUrlGuard.Check(_webServerUrl, nameof(WorkOrderClient));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(8));

            // GS 인증: Web 서버 X-API-Key 인증 (BODA.VMS.Web PR #10). 키가 있으면 모든
            // 요청에 헤더 자동 송신. 빈 키면 서버의 호환 모드(Required=false)에서만 통과.
            if (!string.IsNullOrWhiteSpace(clientApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", clientApiKey);
            }
        }

        /// <summary>옵션 status — "Planned" / "InProgress" / "Completed" 등. null이면 전체.</summary>
        public async Task<List<WorkOrderDto>> GetByClientAsync(string? status = null)
        {
            try
            {
                var url = $"{_webServerUrl}/api/workorders/by-client/{_clientIndex}";
                if (!string.IsNullOrEmpty(status))
                    url += $"?status={Uri.EscapeDataString(status)}";

                var list = await _httpClient.GetFromJsonAsync<List<WorkOrderDto>>(url, JsonOptions);
                if (list == null) return new List<WorkOrderDto>();
                // Phase 3c — WO 드롭다운 직접 바인딩 DTO sanitize.
                foreach (var wo in list) wo.Sanitize();
                return list;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WorkOrderClient] GetByClientAsync failed: {ex.Message}");
                return new List<WorkOrderDto>();
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
