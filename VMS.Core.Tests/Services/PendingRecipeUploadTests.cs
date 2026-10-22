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
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// Web 레시피 ID 를 모르는 채 끝난 사이클(기동 · 레시피 전환 때 Web 이 끊겨 있었다).
    ///
    /// <para><b>왜.</b> 예전에는 <c>CurrentRecipeId == 0</c> 이면 업로드도 큐 저장도 하지 않아, Web 이 돌아와도
    /// 그 사이 검사 이력 · WO 수량이 영영 빠졌다(로컬 이력에만 남음). 이제 레시피 이름을 달아 큐에 두고,
    /// 드레인이 이름을 ID 로 바꿔 보낸다. 반대로 Web 목록을 받아 봤는데 그 이름이 없으면(Web 에 없는 레시피)
    /// 올릴 곳이 없으므로 큐를 막지 않도록 넣지 않는다.</para>
    /// </summary>
    public class PendingRecipeUploadTests : IDisposable
    {
        private readonly string _queueDir = Path.Combine(Path.GetTempPath(), "vms_pending_recipe_" + Guid.NewGuid().ToString("N"));
        private readonly StubWeb _web = new();

        public void Dispose()
        {
            try { Directory.Delete(_queueDir, recursive: true); } catch { /* 무시 */ }
        }

        private ParameterSyncService CreateService(string recipeName = "Fitting-A")
            => new("http://localhost:5292", 3, "", _web, _queueDir, startRetryTimer: false)
            {
                DesiredRecipeName = recipeName,
                WorkOrderId = 41,
            };

        private string[] QueueFiles() => Directory.GetFiles(_queueDir, "*.json");
        private string[] RejectedFiles()
        {
            var dir = Path.Combine(_queueDir, "rejected");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : Array.Empty<string>();
        }

        [Fact]
        public async Task Web_down_queues_by_name_and_drain_sends_with_resolved_id()
        {
            _web.Up = false;
            using var svc = CreateService();

            Assert.True(await svc.UploadForPendingRecipeAsync(new List<ParameterResultDto>(), overallPass: true));
            var queued = Assert.Single(QueueFiles());
            Assert.Contains("\"pendingRecipeName\":\"Fitting-A\"", File.ReadAllText(queued));
            Assert.Empty(_web.Uploads);

            _web.Up = true;
            _web.RecipeList = new() { new RecipeSummaryDto { Id = 7, Name = "fitting-a" } };   // 대소문자 무시
            await svc.DrainQueueOnceAsync();

            Assert.Empty(QueueFiles());
            var sent = Assert.Single(_web.Uploads);
            Assert.Equal(7, sent.GetProperty("recipeId").GetInt32());
            Assert.Equal(41, sent.GetProperty("workOrderId").GetInt32());   // 큐에 넣던 때의 WO 그대로
            Assert.False(sent.TryGetProperty("pendingRecipeName", out _));    // 큐 전용 필드는 Web 으로 안 나간다
        }

        [Fact]
        public async Task Recipe_not_on_web_is_not_queued()
        {
            _web.RecipeList = new() { new RecipeSummaryDto { Id = 7, Name = "Other" } };
            using var svc = CreateService();
            Assert.True(await svc.SyncRecipesAsync());

            Assert.False(await svc.UploadForPendingRecipeAsync(new List<ParameterResultDto>(), overallPass: true));
            Assert.Empty(QueueFiles());
            Assert.Empty(_web.Uploads);
        }

        [Fact]
        public async Task Known_name_uploads_immediately_with_its_id()
        {
            _web.RecipeList = new() { new RecipeSummaryDto { Id = 9, Name = "Fitting-A" } };
            using var svc = CreateService();
            await svc.SyncRecipesAsync();

            Assert.True(await svc.UploadForPendingRecipeAsync(new List<ParameterResultDto>(), overallPass: false));
            Assert.Equal(9, Assert.Single(_web.Uploads).GetProperty("recipeId").GetInt32());
            Assert.Empty(QueueFiles());
        }

        [Fact]
        public async Task Name_missing_after_web_returns_waits_then_moves_aside()
        {
            _web.Up = false;
            using var svc = CreateService();
            await svc.UploadForPendingRecipeAsync(new List<ParameterResultDto>(), overallPass: true);

            _web.Up = true;
            _web.RecipeList = new() { new RecipeSummaryDto { Id = 7, Name = "Other" } };
            await svc.DrainQueueOnceAsync();
            Assert.Single(QueueFiles());     // 유예 중 — 등록될 수 있으니 기다린다
            Assert.Empty(RejectedFiles());

            // 유예 시간보다 오래된 항목으로 만든다 (나이는 파일 이름의 UTC 시각으로 센다).
            var file = QueueFiles()[0];
            var old = DateTime.UtcNow - ParameterSyncService.NotRegisteredGrace - TimeSpan.FromMinutes(1);
            File.Move(file, Path.Combine(_queueDir, $"{old:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json"));
            await svc.DrainQueueOnceAsync();

            Assert.Empty(QueueFiles());
            Assert.Single(RejectedFiles());
            Assert.Empty(_web.Uploads);
        }

        [Fact]
        public async Task Periodic_resolve_reconnects_the_running_recipe()
        {
            _web.RecipeList = new() { new RecipeSummaryDto { Id = 12, Name = "Fitting-A" } };
            using var svc = CreateService();
            await svc.SyncRecipesAsync();
            Assert.Equal(0, svc.CurrentRecipeId);

            await svc.TryResolveDesiredRecipeAsync();

            Assert.Equal(12, svc.CurrentRecipeId);
        }

        /// <summary>Web 흉내 — 레시피 목록 · 레시피 파라미터 · 결과 업로드.</summary>
        private sealed class StubWeb : HttpMessageHandler
        {
            public bool Up { get; set; } = true;
            public List<RecipeSummaryDto> RecipeList { get; set; } = new();
            public List<JsonElement> Uploads { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (!Up) throw new HttpRequestException("연결할 수 없습니다 (시험)");

                var path = request.RequestUri!.AbsolutePath;
                if (request.Method == HttpMethod.Get && path.StartsWith("/api/parameters/sync/recipes/"))
                    return Json(JsonSerializer.Serialize(RecipeList, ParameterSyncService.JsonOptions));
                if (request.Method == HttpMethod.Get && path.StartsWith("/api/parameters/sync/recipe/"))
                    return Json("[]");
                if (request.Method == HttpMethod.Post && path == "/api/parameters/results")
                {
                    var body = await request.Content!.ReadAsStringAsync(ct);
                    lock (Uploads) Uploads.Add(JsonDocument.Parse(body).RootElement.Clone());
                    return Json("{}");
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
