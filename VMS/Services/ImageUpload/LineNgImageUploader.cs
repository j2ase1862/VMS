using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VMS.Core.Imaging;
using VMS.Core.Security;

namespace VMS.Services.ImageUpload
{
    public interface ILineNgImageUploader : IDisposable
    {
        /// <summary>판정 이미지를 (토글·샘플링에 따라) MLOps 전송 큐에 적재. 비대상이면 no-op.</summary>
        void Enqueue(BitmapSource? image, InspectionImageContext? context, ImageSaveOptions? options);

        /// <summary>백그라운드 드레인 시작.</summary>
        void Start();
    }

    /// <summary>
    /// MLOps 큐 사이드카(.json). 전송 필드와 재시도 상태를 함께 담는다.
    /// </summary>
    public sealed class LineNgUploadMeta
    {
        /// <summary>Web 생산 이력과 이어 붙이는 열쇠 — 결과 업로드와 같은 상관 키. 없으면 null.</summary>
        public string? InspectionId { get; set; }
        public string Verdict { get; set; } = "NG";      // OK | NG
        public string Ext { get; set; } = "png";
        public string CapturedAtUtc { get; set; } = string.Empty;  // ISO 8601 UTC
        public string[] Tags { get; set; } = Array.Empty<string>();

        // 큐 내부 운영 필드(서버 전송 안 함) — 재시도 백오프.
        public int Attempts { get; set; }
        public string? NextAttemptAtUtc { get; set; }
    }

    /// <summary>
    /// 양품 N 장에 1 장을 고르는 계수기. 첫 장을 바로 보내고 그 뒤로 N 장마다 한 장이라,
    /// 라인이 새로 켜져도 정상 샘플이 곧바로 한 장은 올라간다.
    /// </summary>
    public sealed class OkSampler
    {
        private long _seen;

        /// <summary><paramref name="rate"/> 가 0 이하이면 세지도 않고 false.</summary>
        public bool Take(int rate)
        {
            if (rate <= 0) return false;
            var n = Interlocked.Increment(ref _seen);
            return (n - 1) % rate == 0;
        }
    }

    /// <summary>
    /// 검사 이미지를 BODA.VMS.MLOps 데이터 풀로 보낸다 (<c>POST /api/images/line-ng</c>, 라인 토큰).
    /// Web 업로드(<see cref="ImageUploadService"/>)와 같은 뼈대 — Enqueue 는 frozen 클론 + 백그라운드
    /// 인코딩·큐 기록만 하고, 전송은 드레인 타이머가 한다. 로컬 디스크 큐(재시작 내구) + 상한/드롭 +
    /// 지수 백오프. 다른 점은 셋이다:
    /// <list type="bullet">
    /// <item>원본 해상도로 보낸다 — 학습 데이터라 썸네일은 쓸모가 없다. 포맷은 로컬 저장 포맷을 따르되
    /// 서버가 받지 않는 TIFF 만 PNG 로 바꾼다.</item>
    /// <item>양품은 N 장에 1 장만 보낸다 (<see cref="ImageSaveOptions.MlopsOkSampleRate"/>).</item>
    /// <item>서버가 규칙 위반으로 거절한 것(4xx, 단 401·403·408·429 제외)은 다시 보내지 않고
    /// <c>rejected/</c> 에 옮긴다 — 큐 맨 앞에 박혀 뒤를 막지 않게 (#444 와 같은 규칙).</item>
    /// </list>
    /// 어느 라인에서 왔는지(lineId)는 보내지 않는다. 라인 토큰이 그 라인에 발급된 것이라 서버가
    /// 토큰에서 읽는다 — 라인 PC 가 다른 라인 이름을 댈 수 없어야 한다.
    /// </summary>
    public sealed class LineNgImageUploader : ILineNgImageUploader
    {
        private const int MaxConcurrentUploads = 1;   // 원본 이미지라 한 번에 한 장 — 라인 네트워크를 독점하지 않는다
        private const int MaxQueueFiles = 2000;
        private const long MaxQueueBytes = 4L * 1024 * 1024 * 1024; // 4 GB
        private const int MaxRejectedFiles = 200;
        private const int DrainIntervalMs = 5000;
        private const int BaseBackoffSec = 5;
        private const int MaxBackoffSec = 300;
        private const string RejectedSubdir = "rejected";
        public const string QueueDirName = "mlops_line_ng_queue";

        private readonly string _baseUrl;
        private readonly string _queueDir;
        private readonly HttpClient _httpClient;
        private readonly OkSampler _okSampler = new();
        private readonly SemaphoreSlim _slots = new(MaxConcurrentUploads, MaxConcurrentUploads);
        private readonly object _capLock = new();
        private Timer? _drainTimer;
        private int _draining;
        private bool _disposed;

        /// <param name="baseUrl">MLOps 서버 주소 (system_config mlopsServerUrl)</param>
        /// <param name="lineToken">라인 토큰 <c>ln_…</c> (system_config mlopsLineToken)</param>
        /// <param name="queueDir">큐 폴더 — 기본 AppData. 테스트에서 바꾼다.</param>
        /// <param name="httpClient">주입용 — 기본은 보안 정책 클라이언트.</param>
        public LineNgImageUploader(string baseUrl, string lineToken, string? queueDir = null, HttpClient? httpClient = null)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            if (_baseUrl.Length == 0) throw new ArgumentException("MLOps 서버 주소가 필요합니다.", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(lineToken)) throw new ArgumentException("라인 토큰이 필요합니다.", nameof(lineToken));
            InsecureUrlGuard.Check(_baseUrl, nameof(LineNgImageUploader));

            _httpClient = httpClient ?? HttpClientPolicy.Build(TimeSpan.FromSeconds(120));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", lineToken.Trim());
            _queueDir = queueDir ?? VMS.Camera.Configuration.AppDataPaths.GetPath(QueueDirName);
        }

        public void Start()
        {
            if (_disposed) return;
            _drainTimer ??= new Timer(_ => _ = DrainAsync(), null, DrainIntervalMs, DrainIntervalMs);
        }

        public void Enqueue(BitmapSource? image, InspectionImageContext? context, ImageSaveOptions? options)
        {
            if (_disposed || image == null || context == null || options == null) return;
            if (!ShouldSend(context.Ok, options, _okSampler)) return;

            BitmapSource frozen = image;
            if (!frozen.IsFrozen)
            {
                frozen = image.Clone();
                frozen.Freeze();
            }

            var ctx = context;
            var opts = options;
            _ = Task.Run(() =>
            {
                try
                {
                    PrepareAndQueue(frozen, ctx, opts);
                    _ = DrainAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LineNgImageUploader] Enqueue 실패: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 보낼지 결정. NG 는 토글이 켜져 있으면 전부, OK 는 토글이 켜져 있고 샘플러가 고른 장만.
        /// 토글이 꺼져 있으면 샘플러를 세지 않는다 — 켜는 순간 첫 양품이 바로 한 장 나가야 한다.
        /// </summary>
        internal static bool ShouldSend(bool ok, ImageSaveOptions opts, OkSampler sampler)
        {
            if (!opts.MlopsSendNg) return false;
            return ok ? sampler.Take(opts.MlopsOkSampleRate) : true;
        }

        /// <summary>
        /// 다시 보내도 같은 답이 올 상태 코드인가. 408·429 는 시간이 지나면 통하고, 401·403 은
        /// 토큰을 다시 발급하면 통하므로 제외한다 — 토큰이 잠깐 흔들린 사이의 NG 사진이 사라지면 안 된다.
        /// </summary>
        internal static bool IsDeterministicReject(HttpStatusCode status)
        {
            int code = (int)status;
            if (code < 400 || code >= 500) return false;
            return code != 401 && code != 403 && code != 408 && code != 429;
        }

        /// <summary>
        /// 서버는 파일 하나가 잘못돼도 200 을 주고 <c>errors</c> 에 사유를 적는다 (라인의 재시도 로직을
        /// 단순하게 두려는 설계). 한 장씩 보내므로 결과가 비고 오류만 있으면 그 장은 거절된 것이다.
        /// </summary>
        internal static bool IsRejectedInBody(string? body, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(body)) return false;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                bool anyResult = root.TryGetProperty("results", out var results)
                                 && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0;
                if (anyResult) return false;
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                    && errors.GetArrayLength() > 0)
                {
                    reason = string.Join("; ", errors.EnumerateArray().Select(e => e.ToString()));
                    return true;
                }
            }
            catch (JsonException) { }
            return false;
        }

        internal static string[] BuildTags(InspectionImageContext ctx)
        {
            var tags = new List<string>(4) { ctx.Ok ? "verdict:ok" : "verdict:ng" };
            if (!string.IsNullOrWhiteSpace(ctx.RecipeName)) tags.Add("recipe:" + ctx.RecipeName.Trim());
            if (!string.IsNullOrWhiteSpace(ctx.CameraName)) tags.Add("camera:" + ctx.CameraName.Trim());
            if (ctx.StepNumber > 0) tags.Add("step:" + ctx.StepNumber);
            return tags.ToArray();
        }

        /// <summary>서버가 받는 확장자(jpg·png·bmp·webp·gif)만 — TIFF 는 PNG 로.</summary>
        internal static ImageSaveFormat FormatForServer(ImageSaveFormat local)
            => local == ImageSaveFormat.Tiff ? ImageSaveFormat.Png : local;

        private void PrepareAndQueue(BitmapSource image, InspectionImageContext ctx, ImageSaveOptions opts)
        {
            Directory.CreateDirectory(_queueDir);

            var format = FormatForServer(opts.Format);
            var (bytes, ext) = WebImageEncoder.EncodeFull(image, format, opts.JpegQuality);
            var stem = BuildStem(ctx);

            lock (_capLock)
            {
                EnforceCap(incomingBytes: bytes.LongLength);

                var dataPath = Path.Combine(_queueDir, stem + ".img");
                var metaPath = Path.Combine(_queueDir, stem + ".json");
                if (File.Exists(metaPath)) return; // 같은 검사의 같은 카메라·스텝 = 이미 큐에 있음(멱등)

                var meta = new LineNgUploadMeta
                {
                    InspectionId = string.IsNullOrWhiteSpace(ctx.CorrelationKey) ? null : ctx.CorrelationKey,
                    Verdict = ctx.Ok ? "OK" : "NG",
                    Ext = ext,
                    CapturedAtUtc = ctx.Timestamp.ToUniversalTime().ToString("o"),
                    Tags = BuildTags(ctx),
                };

                File.WriteAllBytes(dataPath, bytes);
                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta));
            }
        }

        /// <summary>
        /// 큐 파일 이름. 상관 키는 사이클 안의 여러 카메라·스텝이 공유하므로 카메라·스텝을 덧붙여야
        /// 서로 덮어쓰지 않는다. 키가 없으면 시각으로 구분한다.
        /// </summary>
        internal static string BuildStem(InspectionImageContext ctx)
        {
            var head = !string.IsNullOrWhiteSpace(ctx.CorrelationKey)
                ? ctx.CorrelationKey!
                : ctx.Timestamp.ToString("yyyyMMddHHmmssfff");
            return Sanitize($"{head}|{ctx.CameraName}|{ctx.StepNumber}|{(ctx.Ok ? "OK" : "NG")}");
        }

        /// <summary>큐가 상한 초과면 오래된 것부터 폐기 — OK 샘플 먼저, 부족하면 NG 도.</summary>
        private void EnforceCap(long incomingBytes)
        {
            var metas = Directory.Exists(_queueDir)
                ? Directory.GetFiles(_queueDir, "*.json")
                : Array.Empty<string>();

            long totalBytes = metas.Sum(SafePairBytes) + incomingBytes;
            int count = metas.Length + 1;
            if (count <= MaxQueueFiles && totalBytes <= MaxQueueBytes) return;

            var entries = metas
                .Select(m => new { Meta = m, Verdict = ReadMeta(m)?.Verdict ?? "NG", When = File.GetLastWriteTimeUtc(m) })
                .OrderByDescending(e => e.Verdict == "OK" ? 0 : 1)
                .ThenBy(e => e.When)
                .ToList();

            foreach (var e in entries)
            {
                if (count <= MaxQueueFiles && totalBytes <= MaxQueueBytes) break;
                long freed = SafePairBytes(e.Meta);
                DeletePair(e.Meta);
                totalBytes -= freed;
                count--;
                if (e.Verdict != "OK")
                    Debug.WriteLine($"[LineNgImageUploader] 큐 상한 — NG 항목 폐기: {Path.GetFileName(e.Meta)}");
            }
        }

        /// <summary>큐를 한 바퀴 비운다. 테스트와 수동 트리거용 — 타이머 드레인과 같은 경로.</summary>
        internal Task DrainOnceAsync() => DrainAsync();

        /// <summary>대기 중(재시도 포함) 항목 수. 거절 보관함은 세지 않는다.</summary>
        internal int PendingCount
            => Directory.Exists(_queueDir) ? Directory.GetFiles(_queueDir, "*.json").Length : 0;

        internal int RejectedCount
        {
            get
            {
                var dir = Path.Combine(_queueDir, RejectedSubdir);
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
            }
        }

        private async Task DrainAsync()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _draining, 1) == 1) return;
            try
            {
                if (!Directory.Exists(_queueDir)) return;
                var metas = Directory.GetFiles(_queueDir, "*.json");
                var nowUtc = DateTime.UtcNow;

                var tasks = new List<Task>();
                foreach (var metaPath in metas)
                {
                    var meta = ReadMeta(metaPath);
                    if (meta == null) { DeletePair(metaPath); continue; }
                    if (!string.IsNullOrEmpty(meta.NextAttemptAtUtc)
                        && DateTime.TryParse(meta.NextAttemptAtUtc, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var next)
                        && next > nowUtc)
                    {
                        continue;
                    }

                    await _slots.WaitAsync();
                    tasks.Add(Task.Run(async () =>
                    {
                        try { await UploadOneAsync(metaPath, meta); }
                        finally { _slots.Release(); }
                    }));
                }
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LineNgImageUploader] Drain 오류: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _draining, 0);
            }
        }

        private async Task UploadOneAsync(string metaPath, LineNgUploadMeta meta)
        {
            var dataPath = Path.ChangeExtension(metaPath, ".img");
            if (!File.Exists(dataPath)) { DeletePair(metaPath); return; }

            try
            {
                byte[] bytes = File.ReadAllBytes(dataPath);
                using var content = new MultipartFormDataContent();
                var imageContent = new ByteArrayContent(bytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(meta.Ext));
                content.Add(imageContent, "files", $"{Path.GetFileNameWithoutExtension(metaPath)}.{meta.Ext}");
                if (!string.IsNullOrWhiteSpace(meta.InspectionId))
                    content.Add(new StringContent(meta.InspectionId), "inspectionId");
                if (!string.IsNullOrWhiteSpace(meta.CapturedAtUtc))
                    content.Add(new StringContent(meta.CapturedAtUtc), "capturedAt");
                if (meta.Tags.Length > 0)
                    content.Add(new StringContent(string.Join(",", meta.Tags)), "tags");

                var resp = await _httpClient.PostAsync($"{_baseUrl}/api/images/line-ng", content);
                var body = await SafeReadAsync(resp);

                if (resp.IsSuccessStatusCode)
                {
                    if (IsRejectedInBody(body, out var reason))
                        Reject(metaPath, $"서버가 파일을 받지 않음: {reason}");
                    else
                        DeletePair(metaPath); // 중복(같은 해시)도 서버가 200 으로 답한다 — 완료
                }
                else if (IsDeterministicReject(resp.StatusCode))
                {
                    Reject(metaPath, $"HTTP {(int)resp.StatusCode} {Truncate(body, 200)}");
                }
                else
                {
                    Backoff(metaPath, meta, $"HTTP {(int)resp.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Backoff(metaPath, meta, ex.Message);
            }
        }

        private void Backoff(string metaPath, LineNgUploadMeta meta, string reason)
        {
            meta.Attempts++;
            var delay = Math.Min(MaxBackoffSec, BaseBackoffSec * (int)Math.Pow(2, Math.Min(meta.Attempts, 12)));
            meta.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delay).ToString("o");
            try { File.WriteAllText(metaPath, JsonSerializer.Serialize(meta)); } catch { }
            Debug.WriteLine($"[LineNgImageUploader] 재시도 예약(+{delay}s, 시도 {meta.Attempts}): {reason}");
        }

        /// <summary>거절된 쌍을 rejected/ 로 옮긴다. 드레인 대상이 아니다 — 사람이 보고 되살린다. 상한을 넘으면 오래된 것부터 지운다.</summary>
        private void Reject(string metaPath, string reason)
        {
            Debug.WriteLine($"[LineNgImageUploader] CRITICAL: 서버가 이 이미지를 거절했습니다. 다시 보내지 않습니다 — {reason}");
            try
            {
                var dir = Path.Combine(_queueDir, RejectedSubdir);
                Directory.CreateDirectory(dir);
                var dataPath = Path.ChangeExtension(metaPath, ".img");
                var name = Path.GetFileName(metaPath);
                File.Move(metaPath, Path.Combine(dir, name), overwrite: true);
                if (File.Exists(dataPath))
                    File.Move(dataPath, Path.Combine(dir, Path.ChangeExtension(name, ".img")), overwrite: true);
                try { File.WriteAllText(Path.Combine(dir, Path.ChangeExtension(name, ".reason.txt")), reason); } catch { }

                var old = Directory.GetFiles(dir, "*.json").OrderBy(File.GetLastWriteTimeUtc).ToList();
                for (int i = 0; i < old.Count - MaxRejectedFiles; i++)
                {
                    DeletePair(old[i]);
                    try { File.Delete(Path.ChangeExtension(old[i], ".reason.txt")); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LineNgImageUploader] rejected 이동 실패, 폐기: {ex.Message}");
                DeletePair(metaPath);
            }
        }

        // ── helpers ──────────────────────────────────────────────

        private static async Task<string?> SafeReadAsync(HttpResponseMessage resp)
        {
            try { return await resp.Content.ReadAsStringAsync(); }
            catch { return null; }
        }

        private static string Truncate(string? s, int max)
            => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");

        private static string Sanitize(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static string ContentTypeFor(string ext) => ext.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "bmp" => "image/bmp",
            _ => "image/png",
        };

        private static LineNgUploadMeta? ReadMeta(string metaPath)
        {
            try { return JsonSerializer.Deserialize<LineNgUploadMeta>(File.ReadAllText(metaPath)); }
            catch { return null; }
        }

        private static long SafePairBytes(string metaPath)
        {
            long n = 0;
            try { n += new FileInfo(metaPath).Length; } catch { }
            try
            {
                var data = Path.ChangeExtension(metaPath, ".img");
                if (File.Exists(data)) n += new FileInfo(data).Length;
            }
            catch { }
            return n;
        }

        private static void DeletePair(string metaPath)
        {
            try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
            try
            {
                var data = Path.ChangeExtension(metaPath, ".img");
                if (File.Exists(data)) File.Delete(data);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _drainTimer?.Dispose();
            _httpClient.Dispose();
            _slots.Dispose();
        }
    }
}
