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

        public WorkOrderClient(string webServerUrl, int clientIndex)
        {
            _webServerUrl = webServerUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            InsecureUrlGuard.Check(_webServerUrl, nameof(WorkOrderClient));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(8));
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
                return list ?? new List<WorkOrderDto>();
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
