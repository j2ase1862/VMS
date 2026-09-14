using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Security;

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
        private readonly int? _clientIndex;
        private HubConnection? _connection;
        private bool _disposed;

        /// <summary>WO 진행률 변경 — 다른 클라이언트에서 발생한 검사 결과 포함.</summary>
        public event Action<WorkOrderProgressDto>? WorkOrderUpdated;

        /// <summary>WO 가 막 Completed 전이됨.</summary>
        public event Action<WorkOrderProgressDto>? WorkOrderCompleted;

        /// <summary>
        /// Web 에서 레시피 파라미터가 추가/수정/삭제됨 (recipeId) — 수신 측은 해당
        /// 레시피가 로드돼 있으면 파라미터 캐시를 즉시 재동기화 (60초 폴링은 안전망).
        /// </summary>
        public event Action<int>? RecipeParametersChanged;

        /// <summary>WO 에 새 Lot 발행됨 (workOrderId) — 수신 측은 선택 중인 WO 면 Open Lot 목록 갱신.</summary>
        public event Action<int>? LotIssued;

        /// <summary>Lot 마감됨 (workOrderId) — 수신 측은 목록 갱신 + 선택 중이던 Lot 이면 재선택.</summary>
        public event Action<int>? LotClosed;

        /// <summary>연결 상태 변경 이벤트 (true=Connected).</summary>
        public event Action<bool>? ConnectionChanged;

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        /// <param name="clientIndex">
        /// 이 설비의 라인 번호. 주면 연결·재연결마다 <c>JoinLine</c> 을 불러 <b>자기 라인</b>
        /// 그룹에 든다.
        ///
        /// <para><b>왜.</b> 공개 허브는 익명이라 붙기만 하면 누구나 받는다. 예전 서버는 모든
        /// 이벤트를 전체 발신해서 공장 전체의 작업지시 번호·수량이 아무에게나 흘러갔다.
        /// 서버가 라인 그룹으로 좁힌 뒤에는(BODA.VMS.Web W-009) 이 호출을 해야 자기 라인
        /// 이벤트를 받는다. 구버전 서버에는 이 메서드가 없지만, 실패해도 전체 발신으로
        /// 받으므로 동작에 지장이 없다.</para>
        /// </param>
        public VmsHubClient(string webServerUrl, int? clientIndex = null)
        {
            _hubUrl = $"{webServerUrl.TrimEnd('/')}/hubs/vms-public";
            _clientIndex = clientIndex;
            InsecureUrlGuard.Check(webServerUrl, nameof(VmsHubClient));
        }

        /// <summary>
        /// 자기 라인 그룹에 든다. 그룹 소속은 <b>연결</b>에 붙으므로 재연결마다 다시 불러야 한다.
        /// 구버전 서버(메서드 없음)에서는 조용히 지나간다 — 그쪽은 전체 발신이라 어차피 받는다.
        /// </summary>
        internal async Task JoinLineAsync()
        {
            if (_clientIndex is not int idx || _connection is null) return;
            try
            {
                await _connection.InvokeAsync("JoinLine", idx);
                Debug.WriteLine($"[VmsHubClient] 라인 {idx} 그룹 참가");
            }
            catch (Exception ex)
            {
                // 구버전 서버이거나 일시적 실패 — 전체 발신 호환 모드가 안전망이다.
                Debug.WriteLine($"[VmsHubClient] JoinLine 실패(무시): {ex.Message}");
            }
        }

        public async Task StartAsync()
        {
            if (_disposed) return;
            if (_connection != null) return;

            var allowSelfSigned = SecurityOptions.Current.AllowSelfSignedCert
                               && SecurityOptions.Current.Mode == SecurityMode.Development;

            _connection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    // Development + sentinel: self-signed cert 우회.
                    // Production 모드에서는 OS 기본 검증 사용 — 사설 CA / 정식 인증서 필수.
                    if (allowSelfSigned)
                    {
                        options.HttpMessageHandlerFactory = handler =>
                        {
                            if (handler is System.Net.Http.HttpClientHandler clientHandler)
                            {
                                clientHandler.ServerCertificateCustomValidationCallback =
                                    (request, cert, chain, errors) =>
                                    {
                                        // HttpClientPolicy 와 동일 정책 — localhost / 사설망에서만 우회.
                                        var host = request?.RequestUri?.Host ?? string.Empty;
                                        return IsLocalOrPrivateHost(host)
                                            || errors == System.Net.Security.SslPolicyErrors.None;
                                    };
                            }
                            return handler;
                        };
                    }
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

            _connection.On<JsonElement>("RecipeParametersChanged", elem =>
            {
                var recipeId = TryParseRecipeId(elem);
                if (recipeId is int id) RecipeParametersChanged?.Invoke(id);
            });

            _connection.On<JsonElement>("LotIssued", elem =>
            {
                var woId = TryParseWorkOrderId(elem);
                if (woId is int id) LotIssued?.Invoke(id);
            });

            _connection.On<JsonElement>("LotClosed", elem =>
            {
                var woId = TryParseWorkOrderId(elem);
                if (woId is int id) LotClosed?.Invoke(id);
            });

            _connection.Reconnected += async _ =>
            {
                ConnectionChanged?.Invoke(true);
                await JoinLineAsync();   // 재연결하면 그룹 소속이 사라진다
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
                await JoinLineAsync();
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

        /// <summary>{"workOrderId": N, ...} 페이로드에서 workOrderId 추출 (camelCase/PascalCase 허용).</summary>
        internal static int? TryParseWorkOrderId(JsonElement elem)
        {
            if (elem.ValueKind != JsonValueKind.Object) return null;
            if (elem.TryGetProperty("workOrderId", out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var id)) return id;
            if (elem.TryGetProperty("WorkOrderId", out var v2)
                && v2.ValueKind == JsonValueKind.Number && v2.TryGetInt32(out var id2)) return id2;
            return null;
        }

        /// <summary>{"recipeId": N} 페이로드에서 recipeId 추출 (camelCase/PascalCase 허용).</summary>
        internal static int? TryParseRecipeId(JsonElement elem)
        {
            if (elem.ValueKind != JsonValueKind.Object) return null;
            if (elem.TryGetProperty("recipeId", out var v)
                && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var id)) return id;
            if (elem.TryGetProperty("RecipeId", out var v2)
                && v2.ValueKind == JsonValueKind.Number && v2.TryGetInt32(out var id2)) return id2;
            return null;
        }

        /// <summary>
        /// SignalR cert 우회를 사설망에 제한 — HttpClientPolicy 의 동일 로직 복제.
        /// (VMS.Core 내부 헬퍼라 HttpClientPolicy 의 private 메서드 노출 회피.)
        /// </summary>
        private static bool IsLocalOrPrivateHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
            if (System.Net.IPAddress.TryParse(host, out var ip))
            {
                if (System.Net.IPAddress.IsLoopback(ip)) return true;
                var b = ip.GetAddressBytes();
                if (b.Length == 4)
                {
                    if (b[0] == 10) return true;
                    if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                    if (b[0] == 192 && b[1] == 168) return true;
                }
            }
            return false;
        }

        private static WorkOrderProgressDto? ParseProgress(JsonElement elem)
        {
            try
            {
                var dto = JsonSerializer.Deserialize<WorkOrderProgressDto>(
                    elem.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                // Phase 3b — SignalR push 도 외부 입력 → sanitize.
                return dto?.Sanitize();
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
