using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 서버가 받지 않아 <c>upload_queue\rejected\</c> 로 간 검사 결과 옆에 사유 파일이 남는다.
    ///
    /// <para><b>왜.</b> 사용자 매뉴얼 10.5 는 "rejected\ 에 파일이 쌓이면 서버가 받지 않은 건(사유 파일 동봉)" 이라고
    /// 안내하는데, 종전에는 결과 JSON 만 옮기고 사유는 디버그 출력에만 있어 현장에서 원인을 볼 수 없었다
    /// (GS 매뉴얼 대조 06 #5).</para>
    /// </summary>
    public class RejectedUploadReasonFileTests : IDisposable
    {
        private readonly string _queueDir = Path.Combine(Path.GetTempPath(), "vms_reject_reason_" + Guid.NewGuid().ToString("N"));
        private readonly StubWeb _web = new();

        public void Dispose()
        {
            try { Directory.Delete(_queueDir, recursive: true); } catch { /* 무시 */ }
        }

        private ParameterSyncService CreateService()
            => new("http://localhost:5292", 3, "", _web, _queueDir, startRetryTimer: false);

        private string RejectedDir => Path.Combine(_queueDir, "rejected");

        [Fact]
        public async Task Rejected_upload_keeps_a_reason_file_next_to_the_result()
        {
            _web.Status = HttpStatusCode.BadRequest;
            _web.Body = "DlModelVersion 은 50자 이하여야 합니다";
            using var svc = CreateService();

            Assert.False(await svc.UploadResultsAsync(7, new List<ParameterResultDto>()));

            var json = Assert.Single(Directory.GetFiles(RejectedDir, "*.json"));
            var reason = Path.ChangeExtension(json, ParameterSyncService.RejectReasonExtension);
            Assert.True(File.Exists(reason), "결과 파일과 같은 이름의 사유 파일이 있어야 한다");
            var text = File.ReadAllText(reason);
            Assert.Contains("HTTP 400", text);
            Assert.Contains("DlModelVersion", text);          // 서버가 준 본문
            Assert.Equal(1, svc.RejectedUploadCount);          // 사유 파일은 건수에 들어가지 않는다
            Assert.Empty(Directory.GetFiles(_queueDir, "*.json"));
        }

        [Fact]
        public async Task Drain_moves_a_rejected_item_aside_with_its_reason()
        {
            // 서버가 죽어 있을 때 큐에 쌓였다가, 돌아온 서버가 거절하는 경우
            _web.Up = false;
            using var svc = CreateService();
            Assert.False(await svc.UploadResultsAsync(7, new List<ParameterResultDto>()));
            Assert.Single(Directory.GetFiles(_queueDir, "*.json"));
            Assert.False(Directory.Exists(RejectedDir) && Directory.GetFiles(RejectedDir).Length > 0);

            _web.Up = true;
            _web.Status = HttpStatusCode.UnprocessableEntity;
            _web.Body = "validation failed";
            await svc.DrainQueueOnceAsync();

            Assert.Empty(Directory.GetFiles(_queueDir, "*.json"));
            var json = Assert.Single(Directory.GetFiles(RejectedDir, "*.json"));
            var text = File.ReadAllText(Path.ChangeExtension(json, ParameterSyncService.RejectReasonExtension));
            Assert.Contains("HTTP 422", text);
            Assert.Contains("validation failed", text);
        }

        [Fact]
        public async Task Retryable_failure_writes_no_reason_file()
        {
            // 503 은 다시 보내면 되는 실패 — 재시도 큐에만 있고 사유 파일은 없다
            _web.Status = HttpStatusCode.ServiceUnavailable;
            using var svc = CreateService();

            Assert.False(await svc.UploadResultsAsync(7, new List<ParameterResultDto>()));

            Assert.Single(Directory.GetFiles(_queueDir, "*.json"));
            Assert.Empty(Directory.GetFiles(_queueDir, "*" + ParameterSyncService.RejectReasonExtension, SearchOption.AllDirectories));
        }

        private sealed class StubWeb : HttpMessageHandler
        {
            public bool Up { get; set; } = true;
            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
            public string Body { get; set; } = "{}";

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (!Up) throw new HttpRequestException("연결할 수 없습니다 (시험)");
                return Task.FromResult(new HttpResponseMessage(Status)
                {
                    Content = new StringContent(Body, Encoding.UTF8, "text/plain"),
                });
            }
        }
    }
}
