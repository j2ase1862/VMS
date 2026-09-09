using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Security;

namespace VMS.Core.Services
{
    /// <summary>웹에서 관리하는 데이터셋 한 줄.</summary>
    public sealed class RegistryDataset
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("taskType")] public string TaskType { get; set; } = string.Empty;
        [JsonPropertyName("classes")] public string[] Classes { get; set; } = Array.Empty<string>();
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("stats")] public RegistryDatasetStats? Stats { get; set; }
    }

    /// <summary>몇 장이 라벨링·검토를 마쳤는지. 스냅샷을 뜨기 전에 사람이 보는 숫자다.</summary>
    public sealed class RegistryDatasetStats
    {
        [JsonPropertyName("imageCount")] public int ImageCount { get; set; }
        [JsonPropertyName("labeled")] public int Labeled { get; set; }
        [JsonPropertyName("reviewed")] public int Reviewed { get; set; }
        [JsonPropertyName("unlabeled")] public int Unlabeled { get; set; }
        [JsonPropertyName("annotationCount")] public int AnnotationCount { get; set; }
    }

    /// <summary>데이터셋의 한 판 — 내보내기 zip 하나로 굳어진 상태.</summary>
    public sealed class RegistryDatasetVersion
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("taskType")] public string TaskType { get; set; } = string.Empty;
        [JsonPropertyName("exportFormat")] public string ExportFormat { get; set; } = string.Empty;
        [JsonPropertyName("manifestHash")] public string ManifestHash { get; set; } = string.Empty;
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("imageCount")] public int ImageCount { get; set; }
        [JsonPropertyName("annotationCount")] public int AnnotationCount { get; set; }
        [JsonPropertyName("classes")] public string[] Classes { get; set; } = Array.Empty<string>();
        [JsonPropertyName("createdBy")] public string CreatedBy { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("datasetId")] public Guid? DatasetId { get; set; }
    }

    /// <summary>
    /// 웹에서 라벨링한 데이터셋을 학습 도구로 가져오는 클라이언트 (개발 문서 §4).
    ///
    /// <para>
    /// 가져오는 것은 <b>내보내기 zip</b> 이다 — 학습 스크립트가 그대로 읽는 형식(yolo·imagefolder·
    /// mvtec·ppocr·coco)으로 서버가 굽는다. 라벨을 학습 도구의 데이터셋 형식으로 되돌리지 않는다.
    /// 두 형식을 오가며 옮겨 적는 길을 열면 어느 쪽이 정본인지 흐려지고, 조용히 어긋난 라벨이
    /// 다음 학습에 들어간다. 웹에서 라벨링한 것은 웹이 정본이고, 학습 도구는 그것으로 학습만 한다.
    /// </para>
    /// <para>
    /// 받는 자격은 사람의 JWT 다 (BODA.VMS.Web 로그인). 스냅샷을 새로 뜨는 것은 엔지니어의 일이다.
    /// </para>
    /// </summary>
    public sealed class DatasetRegistryClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// 서버가 이 판의 <b>매니페스트 해시</b>를 이 헤더로 알려 준다.
        ///
        /// <para>
        /// zip 바이트의 해시가 <b>아니다</b>. 매니페스트(이미지·라벨·분할 목록) JSON 의 해시라
        /// 같은 내용이면 언제 구워도 같은 값이고, zip 자체는 구울 때마다 바이트가 달라진다.
        /// 그래서 이 값으로 받은 바이트를 검증할 수 없다 — 내가 요청한 판이 맞는지 보는 데 쓴다.
        /// </para>
        /// </summary>
        public const string ManifestHashHeader = "X-Content-Sha256";

        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public DatasetRegistryClient(string baseUrl, string bearerToken)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            if (_baseUrl.Length == 0) throw new ArgumentException("서버 주소가 필요합니다.", nameof(baseUrl));
            InsecureUrlGuard.Check(_baseUrl, nameof(DatasetRegistryClient));

            // 수십 GB 짜리 데이터셋도 있다. 받는 동안은 스트림이라 버퍼 상한에 걸리지 않는다.
            _http = HttpClientPolicy.Build(TimeSpan.FromHours(2), maxResponseBytes: int.MaxValue);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
            }
        }

        /// <summary>작업 유형에 맞는 데이터셋 목록. 보관한 것은 빼고 준다.</summary>
        public async Task<IReadOnlyList<RegistryDataset>> ListDatasetsAsync(
            string? taskType = null, CancellationToken ct = default)
        {
            var url = _baseUrl + "/api/datasets?archived=false";
            if (!string.IsNullOrWhiteSpace(taskType)) url += "&taskType=" + Uri.EscapeDataString(taskType!);
            return await GetAsync<List<RegistryDataset>>(url, "데이터셋 목록", ct).ConfigureAwait(false)
                   ?? new List<RegistryDataset>();
        }

        /// <summary>한 데이터셋의 판들. 새것부터 준다.</summary>
        public async Task<IReadOnlyList<RegistryDatasetVersion>> ListVersionsAsync(
            Guid datasetId, CancellationToken ct = default)
            => await GetAsync<List<RegistryDatasetVersion>>(
                   _baseUrl + "/api/dataset-versions?datasetId=" + datasetId.ToString("D"), "버전 목록", ct)
                   .ConfigureAwait(false) ?? new List<RegistryDatasetVersion>();

        /// <summary>
        /// 지금 상태로 새 판을 뜬다.
        ///
        /// <para>
        /// 기본은 라벨이 붙은 것만 담는다. <paramref name="includeUnlabeled"/> 는 검출에서
        /// 배경 샘플이 필요할 때만 켠다 — 분류·이상탐지에서 켜면 라벨 없는 이미지가 학습에 섞인다.
        /// </para>
        /// </summary>
        public async Task<RegistryDatasetVersion> CreateSnapshotAsync(
            Guid datasetId, string? name = null, bool includeUnlabeled = false, bool reviewedOnly = false,
            CancellationToken ct = default)
        {
            var payload = JsonSerializer.Serialize(new { name, includeUnlabeled, reviewedOnly }, JsonOptions);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await SendAsync(HttpMethod.Post,
                _baseUrl + "/api/datasets/" + datasetId.ToString("D") + "/versions",
                content, "스냅샷 만들기", ct).ConfigureAwait(false);

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RegistryDatasetVersion>(json, JsonOptions)
                   ?? throw new ModelRegistryException("스냅샷 응답을 읽지 못했습니다.");
        }

        /// <summary>
        /// 내보내기 zip 을 받아 <paramref name="targetDirectory"/> 에 푼다. 푼 폴더 경로를 돌려준다.
        ///
        /// <para>
        /// 다 받았는지는 Content-Length 와 받은 바이트 수로 본다. 끊긴 연결로 반쯤 받은 zip 을 풀면
        /// 이미지 몇 장이 빠진 채 학습이 돌고, 그건 아무 데도 남지 않는다.
        /// zip 바이트의 해시로는 대조하지 않는다 — 서버가 주는 것은 <see cref="ManifestHashHeader"/>,
        /// 즉 매니페스트의 해시이고 zip 자체는 구울 때마다 바이트가 달라진다.
        /// 그 값은 <b>내가 요청한 판이 맞는지</b> 보는 데 쓴다.
        /// </para>
        /// <para>
        /// 대상 폴더가 이미 있으면 지우고 새로 만든다. 이전 판의 라벨 파일이 남아 섞이면
        /// 지운 라벨이 학습에 되살아난다.
        /// </para>
        /// </summary>
        public async Task<string> DownloadExportAsync(
            RegistryDatasetVersion version, string targetDirectory,
            IProgress<DatasetDownloadProgress>? progress = null, CancellationToken ct = default)
        {
            if (version is null) throw new ArgumentNullException(nameof(version));
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new ArgumentException("풀어 넣을 폴더가 필요합니다.", nameof(targetDirectory));

            var url = _baseUrl + "/api/dataset-versions/" + version.Id.ToString("D") + "/export";
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new ModelRegistryException("데이터셋 받기 실패 — 서버에 닿지 못했습니다: " + _baseUrl, ex);
            }

            string zipPath = Path.Combine(Path.GetTempPath(), "vms-dataset-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                long declared;
                long received;
                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        throw Translate(response.StatusCode, "데이터셋 받기", body);
                    }

                    // 내가 요청한 판이 맞는지 본다. 다른 판이 오면 라벨이 통째로 다른 데이터로 학습이 돈다.
                    var served = response.Headers.TryGetValues(ManifestHashHeader, out var values)
                        ? FirstOf(values) : null;
                    if (!string.IsNullOrWhiteSpace(served) && !string.IsNullOrWhiteSpace(version.ManifestHash)
                        && !string.Equals(served, version.ManifestHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ModelRegistryException(
                            "서버가 다른 판을 보냈습니다. 목록을 새로 받아 다시 고르세요." +
                            $" 요청 {Short(version.ManifestHash)}, 받은 {Short(served!)}");
                    }

                    declared = response.Content.Headers.ContentLength ?? 0;
                    var total = declared > 0 ? declared : version.SizeBytes;

                    await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    received = await CopyAsync(source, zipPath, total, progress, ct).ConfigureAwait(false);
                }

                // 끊긴 연결을 잡는다. 반쯤 받은 zip 을 풀면 이미지 몇 장이 빠진 채 학습이 돌고,
                // 그건 아무 데도 남지 않는다.
                if (declared > 0 && received != declared)
                {
                    throw new ModelRegistryException(
                        $"데이터셋을 끝까지 받지 못했습니다 ({received:N0} / {declared:N0} 바이트). 다시 받아 주세요.");
                }

                progress?.Report(new DatasetDownloadProgress(received, received, "푸는 중…"));
                var folder = Extract(zipPath, targetDirectory);
                progress?.Report(new DatasetDownloadProgress(received, received, "끝"));
                return folder;
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (IOException) { }
            }
        }

        /// <summary>받은 바이트 수를 돌려준다. 그 수가 곧 다 받았는지의 근거다.</summary>
        private static async Task<long> CopyAsync(
            Stream source, string zipPath, long total,
            IProgress<DatasetDownloadProgress>? progress, CancellationToken ct)
        {
            var buffer = new byte[81920];
            long received = 0;
            long lastReported = 0;

            await using (var file = File.Create(zipPath))
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;

                    // 1MB 마다만 알린다. 매 덩어리마다 알리면 화면 갱신이 받기보다 오래 걸린다.
                    if (progress is not null && received - lastReported >= 1024 * 1024)
                    {
                        lastReported = received;
                        progress.Report(new DatasetDownloadProgress(received, total, "받는 중…"));
                    }
                }
            }

            progress?.Report(new DatasetDownloadProgress(received, total == 0 ? received : total, "받는 중…"));
            return received;
        }

        /// <summary>
        /// zip 을 푼다. 대상 폴더는 비우고 시작한다.
        /// 항목 경로가 폴더 밖을 가리키면 거부한다 — zip 은 밖에서 온 파일이다.
        /// </summary>
        private static string Extract(string zipPath, string targetDirectory)
        {
            var full = Path.GetFullPath(targetDirectory);
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            Directory.CreateDirectory(full);

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                var destination = Path.GetFullPath(Path.Combine(full, entry.FullName));
                if (!destination.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(destination, full, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ModelRegistryException("데이터셋 압축에 폴더 밖을 가리키는 항목이 있습니다: " + entry.FullName);
                }

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
            return full;
        }

        private async Task<T?> GetAsync<T>(string url, string what, CancellationToken ct)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new ModelRegistryException(what + "을(를) 받지 못했습니다: " + _baseUrl, ex);
            }

            using (response)
            {
                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw Translate(response.StatusCode, what, json);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
        }

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
                throw Translate(response.StatusCode, what, body);
            }
        }

        /// <summary>서버가 준 이유를 그대로 전한다. "403 Forbidden" 만으로는 무엇을 고쳐야 할지 알 수 없다.</summary>
        private static ModelRegistryException Translate(HttpStatusCode status, string what, string body)
        {
            var reason = DescribeError(body);
            return status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ModelRegistryException(
                    what + " 권한이 없습니다. 웹 계정으로 로그인했는지 확인하세요. (" + reason + ")"),
                HttpStatusCode.NotFound => new ModelRegistryException(
                    what + " — 서버에 없습니다. 다른 사람이 지웠을 수 있습니다. (" + reason + ")"),
                _ => new ModelRegistryException(what + " 실패 (" + (int)status + "): " + reason),
            };
        }

        private static string DescribeError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "서버가 이유를 주지 않았습니다";
            try
            {
                using var document = JsonDocument.Parse(body);
                var message = document.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                return string.IsNullOrWhiteSpace(message) ? Shorten(body) : message!;
            }
            catch (JsonException)
            {
                return Shorten(body);
            }
        }

        private static string Shorten(string text) => text.Length > 300 ? text.Substring(0, 300) : text;

        private static string Short(string hash) => hash.Length > 12 ? hash.Substring(0, 12) : hash;

        private static string? FirstOf(IEnumerable<string> values)
        {
            foreach (var value in values) return value;
            return null;
        }

        public void Dispose() => _http.Dispose();
    }

    /// <summary>받는 중에 화면에 보여 줄 것.</summary>
    public readonly record struct DatasetDownloadProgress(long Received, long Total, string Message)
    {
        /// <summary>0~100. 전체 크기를 모르면 0.</summary>
        public double Percent => Total > 0 ? Math.Clamp(Received * 100.0 / Total, 0, 100) : 0;
    }
}
