using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VMS.Core.Imaging;
using VMS.Services.ImageUpload;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 라인 NG 이미지 → MLOps 데이터 풀 송신부 (개발 문서 §5.2 수집, MLOps 수신 <c>POST /api/images/line-ng</c>).
    /// 정책(토글·양품 1/N 샘플·거절 판정)과 큐 한 바퀴(적재 → 전송 → 완료/재시도/거절 보관)를 가짜 HTTP 핸들러로 확인한다.
    /// </summary>
    public class LineNgImageUploaderTests : IDisposable
    {
        private readonly string _queueDir = Path.Combine(Path.GetTempPath(), "vms-lineng-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_queueDir)) Directory.Delete(_queueDir, recursive: true); } catch { }
        }

        // ─── 정책 ───────────────────────────────────────────────

        [Fact]
        public void Ng_is_sent_only_when_toggle_on()
        {
            var sampler = new OkSampler();
            Assert.True(LineNgImageUploader.ShouldSend(ok: false, new ImageSaveOptions { MlopsSendNg = true }, sampler));
            Assert.False(LineNgImageUploader.ShouldSend(ok: false, new ImageSaveOptions { MlopsSendNg = false }, sampler));
        }

        [Fact]
        public void Ok_is_sampled_one_in_n_starting_with_the_first()
        {
            var opts = new ImageSaveOptions { MlopsSendNg = true, MlopsOkSampleRate = 3 };
            var sampler = new OkSampler();
            var picks = Enumerable.Range(1, 7).Select(_ => LineNgImageUploader.ShouldSend(ok: true, opts, sampler)).ToArray();
            Assert.Equal(new[] { true, false, false, true, false, false, true }, picks);
        }

        [Fact]
        public void Ok_rate_zero_never_sends_ok()
        {
            var opts = new ImageSaveOptions { MlopsSendNg = true, MlopsOkSampleRate = 0 };
            var sampler = new OkSampler();
            Assert.All(Enumerable.Range(0, 5), _ => Assert.False(LineNgImageUploader.ShouldSend(ok: true, opts, sampler)));
        }

        [Fact]
        public void Toggle_off_does_not_consume_the_sampler()
        {
            // 꺼진 동안 세지 않아야, 켜는 순간 첫 양품이 바로 한 장 나간다.
            var sampler = new OkSampler();
            var off = new ImageSaveOptions { MlopsSendNg = false, MlopsOkSampleRate = 3 };
            for (int i = 0; i < 5; i++) LineNgImageUploader.ShouldSend(ok: true, off, sampler);
            var on = new ImageSaveOptions { MlopsSendNg = true, MlopsOkSampleRate = 3 };
            Assert.True(LineNgImageUploader.ShouldSend(ok: true, on, sampler));
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, true)]
        [InlineData(HttpStatusCode.NotFound, true)]
        [InlineData(HttpStatusCode.RequestEntityTooLarge, true)]
        [InlineData(HttpStatusCode.UnsupportedMediaType, true)]
        [InlineData(HttpStatusCode.Unauthorized, false)]
        [InlineData(HttpStatusCode.Forbidden, false)]
        [InlineData(HttpStatusCode.RequestTimeout, false)]
        [InlineData(HttpStatusCode.TooManyRequests, false)]
        [InlineData(HttpStatusCode.InternalServerError, false)]
        [InlineData(HttpStatusCode.ServiceUnavailable, false)]
        public void Deterministic_reject_excludes_auth_and_transient_4xx(HttpStatusCode status, bool expected)
            => Assert.Equal(expected, LineNgImageUploader.IsDeterministicReject(status));

        [Fact]
        public void Body_with_only_errors_is_a_rejection()
        {
            Assert.True(LineNgImageUploader.IsRejectedInBody(
                "{\"results\":[],\"created\":0,\"duplicates\":0,\"errors\":[\"ng.tif: 지원하지 않는 확장자입니다\"]}", out var reason));
            Assert.Contains("확장자", reason);

            Assert.False(LineNgImageUploader.IsRejectedInBody(
                "{\"results\":[{\"created\":true}],\"created\":1,\"duplicates\":0,\"errors\":[]}", out _));
            Assert.False(LineNgImageUploader.IsRejectedInBody("not json", out _));
            Assert.False(LineNgImageUploader.IsRejectedInBody(null, out _));
        }

        [Fact]
        public void Tags_and_stem_come_from_context()
        {
            var ctx = new InspectionImageContext
            {
                Ok = false, CameraName = "Cam A", StepNumber = 2, RecipeName = "ProductA",
                CorrelationKey = "20260909T120000|1|abc", Timestamp = new DateTime(2026, 9, 9, 12, 0, 0)
            };
            Assert.Equal(new[] { "verdict:ng", "recipe:ProductA", "camera:Cam A", "step:2" }, LineNgImageUploader.BuildTags(ctx));

            // 같은 사이클의 카메라·스텝이 상관 키를 공유하므로 파일 이름에 둘을 덧붙여야 덮어쓰지 않는다
            var stem = LineNgImageUploader.BuildStem(ctx);
            Assert.Contains("Cam A", stem);
            Assert.Contains("2", stem);
            Assert.DoesNotContain("|", stem);   // 파일명 금지 문자는 치환
        }

        [Fact]
        public void Tiff_is_sent_as_png_because_server_rejects_tiff()
        {
            Assert.Equal(ImageSaveFormat.Png, LineNgImageUploader.FormatForServer(ImageSaveFormat.Tiff));
            Assert.Equal(ImageSaveFormat.Jpeg, LineNgImageUploader.FormatForServer(ImageSaveFormat.Jpeg));
            Assert.Equal(ImageSaveFormat.Bmp, LineNgImageUploader.FormatForServer(ImageSaveFormat.Bmp));
        }

        // ─── 큐 한 바퀴 ───────────────────────────────────────────

        [Fact]
        public async Task Ng_image_is_queued_and_posted_as_multipart_without_lineId()
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
                "{\"results\":[{\"created\":true}],\"created\":1,\"duplicates\":0,\"errors\":[]}"));
            using var up = NewUploader(handler);

            up.Enqueue(MakeImage(), NgContext("KEY-1"), new ImageSaveOptions { MlopsSendNg = true, Format = ImageSaveFormat.Png });
            await WaitUntilAsync(() => up.PendingCount == 1 || handler.Requests.Count == 1);
            await up.DrainOnceAsync();
            await WaitUntilAsync(() => handler.Requests.Count >= 1);

            var req = handler.Requests.Single();
            Assert.Equal("Bearer", req.Auth?.Scheme);
            Assert.Equal("ln_test", req.Auth?.Parameter);
            Assert.EndsWith("/api/images/line-ng", req.Url);
            Assert.DoesNotContain("lineId", req.Fields.Keys);            // 라인은 토큰이 말한다
            Assert.Equal("KEY-1", req.Fields["inspectionId"]);
            Assert.Contains("verdict:ng", req.Fields["tags"]);
            Assert.Contains("recipe:ProductA", req.Fields["tags"]);
            Assert.EndsWith("Z", req.Fields["capturedAt"]);              // UTC
            Assert.EndsWith(".png", req.FileName);
            Assert.Equal("image/png", req.FileContentType);
            Assert.True(req.FileBytes > 0);

            await WaitUntilAsync(() => up.PendingCount == 0);
            Assert.Equal(0, up.RejectedCount);
        }

        [Fact]
        public async Task Deterministic_4xx_moves_pair_to_rejected_and_is_not_retried()
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.BadRequest, "{\"message\":\"lineId 가 필요합니다.\"}"));
            using var up = NewUploader(handler);

            up.Enqueue(MakeImage(), NgContext("KEY-2"), new ImageSaveOptions { MlopsSendNg = true });
            await WaitUntilAsync(() => up.PendingCount == 1 || handler.Requests.Count == 1);
            await up.DrainOnceAsync();
            await WaitUntilAsync(() => up.RejectedCount == 1);

            Assert.Equal(0, up.PendingCount);
            Assert.Single(Directory.GetFiles(Path.Combine(_queueDir, "rejected"), "*.reason.txt"));

            await up.DrainOnceAsync();
            Assert.Equal(1, handler.Requests.Count);   // 거절 보관함은 다시 보내지 않는다
        }

        [Fact]
        public async Task Ok_200_with_only_errors_is_treated_as_rejected()
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
                "{\"results\":[],\"created\":0,\"duplicates\":0,\"errors\":[\"x.png: 손상된 이미지\"]}"));
            using var up = NewUploader(handler);

            up.Enqueue(MakeImage(), NgContext("KEY-3"), new ImageSaveOptions { MlopsSendNg = true });
            await WaitUntilAsync(() => up.PendingCount == 1 || handler.Requests.Count == 1);
            await up.DrainOnceAsync();
            await WaitUntilAsync(() => up.RejectedCount == 1);
            Assert.Equal(0, up.PendingCount);
        }

        [Fact]
        public async Task Server_error_keeps_item_with_backoff()
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.ServiceUnavailable, ""));
            using var up = NewUploader(handler);

            up.Enqueue(MakeImage(), NgContext("KEY-4"), new ImageSaveOptions { MlopsSendNg = true });
            await WaitUntilAsync(() => up.PendingCount == 1 || handler.Requests.Count == 1);
            await up.DrainOnceAsync();
            await WaitUntilAsync(() => handler.Requests.Count >= 1);

            Assert.Equal(1, up.PendingCount);
            Assert.Equal(0, up.RejectedCount);
            var meta = JsonSerializer.Deserialize<LineNgUploadMeta>(File.ReadAllText(Directory.GetFiles(_queueDir, "*.json").Single()))!;
            Assert.Equal(1, meta.Attempts);
            Assert.False(string.IsNullOrEmpty(meta.NextAttemptAtUtc));

            await up.DrainOnceAsync();                 // 백오프 중 — 다시 보내지 않는다
            Assert.Equal(1, handler.Requests.Count);
        }

        [Fact]
        public async Task Ok_image_without_correlation_key_is_still_sent_without_inspectionId()
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
                "{\"results\":[{\"created\":true}],\"created\":1,\"duplicates\":0,\"errors\":[]}"));
            using var up = NewUploader(handler);

            var ctx = new InspectionImageContext { Ok = true, CameraName = "Cam", StepNumber = 1, Timestamp = DateTime.Now };
            up.Enqueue(MakeImage(), ctx, new ImageSaveOptions { MlopsSendNg = true, MlopsOkSampleRate = 1 });
            await WaitUntilAsync(() => up.PendingCount == 1 || handler.Requests.Count == 1);
            await up.DrainOnceAsync();
            await WaitUntilAsync(() => handler.Requests.Count >= 1);

            var req = handler.Requests.Single();
            Assert.DoesNotContain("inspectionId", req.Fields.Keys);
            Assert.Contains("verdict:ok", req.Fields["tags"]);
        }

        [Fact]
        public async Task Toggle_off_queues_nothing()
        {
            var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));
            using var up = NewUploader(handler);
            up.Enqueue(MakeImage(), NgContext("KEY-5"), new ImageSaveOptions { MlopsSendNg = false });
            await Task.Delay(300);
            Assert.Equal(0, up.PendingCount);
        }

        // ─── 도우미 ───────────────────────────────────────────────

        private LineNgImageUploader NewUploader(FakeHandler handler)
            => new("http://localhost:5310", "ln_test", _queueDir, new HttpClient(handler));

        private static InspectionImageContext NgContext(string key) => new()
        {
            Ok = false, CameraName = "Cam1", StepNumber = 1, RecipeName = "ProductA",
            CorrelationKey = key, Timestamp = DateTime.Now
        };

        private static BitmapSource MakeImage()
        {
            const int w = 8, h = 6;
            var pixels = new byte[w * h * 3];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, pixels, w * 3);
            bmp.Freeze();
            return bmp;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 10000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond())
            {
                if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("조건이 제한 시간 안에 참이 되지 않았다");
                await Task.Delay(50);
            }
        }

        /// <summary>보낸 multipart 를 그 자리에서 뜯어 기록하는 가짜 서버.</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<CapturedRequest, HttpResponseMessage> _respond;
            public List<CapturedRequest> Requests { get; } = new();

            public FakeHandler(Func<CapturedRequest, HttpResponseMessage> respond) => _respond = respond;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var cap = new CapturedRequest
                {
                    Url = request.RequestUri!.ToString(),
                    Auth = request.Headers.Authorization,
                };
                if (request.Content is MultipartFormDataContent form)
                {
                    foreach (var part in form)
                    {
                        var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                        var fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                        if (fileName != null)
                        {
                            cap.FileName = fileName;
                            cap.FileContentType = part.Headers.ContentType?.MediaType;
                            cap.FileBytes = (await part.ReadAsByteArrayAsync(ct)).Length;
                        }
                        else
                        {
                            cap.Fields[name] = await part.ReadAsStringAsync(ct);
                        }
                    }
                }
                lock (Requests) Requests.Add(cap);
                return _respond(cap);
            }
        }

        private sealed class CapturedRequest
        {
            public string Url { get; set; } = "";
            public System.Net.Http.Headers.AuthenticationHeaderValue? Auth { get; set; }
            public Dictionary<string, string> Fields { get; } = new();
            public string? FileName { get; set; }
            public string? FileContentType { get; set; }
            public int FileBytes { get; set; }
        }
    }
}
