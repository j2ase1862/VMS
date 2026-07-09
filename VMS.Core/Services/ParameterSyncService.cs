using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Security;
using VMS.Camera.Configuration;

namespace VMS.Core.Services
{
    /// <summary>
    /// Web API에서 ParamCode→ParamValue를 Dictionary 캐시로 동기화하는 서비스.
    /// BODA.VMS의 InspectionItemSyncService와 동일 패턴.
    /// </summary>
    public class ParameterSyncService : IParameterSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly int _clientIndex;
        private readonly string _baseUrl;

        private readonly Dictionary<int, double> _cache = new();
        private readonly Dictionary<int, RecipeParameterDto> _parameterDetails = new();
        private readonly object _cacheLock = new();

        private Timer? _periodicTimer;
        private Timer? _retryTimer;
        private readonly SemaphoreSlim _retryLock = new(1, 1);
        private readonly string _queueDir;
        private bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public event Action<bool, int>? SyncCompleted;
        public event Action<int, string, int>? RecipeLoaded;
        public event Action<List<RecipeSummaryDto>>? RecipeListChanged;
        public event Action<WorkOrderProgressDto>? WorkOrderProgressed;
        public event Action<WorkOrderProgressDto>? WorkOrderCompleted;

        public DateTime? LastSyncedAt { get; private set; }
        public int CurrentRecipeId { get; private set; }
        public int CachedItemCount
        {
            get { lock (_cacheLock) return _cache.Count; }
        }
        public List<RecipeSummaryDto> Recipes { get; private set; } = new();

        public ParameterSyncService(string baseUrl, int clientIndex, string clientApiKey = "")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            InsecureUrlGuard.Check(_baseUrl, nameof(ParameterSyncService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(10));

            // GS 인증: Web 서버 X-API-Key 인증 (BODA.VMS.Web PR #10).
            if (!string.IsNullOrWhiteSpace(clientApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", clientApiKey);
            }

            // C6: 검사 결과 업로드 실패 시 디스크에 보존 (프로세스 재시작 후에도 복구)
            _queueDir = AppDataPaths.GetPath("upload_queue");
            try { Directory.CreateDirectory(_queueDir); }
            catch (Exception ex) { Debug.WriteLine($"[ParameterSync] queue dir create failed: {ex.Message}"); }

            // 5초마다 큐 드레인 시도 — 시작 직후도 한 번 (이전 세션의 미전송 결과 복구)
            _retryTimer = new Timer(_ => _ = TryDrainQueueAsync(),
                null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        }

        /// <summary>C6: 디스크 큐에 남아있는 미전송 결과 개수.</summary>
        public int PendingUploadCount
        {
            get
            {
                try { return Directory.Exists(_queueDir) ? Directory.GetFiles(_queueDir, "*.json").Length : 0; }
                catch { return 0; }
            }
        }

        public async Task<bool> SyncRecipesAsync()
        {
            try
            {
                var url = $"{_baseUrl}/api/parameters/sync/recipes/{_clientIndex}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ParameterSync] SyncRecipes failed: {response.StatusCode}");
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var recipes = JsonSerializer.Deserialize<List<RecipeSummaryDto>>(json, JsonOptions) ?? new();

                // Phase 3c — 레시피 드롭다운 표시 안정성.
                foreach (var r in recipes) r.Sanitize();

                // 변경 감지: ID 집합 비교
                var oldIds = new HashSet<int>(Recipes.Select(r => r.Id));
                var newIds = new HashSet<int>(recipes.Select(r => r.Id));
                bool changed = !oldIds.SetEquals(newIds);

                Recipes = recipes;
                Debug.WriteLine($"[ParameterSync] Synced {Recipes.Count} recipes");

                if (changed)
                {
                    RecipeListChanged?.Invoke(recipes);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParameterSync] SyncRecipes error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> LoadRecipeAsync(int recipeId)
        {
            try
            {
                var url = $"{_baseUrl}/api/parameters/sync/recipe/{recipeId}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ParameterSync] LoadRecipe {recipeId} failed: {response.StatusCode}");
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var parameters = JsonSerializer.Deserialize<List<RecipeParameterDto>>(json, JsonOptions);

                if (parameters == null)
                    return false;

                // Phase 3c — ParamValue 가 분기 / 임계값으로 흘러가기 전 정상화.
                foreach (var p in parameters) p.Sanitize();

                lock (_cacheLock)
                {
                    _cache.Clear();
                    _parameterDetails.Clear();

                    foreach (var param in parameters.Where(p => p.IsActive))
                    {
                        _cache[param.ParamCode] = param.ParamValue;
                        _parameterDetails[param.ParamCode] = param;
                    }
                }

                CurrentRecipeId = recipeId;
                LastSyncedAt = DateTime.Now;

                var recipeName = Recipes.FirstOrDefault(r => r.Id == recipeId)?.Name ?? $"Recipe#{recipeId}";
                var count = CachedItemCount;

                Debug.WriteLine($"[ParameterSync] Loaded recipe {recipeId} ({recipeName}): {count} parameters");
                RecipeLoaded?.Invoke(recipeId, recipeName, count);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParameterSync] LoadRecipe {recipeId} error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SyncAsync()
        {
            if (CurrentRecipeId <= 0)
                return false;

            var success = await LoadRecipeAsync(CurrentRecipeId);
            SyncCompleted?.Invoke(success, CachedItemCount);
            return success;
        }

        public void StartPeriodicSync(int intervalSeconds = 60)
        {
            _periodicTimer?.Dispose();
            _periodicTimer = new Timer(
                async _ =>
                {
                    // 레시피 목록도 주기적으로 갱신 (Web에서 추가/삭제된 레시피 반영)
                    await SyncRecipesAsync();

                    if (CurrentRecipeId > 0)
                        await SyncAsync();
                },
                null,
                TimeSpan.FromSeconds(intervalSeconds),
                TimeSpan.FromSeconds(intervalSeconds));

            Debug.WriteLine($"[ParameterSync] Periodic sync started ({intervalSeconds}s interval)");
        }

        public double ResolveValue(int paramCode, double defaultValue = 0.0)
        {
            lock (_cacheLock)
            {
                return _cache.TryGetValue(paramCode, out var value) ? value : defaultValue;
            }
        }

        public bool ValidateCode(int paramCode)
        {
            lock (_cacheLock)
            {
                return _cache.ContainsKey(paramCode);
            }
        }

        public (bool exists, bool synced) CheckCodeStatus(int paramCode, double currentSetupValue)
        {
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(paramCode, out var cachedValue))
                    return (false, false);

                return (true, Math.Abs(cachedValue - currentSetupValue) < 1e-9);
            }
        }

        public List<RecipeParameterDto> GetAll()
        {
            lock (_cacheLock)
            {
                return _parameterDetails.Values.ToList();
            }
        }

        // ─── Phase 3 추적성 컨텍스트 ───
        public int? WorkOrderId { get; set; }
        public int? LotId { get; set; }
        public int? OperatorId { get; set; }
        public string? SerialNumber { get; set; }

        public async Task<bool> UploadResultsAsync(
            int recipeId,
            List<ParameterResultDto> results,
            InspectionFeatureMetrics? featureMetrics = null,
            string? correlationKey = null)
        {
            var request = new ParameterResultUploadRequest
            {
                ClientIndex = _clientIndex,
                RecipeId = recipeId,
                Results = results,
                WorkOrderId = WorkOrderId,
                LotId = LotId,
                OperatorId = OperatorId,
                SerialNumber = SerialNumber,
                CorrelationKey = correlationKey
            };

            if (featureMetrics != null)
            {
                request.CycleTimeMs = featureMetrics.CycleTimeMs;
                request.Brightness = featureMetrics.Brightness;
                request.ContrastStd = featureMetrics.ContrastStd;
                request.FocusScore = featureMetrics.FocusScore;
                request.BlobCount = featureMetrics.BlobCount;
                request.MaxBlobAreaPx = featureMetrics.MaxBlobAreaPx;
                request.DlConfidence = featureMetrics.DlConfidence;
                request.DlModelVersion = featureMetrics.DlModelVersion;
            }

            var ok = await TrySendAsync(request);
            if (!ok)
            {
                // C6: 실패한 결과는 디스크에 보존 → retry timer 가 자동 재전송
                EnqueueFailed(request);
            }
            return ok;
        }

        /// <summary>실제 HTTP POST + 응답에서 WO 진행률 이벤트 발생. 성공 = true.</summary>
        private async Task<bool> TrySendAsync(ParameterResultUploadRequest request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"{_baseUrl}/api/parameters/results";
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ParameterSync] UploadResults failed: {response.StatusCode}");
                    return false;
                }

                Debug.WriteLine($"[ParameterSync] Uploaded {request.Results.Count} results for recipe {request.RecipeId}");

                // Stage 3: 응답에서 WO 진행률 추출 → 이벤트 발생
                try
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("workOrder", out var woElem)
                            && woElem.ValueKind == JsonValueKind.Object)
                        {
                            var progress = JsonSerializer.Deserialize<WorkOrderProgressDto>(woElem.GetRawText(), JsonOptions);
                            if (progress != null)
                            {
                                // Phase 3b — 외부 응답 sanitization (clamp 수량 / truncate 문자열).
                                progress.Sanitize();
                                WorkOrderProgressed?.Invoke(progress);
                                if (progress.Completed)
                                    WorkOrderCompleted?.Invoke(progress);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ParameterSync] WO progress parse skip: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ParameterSync] UploadResults error: {ex.Message}");
                return false;
            }
        }

        /// <summary>실패한 요청을 디스크 큐에 보존. 파일명 = timestamp+guid → 정렬 시 시간 순.</summary>
        private void EnqueueFailed(ParameterResultUploadRequest request)
        {
            try
            {
                var filename = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json";
                var path = Path.Combine(_queueDir, filename);
                var json = JsonSerializer.Serialize(request, JsonOptions);
                File.WriteAllText(path, json, Encoding.UTF8);
                Debug.WriteLine($"[ParameterSync] Queued failed upload → {filename} (pending: {PendingUploadCount})");
            }
            catch (Exception ex)
            {
                // 디스크 쓰기 실패 = 데이터 손실. 산업 환경에서는 알림 필요.
                Debug.WriteLine($"[ParameterSync] CRITICAL: queue persist failed: {ex.Message}");
            }
        }

        /// <summary>큐를 시간 순으로 드레인. 첫 실패에서 멈춤 — 순서 보존 + 서버 다운 시 스팸 방지.</summary>
        private async Task TryDrainQueueAsync()
        {
            if (_disposed) return;
            // 동시 드레인 방지 — 5s 주기지만 한 번에 큐가 길면 다음 tick 과 겹칠 수 있음
            if (!await _retryLock.WaitAsync(0)) return;
            try
            {
                if (!Directory.Exists(_queueDir)) return;
                var files = Directory.GetFiles(_queueDir, "*.json")
                                     .OrderBy(f => f, StringComparer.Ordinal)
                                     .ToList();
                if (files.Count == 0) return;

                foreach (var file in files)
                {
                    if (_disposed) break;
                    ParameterResultUploadRequest? request;
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        request = JsonSerializer.Deserialize<ParameterResultUploadRequest>(json, JsonOptions);
                    }
                    catch (Exception ex)
                    {
                        // 손상된 파일 — 무한 재시도 방지로 제거 (마지막 수단)
                        Debug.WriteLine($"[ParameterSync] queue file corrupted, deleting: {Path.GetFileName(file)} — {ex.Message}");
                        try { File.Delete(file); } catch { }
                        continue;
                    }

                    if (request == null)
                    {
                        try { File.Delete(file); } catch { }
                        continue;
                    }

                    var ok = await TrySendAsync(request);
                    if (ok)
                    {
                        try { File.Delete(file); } catch (Exception ex) { Debug.WriteLine($"[ParameterSync] queue file delete failed: {ex.Message}"); }
                    }
                    else
                    {
                        // 서버가 아직 안 살아남 — 멈춤. 다음 tick 에 다시 시도.
                        break;
                    }
                }
            }
            finally
            {
                _retryLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _periodicTimer?.Dispose();
            _retryTimer?.Dispose();
            _retryLock.Dispose();
            _httpClient.Dispose();
        }
    }
}
