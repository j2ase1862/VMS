using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// VM-35 — Inspection Parameters 창에서 다른 레시피를 골라 보는 것(보기 전용 조회)은
    /// 운전에 쓰는 Web 파라미터 캐시 · CurrentRecipeId 를 바꾸면 안 된다.
    /// </summary>
    public class ParameterSyncFetchOnlyTests : IDisposable
    {
        private readonly string _queueDir = Path.Combine(Path.GetTempPath(), $"psync_fetch_{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { if (Directory.Exists(_queueDir)) Directory.Delete(_queueDir, recursive: true); } catch { }
        }

        [Fact]
        public async Task Fetch_DoesNotChangeCurrentRecipeOrCache()
        {
            var handler = new StubHandler(path => path.EndsWith("/recipe/7")
                ? "[{\"paramCode\":1,\"paramValue\":10.0,\"isActive\":true}]"
                : "[{\"paramCode\":2,\"paramValue\":20.0,\"isActive\":true},{\"paramCode\":3,\"paramValue\":30.0,\"isActive\":false}]");
            using var svc = new ParameterSyncService("https://web.example", 1, "", handler, _queueDir, startRetryTimer: false);

            Assert.True(await svc.LoadRecipeAsync(7));
            Assert.Equal(7, svc.CurrentRecipeId);

            var other = await svc.FetchRecipeParametersAsync(12);

            Assert.NotNull(other);
            Assert.Single(other!);                // 비활성 항목은 캐시와 같은 기준으로 뺀다
            Assert.Equal(2, other![0].ParamCode);
            Assert.Equal(7, svc.CurrentRecipeId); // 운전 연결은 그대로
            var cached = Assert.Single(svc.GetAll());
            Assert.Equal(1, cached.ParamCode);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<string, string> _body;
            public StubHandler(Func<string, string> body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body(request.RequestUri!.AbsolutePath), Encoding.UTF8, "application/json")
                });
        }
    }
}
