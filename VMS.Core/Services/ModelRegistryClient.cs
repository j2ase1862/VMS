using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.DeepLearning;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>레지스트리가 참조를 풀어 알려 주는 것.</summary>
    public sealed class ResolvedModel
    {
        [JsonPropertyName("modelId")] public Guid ModelId { get; set; }
        [JsonPropertyName("modelVersionId")] public Guid ModelVersionId { get; set; }
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("stage")] public string Stage { get; set; } = string.Empty;
        [JsonPropertyName("artifactUrl")] public string ArtifactUrl { get; set; } = string.Empty;
    }

    /// <summary>레지스트리에 닿지 못했거나 참조를 풀 수 없을 때.</summary>
    public sealed class ModelRegistryException : Exception
    {
        public ModelRegistryException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// MLOps 모델 레지스트리 클라이언트 (개발 문서 §5.1 배포).
    ///
    /// <para>
    /// 라인 PC 는 <c>ln_</c> 로 시작하는 서비스 계정 토큰으로 붙는다. 그 토큰이 할 수 있는 일은
    /// 참조를 푸는 것과 아티팩트를 받는 것뿐이라, 이 클라이언트가 하는 일도 그 둘이다.
    /// </para>
    /// <para>
    /// 서버 주소가 비어 있으면 단독 모드로 본다. 그때는 참조를 풀지 못하고, 캐시에 이미 있는 모델만 쓴다.
    /// </para>
    /// </summary>
    public sealed class ModelRegistryClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// 버퍼링 응답의 상한. HttpClient 가 int 범위까지만 받아서 2GB 를 넘길 수 없다.
        /// 아티팩트는 어차피 스트리밍으로 받아 캐시에 바로 쓰므로 이 값에 걸리지 않는다 —
        /// 실제 안전장치는 받으면서 계산하는 SHA-256 대조다. 이 값이 막는 것은 JSON 응답이다.
        /// </summary>
        private const long MaxBufferedResponseBytes = int.MaxValue;

        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public ModelRegistryClient(string baseUrl, string lineToken)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            if (_baseUrl.Length == 0) throw new ArgumentException("레지스트리 주소가 필요합니다.", nameof(baseUrl));
            InsecureUrlGuard.Check(_baseUrl, nameof(ModelRegistryClient));

            _http = HttpClientPolicy.Build(TimeSpan.FromMinutes(10), maxResponseBytes: MaxBufferedResponseBytes);
            if (!string.IsNullOrWhiteSpace(lineToken))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", lineToken.Trim());
            }
        }

        /// <summary>참조 → 실제 버전. 어느 파일을 받아야 하는지가 여기서 정해진다.</summary>
        public async Task<ResolvedModel> ResolveAsync(ModelReference reference, CancellationToken ct = default)
        {
            if (reference is null) throw new ArgumentNullException(nameof(reference));

            var query = reference.Kind == ModelReferenceKind.Version
                ? "version=" + reference.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "stage=" + reference.Stage;
            var url = $"{_baseUrl}/api/models/{reference.ModelId:D}/resolve?{query}";

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new ModelRegistryException($"모델 레지스트리에 닿지 못했습니다: {_baseUrl}", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    throw new ModelRegistryException("라인 계정 토큰이 없거나 거부되었습니다. 관리자에게 토큰 재발급을 요청하세요.");
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new ModelRegistryException($"참조를 풀 수 없습니다: {reference}. 그 단계에 승격된 버전이 없을 수 있습니다.");
                if (!response.IsSuccessStatusCode)
                    throw new ModelRegistryException($"모델 참조 해석 실패 ({(int)response.StatusCode}): {reference}");

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var resolved = JsonSerializer.Deserialize<ResolvedModel>(json, JsonOptions);
                if (resolved is null || string.IsNullOrWhiteSpace(resolved.Sha256))
                    throw new ModelRegistryException($"레지스트리 응답을 읽지 못했습니다: {reference}");
                return resolved;
            }
        }

        /// <summary>아티팩트를 캐시에 받아 넣고 로컬 경로를 돌려준다. 해시가 다르면 캐시에 넣지 않는다.</summary>
        public async Task<string> DownloadAsync(ResolvedModel resolved, ModelArtifactCache cache, CancellationToken ct = default)
        {
            if (resolved is null) throw new ArgumentNullException(nameof(resolved));
            if (cache is null) throw new ArgumentNullException(nameof(cache));

            var cached = cache.TryGet(resolved.Sha256);
            if (cached is not null) return cached;

            var url = resolved.ArtifactUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? resolved.ArtifactUrl
                : _baseUrl + "/" + resolved.ArtifactUrl.TrimStart('/');

            var started = Stopwatch.StartNew();
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new ModelRegistryException($"모델 아티팩트를 받지 못했습니다: {url}", ex);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new ModelRegistryException($"모델 아티팩트 내려받기 실패 ({(int)response.StatusCode}): {url}");

                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var path = await cache.PutAsync(resolved.Sha256, stream, ct).ConfigureAwait(false);
                Debug.WriteLine($"[ModelRegistry] {resolved.Sha256[..12]} {resolved.SizeBytes / 1024 / 1024}MB " +
                                $"{started.ElapsedMilliseconds}ms → {path}");
                return path;
            }
        }

        /// <summary>서버가 살아 있고 토큰이 통하는지. 설정 화면의 [연결 확인] 이 쓴다.</summary>
        public async Task<(bool Ok, string Message)> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                using var response = await _http.GetAsync($"{_baseUrl}/api/line-clients/me", ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return (false, "토큰이 거부되었습니다. 관리자에게 재발급을 요청하세요.");
                if (!response.IsSuccessStatusCode)
                    return (false, $"서버가 {(int)response.StatusCode} 로 답했습니다.");

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var lineId = document.RootElement.TryGetProperty("lineId", out var v) ? v.GetString() : null;
                return (true, string.IsNullOrWhiteSpace(lineId) ? "연결됨" : $"연결됨 · 라인 {lineId}");
            }
            catch (Exception ex)
            {
                return (false, $"닿지 못했습니다: {ex.Message}");
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
