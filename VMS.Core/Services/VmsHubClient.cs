using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using VMS.Core.Models.ParameterSync;

namespace VMS.Core.Services
{
    /// <summary>
    /// C5 — Web 의 /hubs/vms-public (익명 SignalR Hub) 클라이언트.
    /// WorkOrderUpdated / WorkOrderCompleted 이벤트를 수신해서 MainViewModel 에 전달.
    ///
    /// 본인이 검사 결과를 업로드한 경우에도 같은 이벤트가 응답으로 + SignalR 로 둘 다 도착할 수 있다.
    /// 핸들러 (MainViewModel) 는 idempotent 라 두 번 처리해도 값이 같으므로 무해. 단, 완료 다이얼로그는
    /// _lastCompletedNotifiedWoId 가드로 중복 차단.
    /// </summary>
    public class VmsHubClient : IAsyncDisposable
    {
        private readonly string _hubUrl;
        private HubConnection? _connection;
        private bool _disposed;

        /// <summary>WO 진행률 변경 — 다른 클라이언트에서 발생한 검사 결과 포함.</summary>
        public event Action<WorkOrderProgressDto>? WorkOrderUpdated;

        /// <summary>WO 가 막 Completed 전이됨.</summary>
        public event Action<WorkOrderProgressDto>? WorkOrderCompleted;

        /// <summary>연결 상태 변경 이벤트 (true=Connected).</summary>
        public event Action<bool>? ConnectionChanged;

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public VmsHubClient(string webServerUrl)
        {
            _hubUrl = $"{webServerUrl.TrimEnd('/')}/hubs/vms-public";
        }

        public async Task StartAsync()
        {
            if (_disposed) return;
            if (_connection != null) return;

            _connection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    // dev 환경 self-signed cert 우회 — 운영에서는 사설 CA 권장
                    options.HttpMessageHandlerFactory = handler =>
                    {
                        if (handler is System.Net.Http.HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback =
                                (_, _, _, _) => true;
                        }
                        return handler;
                    };
                })
                .WithAutomaticReconnect() // 기본 정책: 0s/2s/10s/30s 후 무한 재시도
                .Build();

            _connection.On<JsonElement>("WorkOrderUpdated", elem =>
            {
                var dto = ParseProgress(elem);
                if (dto != null) WorkOrderUpdated?.Invoke(dto);
            });

            _connection.On<JsonElement>("WorkOrderCompleted", elem =>
            {
                var dto = ParseProgress(elem);
                if (dto != null) WorkOrderCompleted?.Invoke(dto);
            });

            _connection.Reconnected += _ =>
            {
                ConnectionChanged?.Invoke(true);
                return Task.CompletedTask;
            };
            _connection.Closed += _ =>
            {
                ConnectionChanged?.Invoke(false);
                return Task.CompletedTask;
            };
            _connection.Reconnecting += _ =>
            {
                ConnectionChanged?.Invoke(false);
                return Task.CompletedTask;
            };

            try
            {
                await _connection.StartAsync();
                ConnectionChanged?.Invoke(true);
                Debug.WriteLine($"[VmsHubClient] Connected → {_hubUrl}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VmsHubClient] StartAsync failed: {ex.Message}");
                // 실패해도 _connection 은 살아있어서 다음 호출에서 재시도 가능. 운영 정책상
                // 단순화를 위해 별도 backoff 는 안 둠 — 사용자 액션 (재로그인 등) 시 재시도.
            }
        }

        private static WorkOrderProgressDto? ParseProgress(JsonElement elem)
        {
            try
            {
                return JsonSerializer.Deserialize<WorkOrderProgressDto>(
                    elem.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VmsHubClient] payload parse error: {ex.Message}");
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (_connection != null)
            {
                try { await _connection.StopAsync(); } catch { }
                try { await _connection.DisposeAsync(); } catch { }
                _connection = null;
            }
        }
    }
}
