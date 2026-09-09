using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>레지스트리의 모델 계열 한 줄 (고르는 화면이 쓴다).</summary>
    public sealed class RegistryModel
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("taskType")] public string TaskType { get; set; } = string.Empty;
        [JsonPropertyName("classes")] public string[] Classes { get; set; } = Array.Empty<string>();
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    /// <summary>모델 한 계열의 버전 한 줄.</summary>
    public sealed class RegistryModelVersion
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("stage")] public string Stage { get; set; } = string.Empty;
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }

    /// <summary>업로드로 새로 생긴 모델 버전.</summary>
    public sealed class UploadedModelVersion
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("modelId")] public Guid ModelId { get; set; }
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("stage")] public string Stage { get; set; } = string.Empty;
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("format")] public string Format { get; set; } = string.Empty;
        [JsonPropertyName("warnings")] public string[] Warnings { get; set; } = Array.Empty<string>();
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
    /// Bearer 토큰 하나로 붙는다. 라인 PC 는 <c>ln_</c> 로 시작하는 서비스 계정 토큰을 쓰고,
    /// 그 토큰이 할 수 있는 일은 참조를 풀고 아티팩트를 받는 것뿐이다.
    /// 학습 도구가 모델을 <b>올릴</b> 때는 사람의 JWT 를 쓴다 (BODA.VMS.Web 로그인으로 받는다) —
    /// 등록·승격은 엔지니어의 일이라 서비스 계정에 열어 두지 않는다.
    /// 권한이 모자라면 서버가 403 으로 답하고 이 클라이언트는 그 이유를 그대로 전한다.
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

        public ModelRegistryClient(string baseUrl, string bearerToken)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            if (_baseUrl.Length == 0) throw new ArgumentException("레지스트리 주소가 필요합니다.", nameof(baseUrl));
            InsecureUrlGuard.Check(_baseUrl, nameof(ModelRegistryClient));

            _http = HttpClientPolicy.Build(TimeSpan.FromMinutes(10), maxResponseBytes: MaxBufferedResponseBytes);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
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

        /// <summary>모델 계열 목록. 참조를 고르는 화면이 쓴다.</summary>
        public async Task<IReadOnlyList<RegistryModel>> ListModelsAsync(string? taskType = null, CancellationToken ct = default)
        {
            var url = $"{_baseUrl}/api/models?archived=false";
            if (!string.IsNullOrWhiteSpace(taskType)) url += "&taskType=" + Uri.EscapeDataString(taskType!);
            return await GetListAsync<RegistryModel>(url, "모델 목록", ct).ConfigureAwait(false);
        }

        /// <summary>한 계열의 버전 목록. 어느 버전을 못 박을지 고를 때 쓴다.</summary>
        public async Task<IReadOnlyList<RegistryModelVersion>> ListVersionsAsync(Guid modelId, CancellationToken ct = default) =>
            await GetListAsync<RegistryModelVersion>(
                $"{_baseUrl}/api/models/{modelId:D}/versions", "버전 목록", ct).ConfigureAwait(false);

        private async Task<IReadOnlyList<T>> GetListAsync<T>(string url, string what, CancellationToken ct)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new ModelRegistryException($"{what}을(를) 받지 못했습니다: {_baseUrl}", ex);
            }

            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new ModelRegistryException("라인 계정 토큰이 없거나 거부되었습니다.");
                if (!response.IsSuccessStatusCode)
                    throw new ModelRegistryException($"{what} 조회 실패 ({(int)response.StatusCode})");

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
            }
        }

        /// <summary>
        /// 모델 계열을 새로 만든다. 작업 유형과 클래스는 뒤에 바꿀 수 없으므로
        /// 부르는 쪽이 학습에 쓴 값을 그대로 넘긴다.
        /// </summary>
        public async Task<RegistryModel> CreateModelAsync(
            string name, string taskType, IReadOnlyList<string> classes, string? description = null,
            CancellationToken ct = default)
        {
            var payload = JsonSerializer.Serialize(new
            {
                name,
                taskType,
                classes = classes.ToArray(),
                description,
            }, JsonOptions);

            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await SendAsync(HttpMethod.Post, _baseUrl + "/api/models", content, "모델 만들기", ct)
                .ConfigureAwait(false);

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RegistryModel>(json, JsonOptions)
                   ?? throw new ModelRegistryException("모델 생성 응답을 읽지 못했습니다.");
        }

        /// <summary>
        /// 학습 결과 ONNX 를 그 계열의 새 버전으로 올린다.
        ///
        /// <para>
        /// 올라간 버전은 Candidate 로 들어간다 — 바로 라인에 나가지 않는다.
        /// 사람이 레지스트리에서 확인하고 Staging·Production 으로 승격해야 라인이 가져간다.
        /// </para>
        /// <para>
        /// 클래스 이름은 학습에 쓴 것을 그대로 넘긴다. 서버가 모델 계열의 클래스와 대조해
        /// 어긋나면 거절한다 — 이름이 밀린 모델이 라인에 나가는 것을 막기 위해서다.
        /// </para>
        /// </summary>
        public async Task<UploadedModelVersion> UploadVersionAsync(
            Guid modelId, string onnxPath, IReadOnlyList<string> classes,
            string? license = null, string? notes = null, CancellationToken ct = default)
        {
            if (!File.Exists(onnxPath))
                throw new ModelRegistryException("올릴 모델 파일이 없습니다: " + onnxPath);

            var meta = JsonSerializer.Serialize(new
            {
                classes = classes.ToArray(),
                license,
                notes,
            }, JsonOptions);

            using var form = new MultipartFormDataContent();
            var stream = File.OpenRead(onnxPath);
            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", Path.GetFileName(onnxPath));
            form.Add(new StringContent(meta, System.Text.Encoding.UTF8, "application/json"), "meta");

            using var response = await SendAsync(HttpMethod.Post,
                _baseUrl + "/api/models/" + modelId.ToString("D") + "/versions",
                form, "모델 올리기", ct).ConfigureAwait(false);

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UploadedModelVersion>(json, JsonOptions)
                   ?? throw new ModelRegistryException("업로드 응답을 읽지 못했습니다.");
        }

        /// <summary>
        /// 보내고, 실패하면 사람이 읽을 수 있는 문장으로 바꾼다.
        /// 서버가 code·message 로 이유를 주므로 그것을 그대로 전한다 —
        /// "400 Bad Request" 만 보여 주면 무엇을 고쳐야 할지 알 수 없다.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string url, HttpContent content, string what, CancellationToken ct)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(method, url) { Content = content };
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new ModelRegistryException(what + " 실패 — 서버에 닿지 못했습니다: " + _baseUrl, ex);
            }

            if (response.IsSuccessStatusCode) return response;

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new ModelRegistryException(
                        what + " 권한이 없습니다. 모델 등록은 엔지니어 이상 계정이어야 합니다. (" + DescribeError(body) + ")");
                throw new ModelRegistryException(
                    what + " 실패 (" + (int)response.StatusCode + "): " + DescribeError(body));
            }
        }

        /// <summary>서버가 준 ApiError 를 한 줄로 편다. JSON 이 아니면 앞부분을 그대로 보여 준다.</summary>
        private static string DescribeError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "서버가 이유를 주지 않았습니다";
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (string.IsNullOrWhiteSpace(message)) return Shorten(body);

                if (root.TryGetProperty("details", out var details)
                    && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
                {
                    var lines = details.EnumerateArray().Select(d => d.GetString()).Where(d => d is not null);
                    return message + " — " + string.Join("; ", lines);
                }
                return message!;
            }
            catch (JsonException)
            {
                return Shorten(body);
            }
        }

        private static string Shorten(string text) =>
            text.Length > 300 ? text.Substring(0, 300) : text;

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
