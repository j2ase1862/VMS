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
    public interface IImageUploadService : IDisposable
    {
        /// <summary>판정 이미지를 (모드/토글에 따라) 업로드 큐에 적재. 비전송이면 no-op.</summary>
        void Enqueue(BitmapSource? image, InspectionImageContext? context, ImageSaveOptions? options);

        /// <summary>백그라운드 드레인 시작.</summary>
        void Start();
    }

    /// <summary>
    /// 검사 이미지를 BODA.VMS.Web 으로 비동기 업로드. 검사 택트와 완전 분리:
    /// Enqueue 는 frozen 클론 + 백그라운드 인코딩/큐 기록만, 실제 전송은 드레인 타이머가 담당.
    /// 로컬 디스크 큐(재시작 내구) + 상한/드롭 + 동시성 제한 + 지수 백오프.
    /// </summary>
    public sealed class ImageUploadService : IImageUploadService
    {
        private const int MaxConcurrentUploads = 2;
        private const int MaxQueueFiles = 5000;
        private const long MaxQueueBytes = 2L * 1024 * 1024 * 1024; // 2 GB
        private const int DrainIntervalMs = 5000;
        private const int BaseBackoffSec = 5;
        private const int MaxBackoffSec = 300;

        /// <summary>
        /// 한 항목을 언제까지 재시도할지. 이 시간이 지나도 못 보냈으면 큐 앞을 막지 않도록
        /// <see cref="RejectedSubdir"/> 로 옮긴다(지우지는 않는다 — 사람이 볼 수 있게).
        ///
        /// <para>409(결과 레코드 미도착)는 결과 업로드 큐가 풀리면 통하므로 넉넉히 잡는다.
        /// 결과 쪽 유예(30분)보다 길게 둬서, 결과가 늦게 올라온 뒤에도 이미지가 붙을 여지를 준다.</para>
        /// </summary>
        private static readonly TimeSpan RetryGrace = TimeSpan.FromHours(6);

        /// <summary>보낼 수 없다고 판정된 항목을 모아 두는 하위 폴더. 드레인 대상이 아니다.</summary>
        private const string RejectedSubdir = "rejected";

        /// <summary>
        /// 한 번에 보낼 수 있는 최대 바이트. 서버(Kestrel)의 요청 본문 상한 30MB 보다 낮게 잡아
        /// multipart 오버헤드 여유를 둔다. 넘으면 413 이 오고, 그 항목은 몇 번을 다시 보내도
        /// 같은 답이라 큐에 영구히 남는다 — 다MP 산업 카메라의 무압축 BMP 는 쉽게 이 선을 넘는다.
        /// </summary>
        private const long MaxUploadBytes = 28L * 1024 * 1024;

        private readonly string _baseUrl;
        private readonly int _clientIndex;
        private readonly string _queueDir;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _slots = new(MaxConcurrentUploads, MaxConcurrentUploads);
        private readonly object _capLock = new();
        private Timer? _drainTimer;
        private int _draining; // 0/1 — 동시 드레인 가드
        private bool _disposed;

        public ImageUploadService(string baseUrl, int clientIndex, string clientApiKey = "")
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _clientIndex = clientIndex;

            // 이 채널은 검사 이미지 전체와 X-API-Key 를 실어 나른다 — Web 연동 다른 채널
            // (ParameterSync/Heartbeat/LineNg…)과 같은 보안 정책을 태운다. 예전에는 이 하나만
            // 맨 HttpClient 를 써서, Production 모드에서 원격 http:// 가 다른 채널은 전부
            // 차단되는데 이미지 업로드만 조용히 평문으로 나갔다.
            InsecureUrlGuard.Check(_baseUrl, nameof(ImageUploadService));
            _httpClient = HttpClientPolicy.Build(TimeSpan.FromSeconds(60));
            if (!string.IsNullOrWhiteSpace(clientApiKey))
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", clientApiKey);

            _queueDir = VMS.Camera.Configuration.AppDataPaths.GetPath("image_upload_queue");
        }

        public void Start()
        {
            if (_disposed) return;
            _drainTimer ??= new Timer(_ => _ = DrainAsync(), null, DrainIntervalMs, DrainIntervalMs);
        }

        public void Enqueue(BitmapSource? image, InspectionImageContext? context, ImageSaveOptions? options)
        {
            if (_disposed || image == null || context == null || options == null) return;
            if (string.IsNullOrWhiteSpace(_baseUrl)) return;
            if (!ShouldUpload(context.Ok, options)) return;

            // 상관 키 없는 결과(bypass 카메라, grab 실패 NG 등)는 Web 이력과 매칭될 수 없다
            // — 업로드하면 409 재시도만 반복하다 큐 상한으로 폐기되며, 그 압박이 정상 NG
            // 이미지까지 밀어냈다 (2026-08-19 현장). 로컬 저장은 별개 경로라 영향 없음.
            if (string.IsNullOrWhiteSpace(context.CorrelationKey))
            {
                Debug.WriteLine("[ImageUploadService] 상관 키 없음 — 업로드 스킵 (로컬 저장만)");
                return;
            }

            // 크로스 스레드 인코딩 위해 frozen 보장.
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
                    _ = DrainAsync(); // enqueue 직후 즉시 kick
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImageUploadService] Enqueue 실패: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 모드/토글 기준 전송 여부. Auto = 항상 업로드 — 종전에는 Web 이 같은 머신이면
        /// "무전송 + Web 이 로컬 경로 직접 참조" 를 전제로 업로드를 건너뛰었지만, 그 참조
        /// 경로가 Web 쪽에 구현된 적이 없어 NG 이미지가 영원히 비어 있었다 (2026-08-19 현장).
        /// localhost HTTP 업로드 비용은 미미 — 단순성을 택한다. SharedPath 는 여전히 미구현
        /// 상태라 무전송 (선택 시 이미지가 Web 에 안 올라감 — 설정 UI 에서 안내).
        /// </summary>
        internal static bool ShouldUpload(bool ok, ImageSaveOptions opts)
        {
            if (opts.DeliveryMode == ImageDeliveryMode.SharedPath) return false;
            return ok ? opts.WebSendOk : opts.WebSendNg;
        }

        private void PrepareAndQueue(BitmapSource image, InspectionImageContext ctx, ImageSaveOptions opts)
        {
            Directory.CreateDirectory(_queueDir);

            var (bytes, ext) = EncodeWithinLimit(image, opts);
            var key = BuildCorrelationKey(ctx);
            var safe = Sanitize(key);

            lock (_capLock)
            {
                EnforceCap(incomingBytes: bytes.LongLength);

                var dataPath = Path.Combine(_queueDir, safe + ".img");
                var metaPath = Path.Combine(_queueDir, safe + ".json");
                if (File.Exists(metaPath)) return; // 동일 키 = 이미 큐에 있음(멱등)

                var meta = new ImageUploadMeta
                {
                    ClientIndex = _clientIndex,
                    CorrelationKey = key,
                    Verdict = ctx.Ok ? "OK" : "NG",
                    Variant = opts.WebImageVariant == WebImageVariant.Thumbnail ? "thumb" : "full",
                    Ext = ext,
                    CapturedAt = ctx.Timestamp.ToString("o"),
                    CameraName = ctx.CameraName,
                    Step = ctx.StepNumber,
                    RecipeName = ctx.RecipeName,
                    WorkOrder = ctx.WorkOrder,
                    Lot = ctx.Lot,
                    SerialNumber = ctx.Serial,
                };

                File.WriteAllBytes(dataPath, bytes);
                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta));
            }
        }

        /// <summary>큐가 상한 초과면 오래된 것부터 폐기 — OK 우선, 부족하면 NG 도.</summary>
        private void EnforceCap(long incomingBytes)
        {
            var metas = Directory.Exists(_queueDir)
                ? Directory.GetFiles(_queueDir, "*.json")
                : Array.Empty<string>();

            long totalBytes = metas.Sum(SafePairBytes) + incomingBytes;
            int count = metas.Length + 1;
            if (count <= MaxQueueFiles && totalBytes <= MaxQueueBytes) return;

            // (verdict, writeTime, base) — OK를 먼저, 그 안에서 오래된 순.
            var entries = metas
                .Select(m => new { Meta = m, Verdict = ReadVerdict(m), When = File.GetLastWriteTimeUtc(m) })
                .OrderByDescending(e => e.Verdict == "OK" ? 0 : 1)   // OK(0) 먼저 폐기 후보
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
                    Debug.WriteLine($"[ImageUploadService] 큐 상한 — NG 항목 폐기: {Path.GetFileName(e.Meta)}");
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
                        continue; // 백오프 대기 중
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
                Debug.WriteLine($"[ImageUploadService] Drain 오류: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _draining, 0);
            }
        }

        private async Task UploadOneAsync(string metaPath, ImageUploadMeta meta)
        {
            var dataPath = Path.ChangeExtension(metaPath, ".img");
            if (!File.Exists(dataPath)) { DeletePair(metaPath); return; }

            try
            {
                byte[] bytes = File.ReadAllBytes(dataPath);
                using var content = new MultipartFormDataContent();
                var imageContent = new ByteArrayContent(bytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(meta.Ext));
                content.Add(imageContent, "image", $"{Sanitize(meta.CorrelationKey)}.{meta.Ext}");
                content.Add(new StringContent(JsonSerializer.Serialize(meta), Encoding.UTF8, "application/json"), "meta");

                var resp = await _httpClient.PostAsync($"{_baseUrl}/api/inspection-images", content);
                if (resp.IsSuccessStatusCode)
                {
                    DeletePair(metaPath); // 멱등이므로 2xx = 완료
                }
                else if (IsPermanentRejection(resp.StatusCode))
                {
                    // 다시 보내도 같은 답이다 — 큐에 두면 정상 이미지를 밀어낸다.
                    MoveToRejected(metaPath, $"HTTP {(int)resp.StatusCode}");
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

        /// <summary>
        /// 다시 보내도 같은 답이 올 상태 코드인가 — <c>ParameterSyncService.IsPermanentRejection</c>
        /// 과 같은 규칙을 쓴다(두 채널의 판정이 갈리면 현장에서 설명할 수 없다).
        ///
        /// <para>408·429 는 시간이 지나면 통하고, 401·403 은 키/토큰이 고쳐지면 통하며,
        /// 409 는 결과 레코드가 아직 도착하지 않았다는 뜻이라 전부 재시도 대상이다.
        /// 이들도 영원히 붙잡지는 않는다 — <see cref="RetryGrace"/> 가 지나면 rejected/ 로 간다.</para>
        /// </summary>
        internal static bool IsPermanentRejection(HttpStatusCode code) =>
            (int)code >= 400 && (int)code < 500
            && code is not (HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Conflict);

        /// <summary>유예 시간을 넘겼는가 — 촬영 시각 기준(없으면 파일 기록 시각).</summary>
        internal static bool IsPastRetryGrace(string? capturedAtIso, DateTime fallbackUtc, DateTime nowUtc)
        {
            var start = DateTime.TryParse(capturedAtIso, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : fallbackUtc;
            return nowUtc - start > RetryGrace;
        }

        /// <summary>
        /// 보낼 수 없는 항목을 rejected/ 로 옮긴다. 큐에 남겨 두면 상한(5000파일/2GB)을 채워
        /// <see cref="EnforceCap"/> 이 정상 NG 이미지부터 밀어낸다 — 2026-08-19 현장에서
        /// 409 무한 재시도로 실제로 일어난 일이다.
        /// </summary>
        private void MoveToRejected(string metaPath, string reason)
        {
            try
            {
                var dir = Path.Combine(_queueDir, RejectedSubdir);
                Directory.CreateDirectory(dir);
                foreach (var src in new[] { metaPath, Path.ChangeExtension(metaPath, ".img") })
                {
                    if (!File.Exists(src)) continue;
                    var dest = Path.Combine(dir, Path.GetFileName(src));
                    try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                    File.Move(src, dest);
                }
                Debug.WriteLine($"[ImageUploadService] 업로드 포기 — rejected/ 로 이동({reason}): {Path.GetFileName(metaPath)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageUploadService] rejected 이동 실패: {ex.Message}");
                DeletePair(metaPath);   // 옮기지 못하면 큐를 막지 않도록 비운다
            }
        }

        /// <summary>
        /// 서버가 받을 수 있는 크기로 낮춰서 인코딩한다. 원본 포맷이 상한을 넘으면 JPEG →
        /// 그래도 넘으면 썸네일 순으로 내린다. Web 이미지는 "화면에서 확인"이 목적이라
        /// 못 보내는 원본보다 보이는 축소본이 낫다(로컬 저장본은 별개 경로라 영향 없음).
        /// </summary>
        private static (byte[] bytes, string ext) EncodeWithinLimit(BitmapSource image, ImageSaveOptions opts)
        {
            var encoded = WebImageEncoder.Encode(image, opts);
            if (encoded.bytes.LongLength <= MaxUploadBytes) return encoded;

            var jpeg = WebImageEncoder.EncodeFull(image, ImageSaveFormat.Jpeg, opts.JpegQuality);
            if (jpeg.bytes.LongLength <= MaxUploadBytes)
            {
                Debug.WriteLine($"[ImageUploadService] 전송 상한 초과({encoded.bytes.LongLength:N0}B) — JPEG 로 낮춰 전송");
                return jpeg;
            }

            var thumb = WebImageEncoder.EncodeThumbnail(image, opts.ThumbnailMaxEdge);
            Debug.WriteLine($"[ImageUploadService] 전송 상한 초과({jpeg.bytes.LongLength:N0}B) — 썸네일로 낮춰 전송");
            return thumb;
        }

        private void Backoff(string metaPath, ImageUploadMeta meta, string reason)
        {
            if (IsPastRetryGrace(meta.CapturedAt, SafeWriteTimeUtc(metaPath), DateTime.UtcNow))
            {
                MoveToRejected(metaPath, $"{reason} — 유예({RetryGrace.TotalHours:N0}시간) 초과");
                return;
            }

            meta.Attempts++;
            var delay = Math.Min(MaxBackoffSec, BaseBackoffSec * (int)Math.Pow(2, Math.Min(meta.Attempts, 12)));
            meta.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delay).ToString("o");
            try { File.WriteAllText(metaPath, JsonSerializer.Serialize(meta)); } catch { }
            Debug.WriteLine($"[ImageUploadService] 업로드 재시도 예약(+{delay}s, 시도 {meta.Attempts}): {reason}");
        }

        // ── helpers ──────────────────────────────────────────────

        private static string BuildCorrelationKey(InspectionImageContext ctx)
            => !string.IsNullOrWhiteSpace(ctx.CorrelationKey)
                ? ctx.CorrelationKey!  // 결과 업로드와 공유된 키 — Web 매칭 성립
                : $"{ctx.Timestamp:yyyyMMddHHmmssfff}|{ctx.CameraName}|{ctx.StepNumber}|{(ctx.Ok ? "OK" : "NG")}";

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
            "tiff" or "tif" => "image/tiff",
            _ => "image/png",
        };

        private static ImageUploadMeta? ReadMeta(string metaPath)
        {
            try { return JsonSerializer.Deserialize<ImageUploadMeta>(File.ReadAllText(metaPath)); }
            catch { return null; }
        }

        private static string ReadVerdict(string metaPath) => ReadMeta(metaPath)?.Verdict ?? "NG";

        private static DateTime SafeWriteTimeUtc(string metaPath)
        {
            try { return File.GetLastWriteTimeUtc(metaPath); }
            catch { return DateTime.UtcNow; }
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
