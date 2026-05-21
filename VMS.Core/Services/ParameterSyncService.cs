using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.ParameterSync;

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

        public ParameterSyncService(string baseUrl, int clientIndex)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientIndex = clientIndex;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
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

        public async Task<bool> UploadResultsAsync(int recipeId, List<ParameterResultDto> results)
        {
            try
            {
                var request = new ParameterResultUploadRequest
                {
                    ClientIndex = _clientIndex,
                    RecipeId = recipeId,
                    Results = results,
                    WorkOrderId = WorkOrderId,
                    LotId = LotId,
                    OperatorId = OperatorId,
                    SerialNumber = SerialNumber
                };

                var json = JsonSerializer.Serialize(request, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"{_baseUrl}/api/parameters/results";
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[ParameterSync] UploadResults failed: {response.StatusCode}");
                    return false;
                }

                Debug.WriteLine($"[ParameterSync] Uploaded {results.Count} results for recipe {recipeId}");

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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _periodicTimer?.Dispose();
            _httpClient.Dispose();
        }
    }
}
