using System;
using System.Collections.Generic;
using System.IO;
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
    /// 운전 레시피를 이름으로 Web 레시피에 연결한다(<see cref="ParameterSyncService.LinkRecipeByNameAsync"/>).
    ///
    /// <para><b>왜.</b> 이름이 Web 목록에 없거나 파라미터를 받지 못하면 연결을 바꾸지 않고 돌아와, 직전 레시피의
    /// Web ID 가 남았다. 그러면 새 레시피의 검사 결과가 직전 레시피 ID 로 올라가고(작업지시 수량 · 이력이 엉뚱한
    /// 레시피로) Recent Inspections 에도 직전 레시피 이름이 찍혔다. 이제 연결하지 못하면 비운다(ID 0) — 그러면
    /// 결과는 이름으로 큐에 쌓이거나(Web 끊김) 올리지 않는다(Web 에 없는 레시피).</para>
    /// </summary>
    public class RecipeLinkTests : IDisposable
    {
        private readonly string _queueDir = Path.Combine(Path.GetTempPath(), "vms_recipe_link_" + Guid.NewGuid().ToString("N"));
        private readonly StubWeb _web = new();

        public void Dispose()
        {
            try { Directory.Delete(_queueDir, recursive: true); } catch { /* 무시 */ }
        }

        private ParameterSyncService CreateService()
            => new("http://localhost:5292", 3, "", _web, _queueDir, startRetryTimer: false);

        private async Task<ParameterSyncService> LinkedToAAsync()
        {
            _web.RecipeList = new() { new RecipeSummaryDto { Id = 7, Name = "Fitting-A" } };
            var svc = CreateService();
            var first = await svc.LinkRecipeByNameAsync("Fitting-A");
            Assert.Equal(RecipeLinkOutcome.Linked, first.Outcome);
            Assert.Equal(7, svc.CurrentRecipeId);
            Assert.Equal(1, svc.CachedItemCount);
            return svc;
        }

        [Fact]
        public async Task Name_not_on_web_drops_previous_link()
        {
            using var svc = await LinkedToAAsync();

            var link = await svc.LinkRecipeByNameAsync("Fitting-B");

            Assert.Equal(RecipeLinkOutcome.NotOnWeb, link.Outcome);
            Assert.True(link.DroppedPrevious);
            Assert.Equal(7, link.PreviousRecipeId);
            Assert.Equal(0, svc.CurrentRecipeId);
            Assert.Equal(0, svc.CachedItemCount);
            Assert.Equal("Fitting-B", svc.DesiredRecipeName);
            // Web 에 없는 레시피의 결과는 올릴 곳이 없다 — 직전 레시피로도 올리지 않는다.
            Assert.False(await svc.UploadForPendingRecipeAsync(new List<ParameterResultDto>(), overallPass: true));
            Assert.Empty(_web.Uploads);
        }

        [Fact]
        public async Task Web_down_drops_previous_link_and_results_queue_by_new_name()
        {
            using var svc = await LinkedToAAsync();
            _web.Up = false;

            var link = await svc.LinkRecipeByNameAsync("Fitting-B");

            Assert.Equal(RecipeLinkOutcome.WebUnavailable, link.Outcome);
            Assert.Equal(0, svc.CurrentRecipeId);
            Assert.True(await svc.UploadForPendingRecipeAsync(new List<ParameterResultDto>(), overallPass: true));
            var queued = Assert.Single(Directory.GetFiles(_queueDir, "*.json"));
            var text = File.ReadAllText(queued);
            Assert.Contains("\"pendingRecipeName\":\"Fitting-B\"", text);
            Assert.Contains("\"recipeId\":0", text);
        }

        [Fact]
        public async Task Parameter_load_failure_drops_previous_link()
        {
            using var svc = await LinkedToAAsync();
            _web.RecipeList.Add(new RecipeSummaryDto { Id = 8, Name = "Fitting-B" });
            _web.FailParamsFor.Add(8);

            var link = await svc.LinkRecipeByNameAsync("Fitting-B");

            Assert.Equal(RecipeLinkOutcome.LoadFailed, link.Outcome);
            Assert.Equal(0, svc.CurrentRecipeId);

            // 주기 동기화가 이름으로 다시 연결한다.
            _web.FailParamsFor.Clear();
            await svc.TryResolveDesiredRecipeAsync();
            Assert.Equal(8, svc.CurrentRecipeId);
        }

        [Fact]
        public async Task Recipe_added_on_web_after_list_was_cached_is_found()
        {
            using var svc = await LinkedToAAsync();
            _web.RecipeList.Add(new RecipeSummaryDto { Id = 8, Name = "fitting-b" });   // 대소문자 무시

            var link = await svc.LinkRecipeByNameAsync("Fitting-B");

            Assert.Equal(RecipeLinkOutcome.Linked, link.Outcome);
            Assert.Equal(8, svc.CurrentRecipeId);
        }

        [Fact]
        public async Task Same_recipe_again_keeps_link_without_reloading()
        {
            using var svc = await LinkedToAAsync();
            var loads = _web.ParamRequests;

            var link = await svc.LinkRecipeByNameAsync("FITTING-A");

            Assert.Equal(RecipeLinkOutcome.AlreadyLinked, link.Outcome);
            Assert.False(link.DroppedPrevious);
            Assert.Equal(7, svc.CurrentRecipeId);
            Assert.Equal(loads, _web.ParamRequests);
        }

        [Fact]
        public async Task No_recipe_clears_link_and_desired_name()
        {
            using var svc = await LinkedToAAsync();

            var link = await svc.LinkRecipeByNameAsync(null);

            Assert.Equal(RecipeLinkOutcome.NoRecipe, link.Outcome);
            Assert.Equal(0, svc.CurrentRecipeId);
            Assert.Null(svc.DesiredRecipeName);
        }

        [Fact]
        public async Task Refresh_started_before_switch_does_not_restore_old_recipe()
        {
            using var svc = await LinkedToAAsync();
            _web.RecipeList.Add(new RecipeSummaryDto { Id = 8, Name = "Fitting-B" });

            // 주기 동기화가 직전 레시피(7)를 다시 받기 시작했다 — 응답은 레시피를 바꾼 뒤에 온다.
            var gate = new TaskCompletionSource();
            _web.ParamGates[7] = gate.Task;
            var refresh = svc.SyncAsync();

            var link = await svc.LinkRecipeByNameAsync("Fitting-B");
            Assert.Equal(RecipeLinkOutcome.Linked, link.Outcome);

            gate.SetResult();
            Assert.False(await refresh);
            Assert.Equal(8, svc.CurrentRecipeId);
        }

        [Fact]
        public async Task Earlier_failed_switch_does_not_clear_later_link()
        {
            using var svc = await LinkedToAAsync();
            _web.RecipeList.Add(new RecipeSummaryDto { Id = 8, Name = "Fitting-B" });
            _web.RecipeList.Add(new RecipeSummaryDto { Id = 9, Name = "Fitting-C" });

            // B 로 바꾸는 중 운전자가 C 로 다시 바꿨고, B 의 파라미터 응답은 실패로 늦게 온다.
            var gate = new TaskCompletionSource();
            _web.ParamGates[8] = gate.Task;
            _web.FailParamsFor.Add(8);
            var toB = svc.LinkRecipeByNameAsync("Fitting-B");

            var toC = await svc.LinkRecipeByNameAsync("Fitting-C");
            Assert.Equal(RecipeLinkOutcome.Linked, toC.Outcome);

            gate.SetResult();
            Assert.Equal(RecipeLinkOutcome.Superseded, (await toB).Outcome);   // 실패했지만 C 연결은 지우지 않는다
            Assert.Equal(9, svc.CurrentRecipeId);
        }

        /// <summary>Web 흉내 — 레시피 목록 · 레시피 파라미터(지연 · 실패 지정) · 결과 업로드.</summary>
        private sealed class StubWeb : HttpMessageHandler
        {
            public bool Up { get; set; } = true;
            public List<RecipeSummaryDto> RecipeList { get; set; } = new();
            public HashSet<int> FailParamsFor { get; } = new();
            public Dictionary<int, Task> ParamGates { get; } = new();
            public List<JsonElement> Uploads { get; } = new();
            public int ParamRequests;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (!Up) throw new HttpRequestException("연결할 수 없습니다 (시험)");

                var path = request.RequestUri!.AbsolutePath;
                if (request.Method == HttpMethod.Get && path.StartsWith("/api/parameters/sync/recipes/"))
                    return Json(JsonSerializer.Serialize(RecipeList, ParameterSyncService.JsonOptions));
                if (request.Method == HttpMethod.Get && path.StartsWith("/api/parameters/sync/recipe/"))
                {
                    Interlocked.Increment(ref ParamRequests);
                    var id = int.Parse(path.Substring(path.LastIndexOf('/') + 1));
                    if (ParamGates.TryGetValue(id, out var gate)) await gate;
                    if (FailParamsFor.Contains(id)) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    var parameters = new List<RecipeParameterDto>
                    {
                        new() { RecipeId = id, ParamCode = 100 + id, ParamValue = id, IsActive = true },
                    };
                    return Json(JsonSerializer.Serialize(parameters, ParameterSyncService.JsonOptions));
                }
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
