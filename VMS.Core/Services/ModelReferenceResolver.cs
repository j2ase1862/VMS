using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.Camera.Configuration;
using VMS.Core.DeepLearning;

namespace VMS.Core.Services
{
    /// <summary>
    /// 레시피의 <c>model://…</c> 참조를 라인 PC 의 실제 파일 경로로 바꾼다 (개발 문서 §5.1 배포).
    ///
    /// <para>
    /// 두 갈래로 쓴다.
    /// <list type="bullet">
    /// <item><see cref="ToLocalPath"/> — 네트워크를 쓰지 않는다. 이미 받아 둔 것만 돌려준다.
    /// 검사 중에 부르는 쪽(엔진 적재)이 이 길로 온다. 검사 한 장 도는 사이에 HTTP 를 기다릴 수는 없다.</item>
    /// <item><see cref="PrepareAsync"/> — 없으면 받아 온다. 레시피를 열 때·동기화할 때 부른다.</item>
    /// </list>
    /// 그래서 순서가 중요하다. 레시피를 열 때 <see cref="PrepareAsync"/> 가 먼저 돌아 캐시를 채우고,
    /// 검사 때는 캐시에서 꺼내 쓴다. 준비가 안 된 채로 검사에 들어가면 그 도구는 명확한 메시지로 실패한다.
    /// </para>
    /// <para>
    /// 참조 → 해시 대응은 메모리에 기억하고, 캐시 폴더의 작은 색인(<c>references.json</c>)에도 남긴다 (VM-40 후속).
    /// 색인이 없으면 프로세스를 다시 띄운 직후 서버가 꺼져 있을 때, 파일은 캐시에 있는데도 참조를 풀지 못해
    /// 그 도구가 불합격했다. 같은 PC 의 VMS · VisionSetup 은 같은 캐시 폴더 · 색인을 함께 쓴다.
    /// <c>@production</c> 은 승격·롤백으로 가리키는 것이 바뀌므로 색인은 "마지막으로 받은 답" 일 뿐이다 —
    /// 서버에 닿으면 <see cref="PrepareAsync"/> 가 늘 다시 묻고 색인을 갱신한다.
    /// </para>
    /// </summary>
    public sealed class ModelReferenceResolver : IDisposable
    {
        /// <summary>
        /// 프로세스 전역 인스턴스. 검사 엔진 적재는 DI 를 타지 않는 정적 경로(OnnxEngineCache)라
        /// 거기서도 닿을 수 있어야 한다. 서버 설정이 없으면 null 로 두고, 그때는 참조를 못 푼다.
        /// </summary>
        public static ModelReferenceResolver? Current { get; set; }

        private readonly ModelArtifactCache _cache;
        private readonly Func<ModelRegistryClient?> _clientFactory;
        private readonly ConcurrentDictionary<string, string> _referenceToSha = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 참조 → 그 참조가 실제로 가리킨 모델 버전 id.
        ///
        /// <para>
        /// 검사 결과를 운영 웹에 올릴 때 "어느 모델이 이 판정을 했는가" 를 적어야 하고,
        /// MLOps 가 그 값으로 모델별 불량률을 집계한다. 파일 경로로는 이을 수 없다 —
        /// 캐시 파일 이름은 내용 해시라 사람이 못 읽고, 참조 문자열은 단계를 따라가면
        /// 가리키는 대상이 바뀐다. 실제로 푼 버전 id 만이 그 순간의 답이다.
        /// </para>
        /// </summary>
        private readonly ConcurrentDictionary<string, Guid> _referenceToVersionId = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _downloadGate = new(2, 2);
        private ModelRegistryClient? _client;

        /// <summary>캐시 폴더의 참조 색인 파일 이름 — 캐시 정리(Trim)는 *.onnx 만 보므로 지워지지 않는다.</summary>
        public const string IndexFileName = "references.json";

        private readonly object _indexLock = new();
        private volatile bool _indexLoaded;

        // 이 프로세스가 실제로 쓴(준비했거나 꺼내 쓴) 참조 — 캐시 정리 보호 목록은 이것만 본다.
        // 색인의 옛 참조까지 보호하면 색인에 한 번이라도 올라간 파일은 영영 정리되지 않는다.
        private readonly ConcurrentDictionary<string, byte> _activeReferences = new(StringComparer.OrdinalIgnoreCase);

        public ModelReferenceResolver(ModelArtifactCache cache, Func<ModelRegistryClient?> clientFactory)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        /// <summary>기본 배치 — 캐시는 <c>%LocalAppData%\…\models</c>, 서버 정보는 호출자가 준다.</summary>
        public static ModelReferenceResolver CreateDefault(string? registryUrl, string? lineToken)
        {
            var cache = new ModelArtifactCache(AppDataPaths.GetPath("models"));
            return new ModelReferenceResolver(cache, () =>
            {
                if (string.IsNullOrWhiteSpace(registryUrl) || string.IsNullOrWhiteSpace(lineToken)) return null;
                try
                {
                    var client = new ModelRegistryClient(registryUrl!, lineToken!);
                    MlopsClientStatus.ReportEnabled(MlopsClientStatus.ModelRegistry);
                    return client;
                }
                catch (Exception ex)
                {
                    // 같은 InsecureUrlGuard 에 걸리면 model:// 참조가 캐시 전용으로 조용히 내려앉는다 — 사유를 남긴다.
                    Debug.WriteLine($"[ModelReference] 레지스트리 클라이언트를 만들지 못했습니다: {ex.Message}");
                    MlopsClientStatus.ReportDisabled(MlopsClientStatus.ModelRegistry, ex);
                    return null;
                }
            });
        }

        public ModelArtifactCache Cache => _cache;

        /// <summary>레지스트리에 붙을 수 있는 구성인가. false 면 캐시에 있는 것만 쓴다.</summary>
        public bool CanReachRegistry => Client is not null;

        private ModelRegistryClient? Client => _client ??= _clientFactory();

        /// <summary>
        /// 참조든 파일 경로든 받아 "지금 열 수 있는 경로" 를 돌려준다. 네트워크는 쓰지 않는다.
        ///
        /// <para>
        /// 참조가 아니면 그대로 돌려준다 — 기존 레시피의 절대 경로가 그대로 동작해야 한다.
        /// 참조인데 아직 안 받았으면 null 이다. 부르는 쪽이 "준비되지 않았다" 고 말해 줘야 한다.
        /// </para>
        /// </summary>
        public string? ToLocalPath(string? modelPathOrReference)
        {
            if (string.IsNullOrWhiteSpace(modelPathOrReference)) return modelPathOrReference;
            if (!ModelReference.IsReference(modelPathOrReference)) return modelPathOrReference;
            if (!ModelReference.TryParse(modelPathOrReference, out var reference) || reference is null) return null;

            EnsureIndexLoaded();
            if (_referenceToSha.TryGetValue(reference.ToString(), out var sha))
            {
                var cached = _cache.TryGet(sha);
                if (cached is not null)
                {
                    _activeReferences.TryAdd(reference.ToString(), 0);
                    return cached;
                }
                // 캐시에서 사라졌다면 기억도 지운다 — 다음 준비 때 다시 받는다
                _referenceToSha.TryRemove(reference.ToString(), out _);
                _referenceToVersionId.TryRemove(reference.ToString(), out _);
            }
            return null;
        }

        /// <summary>
        /// 이 참조가 실제로 가리킨 모델 버전 id. 아직 풀지 않았으면 null.
        ///
        /// <para>
        /// 참조가 아닌 절대 경로에는 답이 없다 — 그 파일이 레지스트리의 어느 버전인지 알 수 없다.
        /// 그때도 null 이다.
        /// </para>
        /// </summary>
        public Guid? TryGetModelVersionId(string? modelPathOrReference)
        {
            if (string.IsNullOrWhiteSpace(modelPathOrReference)) return null;
            if (!ModelReference.IsReference(modelPathOrReference)) return null;
            if (!ModelReference.TryParse(modelPathOrReference, out var reference) || reference is null) return null;

            EnsureIndexLoaded();
            return _referenceToVersionId.TryGetValue(reference.ToString(), out var id) ? id : null;
        }

        /// <summary>
        /// 참조를 풀어 파일을 받아 둔다. 이미 있으면 네트워크를 쓰지 않는다.
        /// 레지스트리에 닿지 못해도 캐시에 있으면 그 파일로 이어 간다 — 서버가 죽어도 검사는 돌아야 한다.
        /// </summary>
        public async Task<string> PrepareAsync(string modelPathOrReference, CancellationToken ct = default)
        {
            if (!ModelReference.IsReference(modelPathOrReference)) return modelPathOrReference;
            if (!ModelReference.TryParse(modelPathOrReference, out var reference) || reference is null)
                throw new ModelRegistryException($"모델 참조 형식이 아닙니다: {modelPathOrReference}");

            var client = Client;
            if (client is null)
            {
                var known = ToLocalPath(modelPathOrReference);
                if (known is not null) return known;
                throw new ModelRegistryException(
                    "모델 레지스트리 주소나 라인 토큰이 설정되지 않아 참조를 풀 수 없습니다. " +
                    "설정에서 MLOps 서버 주소와 라인 토큰을 지정하세요.");
            }

            await _downloadGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ResolvedModel resolved;
                try
                {
                    resolved = await client.ResolveAsync(reference, ct).ConfigureAwait(false);
                }
                catch (ModelRegistryException) when (ToLocalPath(modelPathOrReference) is not null)
                {
                    // 서버가 잠깐 죽었을 뿐이고 쓸 파일은 이미 있다. 검사를 멈출 이유가 없다.
                    var fallback = ToLocalPath(modelPathOrReference)!;
                    Debug.WriteLine($"[ModelReference] 레지스트리에 닿지 못해 캐시본을 씁니다: {fallback}");
                    return fallback;
                }

                var path = await client.DownloadAsync(resolved, _cache, ct).ConfigureAwait(false);
                _referenceToSha[reference.ToString()] = resolved.Sha256;
                _referenceToVersionId[reference.ToString()] = resolved.ModelVersionId;
                _activeReferences.TryAdd(reference.ToString(), 0);
                SaveIndexEntry(reference.ToString(), resolved.Sha256, resolved.ModelVersionId);
                return path;
            }
            finally { _downloadGate.Release(); }
        }

        /// <summary>
        /// 여러 참조를 한 번에 준비한다. 하나가 실패해도 나머지는 계속 받는다 —
        /// 레시피의 도구 하나 때문에 나머지 도구까지 못 쓰게 만들 이유가 없다.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, string>> PrepareManyAsync(
            IEnumerable<string> references, CancellationToken ct = default)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference) || !ModelReference.IsReference(reference)) continue;
                if (result.ContainsKey(reference)) continue;
                try
                {
                    result[reference] = await PrepareAsync(reference, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ModelReference] 준비 실패 {reference}: {ex.Message}");
                }
            }
            return result;
        }

        /// <summary>지금 캐시가 붙들고 있는 파일들 — 정리할 때 보호 목록으로 쓴다.</summary>
        public IReadOnlyCollection<string> InUsePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in _activeReferences.Keys)
            {
                if (!_referenceToSha.TryGetValue(reference, out var sha)) continue;
                var path = _cache.TryGet(sha);
                if (path is not null) paths.Add(path);
            }
            return paths;
        }

        // ─── 참조 색인 (캐시 폴더의 references.json) ──────────────────

        private sealed class IndexEntry
        {
            [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
            [JsonPropertyName("modelVersionId")] public Guid ModelVersionId { get; set; }
            [JsonPropertyName("resolvedAtUtc")] public DateTime ResolvedAtUtc { get; set; }
        }

        private string IndexPath => Path.Combine(_cache.Root, IndexFileName);

        /// <summary>
        /// 색인을 처음 한 번 메모리로 올린다. 파일이 캐시에 남아 있는 항목만 — 정리로 지워진 파일은 버린다.
        /// 이미 이 프로세스가 푼 참조(메모리)가 있으면 그쪽이 이긴다.
        /// </summary>
        private void EnsureIndexLoaded()
        {
            if (_indexLoaded) return;
            lock (_indexLock)
            {
                if (_indexLoaded) return;
                foreach (var pair in ReadIndex())
                {
                    if (string.IsNullOrWhiteSpace(pair.Value.Sha256)) continue;
                    try { if (_cache.TryGet(pair.Value.Sha256) is null) continue; }
                    catch (ArgumentException) { continue; }   // 망가진 해시
                    _referenceToSha.TryAdd(pair.Key, pair.Value.Sha256);
                    if (pair.Value.ModelVersionId != Guid.Empty)
                        _referenceToVersionId.TryAdd(pair.Key, pair.Value.ModelVersionId);
                }
                _indexLoaded = true;
            }
        }

        private Dictionary<string, IndexEntry> ReadIndex()
        {
            try
            {
                if (!File.Exists(IndexPath)) return new(StringComparer.OrdinalIgnoreCase);
                using var stream = new FileStream(IndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var data = JsonSerializer.Deserialize<Dictionary<string, IndexEntry>>(stream);
                return data is null
                    ? new(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, IndexEntry>(data, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 색인이 깨졌거나 남이 쓰는 중 — 없는 것으로 본다. 다음 준비가 다시 쓴다.
                Debug.WriteLine($"[ModelReference] 참조 색인을 읽지 못했습니다: {ex.Message}");
                return new(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 한 참조의 답을 색인에 더한다 — 읽고 합쳐서 통째로 바꿔 쓴다(다른 프로세스가 쓴 항목을 지우지 않게).
        /// 실패해도 검사에는 지장이 없다(메모리 대응은 이미 있다) — 남기고 넘어간다.
        /// </summary>
        private void SaveIndexEntry(string reference, string sha256, Guid modelVersionId)
        {
            lock (_indexLock)
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    var temp = IndexPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        var entries = ReadIndex();
                        entries[reference] = new IndexEntry
                        {
                            Sha256 = sha256,
                            ModelVersionId = modelVersionId,
                            ResolvedAtUtc = DateTime.UtcNow
                        };

                        // 캐시에서 이미 지워진 파일을 가리키는 항목은 함께 정리한다
                        var pruned = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
                        foreach (var pair in entries)
                        {
                            try { if (_cache.TryGet(pair.Value.Sha256) is not null) pruned[pair.Key] = pair.Value; }
                            catch (ArgumentException) { }
                        }

                        Directory.CreateDirectory(_cache.Root);
                        File.WriteAllText(temp, JsonSerializer.Serialize(pruned, new JsonSerializerOptions { WriteIndented = true }));
                        File.Move(temp, IndexPath, overwrite: true);
                        return;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                        Debug.WriteLine($"[ModelReference] 참조 색인 쓰기 재시도 {attempt + 1}: {ex.Message}");
                        Thread.Sleep(30 * (attempt + 1));
                    }
                }
            }
        }

        /// <summary>참조가 아직 준비되지 않았을 때 사람에게 보여 줄 문장.</summary>
        public static string NotPreparedMessage(string reference) =>
            $"모델 참조 {reference} 를 아직 내려받지 못했습니다. " +
            "레시피를 다시 열거나 MLOps 서버 연결을 확인하세요.";

        public void Dispose()
        {
            _client?.Dispose();
            _downloadGate.Dispose();
        }
    }
}
