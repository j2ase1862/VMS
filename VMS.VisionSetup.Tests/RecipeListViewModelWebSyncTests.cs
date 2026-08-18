using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VMS.Core.Interfaces;
using VMS.Core.Models.ParameterSync;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// RecipeListViewModel 의 Web 레시피 자동 갱신(2026-08-10) 검증 —
    /// 기존에는 생성 시 1회만 동기화라 Recipe Manager 창이 열려 있는 동안
    /// Web 에서 만든 레시피가 나타나지 않았다. RecipeListChanged 구독으로
    /// 60초 폴링 변경이 창에 반영되고, 창이 닫히면 구독이 해제되어야 한다.
    /// </summary>
    public class RecipeListViewModelWebSyncTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), $"vms_recipelist_websync_{Guid.NewGuid():N}");

        public RecipeListViewModelWebSyncTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void Ctor_SubscribesToRecipeListChanged_AndDetachUnsubscribes()
        {
            var sync = new FakeSyncService();
            var vm = new RecipeListViewModel(new FakeRecipeService(_dir), new FakeDialogService(), sync);

            Assert.True(sync.HasSubscriber);

            vm.Detach();
            Assert.False(sync.HasSubscriber);
        }

        [Fact]
        public void Ctor_WithoutSyncService_NoSubscription_NoThrowOnDetach()
        {
            var vm = new RecipeListViewModel(new FakeRecipeService(_dir), new FakeDialogService());
            vm.Detach(); // no-op 이어야 함
        }

        [Fact]
        public async Task RecipeListChanged_CreatesWebStub_WhileWindowOpen()
        {
            var sync = new FakeSyncService();
            var vm = new RecipeListViewModel(new FakeRecipeService(_dir), new FakeDialogService(), sync);
            await Task.Delay(200); // 생성자 초기 동기화(빈 목록) 완료 대기

            sync.Recipes.Add(new RecipeSummaryDto { Id = 5, Name = "WebRecipe", Description = "from web" });
            sync.Raise();

            var stubPath = Path.Combine(_dir, "web_5.json");
            var sw = Stopwatch.StartNew();
            while (!File.Exists(stubPath) && sw.ElapsedMilliseconds < 3000)
                await Task.Delay(50);

            Assert.True(File.Exists(stubPath), "RecipeListChanged 이벤트가 web stub 을 생성해야 한다");
        }

        [Fact]
        public async Task RecipeListChanged_AfterDetach_DoesNothing()
        {
            var sync = new FakeSyncService();
            var vm = new RecipeListViewModel(new FakeRecipeService(_dir), new FakeDialogService(), sync);
            await Task.Delay(200);
            vm.Detach();

            sync.Recipes.Add(new RecipeSummaryDto { Id = 7, Name = "AfterClose" });
            sync.Raise();
            await Task.Delay(300);

            Assert.False(File.Exists(Path.Combine(_dir, "web_7.json")));
        }

        // ─── Web 원장 연결 (WebRecipeId) — 2026-08-11 ───

        [Fact]
        public async Task WebStub_CarriesWebRecipeId()
        {
            var sync = new FakeSyncService();
            var recipeSvc = new FakeRecipeService(_dir);
            var vm = new RecipeListViewModel(recipeSvc, new FakeDialogService(), sync);
            await Task.Delay(200);

            sync.Recipes.Add(new RecipeSummaryDto { Id = 5, Name = "WebOnly" });
            sync.Raise();
            var stubPath = Path.Combine(_dir, "web_5.json");
            await WaitForAsync(() => File.Exists(stubPath));

            Assert.Equal(5, recipeSvc.ReadRecipeFile(stubPath)?.WebRecipeId);
        }

        [Fact]
        public async Task LocalRecipe_SameNameOnWeb_AdoptsWebId_WithoutRegistering()
        {
            var sync = new FakeSyncService();
            var recipeSvc = new FakeRecipeService(_dir);
            var localPath = Path.Combine(_dir, "recipe_A1.json");
            recipeSvc.SaveRecipe(new Recipe { Name = "A1", Author = "user" }, localPath);

            // Web 목록을 VM 생성 전에 시드 — 생성자 초기 동기화가 이름 매칭을 보게 됨
            sync.Recipes.Add(new RecipeSummaryDto { Id = 3, Name = "A1" });

            var vm = new RecipeListViewModel(recipeSvc, new FakeDialogService(), sync);
            await WaitForAsync(() => recipeSvc.ReadRecipeFile(localPath)?.WebRecipeId == 3);

            Assert.Equal(3, recipeSvc.ReadRecipeFile(localPath)?.WebRecipeId);
            Assert.Empty(sync.RegisteredNames);   // 이름 매칭 시 신규 등록 안 함
        }

        [Fact]
        public async Task LocalRecipe_NotOnWeb_RegistersAndStoresId()
        {
            var sync = new FakeSyncService { NextRegisterId = 42 };
            var recipeSvc = new FakeRecipeService(_dir);
            var localPath = Path.Combine(_dir, "recipe_Solo.json");
            recipeSvc.SaveRecipe(new Recipe { Name = "Solo", Author = "user" }, localPath);

            var vm = new RecipeListViewModel(recipeSvc, new FakeDialogService(), sync);
            await Task.Delay(200);

            sync.Raise();   // Web 목록은 비어 있음 → 등록 경로
            await WaitForAsync(() => recipeSvc.ReadRecipeFile(localPath)?.WebRecipeId == 42);

            Assert.Contains("Solo", sync.RegisteredNames);
            Assert.Equal(42, recipeSvc.ReadRecipeFile(localPath)?.WebRecipeId);
        }

        [Fact]
        public async Task LocalRecipe_RegisterFails_StaysUnlinked_ForNextRetry()
        {
            var sync = new FakeSyncService { NextRegisterId = null };   // 오프라인 시뮬레이션
            var recipeSvc = new FakeRecipeService(_dir);
            var localPath = Path.Combine(_dir, "recipe_Off.json");
            recipeSvc.SaveRecipe(new Recipe { Name = "Off", Author = "user" }, localPath);

            var vm = new RecipeListViewModel(recipeSvc, new FakeDialogService(), sync);
            await Task.Delay(200);

            sync.Raise();
            await WaitForAsync(() => sync.RegisteredNames.Count > 0);

            Assert.Null(recipeSvc.ReadRecipeFile(localPath)?.WebRecipeId);   // 다음 동기화 때 재시도
        }

        private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(50);
        }

        // ─── Fakes ──────────────────────────────────────────────

        private sealed class FakeRecipeService : IRecipeService
        {
            private readonly string _folder;
            public FakeRecipeService(string folder) => _folder = folder;

            public Recipe? CurrentRecipe { get; set; }
            public string RecipeFolderPath => _folder;
            public event EventHandler<Recipe?>? CurrentRecipeChanged { add { } remove { } }
            public Recipe? LoadRecipe(string filePath) => ReadRecipeFile(filePath);
            public Recipe? ReadRecipeFile(string filePath)
            {
                if (!File.Exists(filePath)) return null;
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<Recipe>(
                        File.ReadAllText(filePath));
                }
                catch { return null; }
            }
            public bool SaveRecipe(Recipe recipe, string? filePath = null)
            {
                filePath ??= Path.Combine(_folder, $"recipe_{recipe.Name}.json");
                File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(recipe));
                return true;
            }
            public bool SaveCurrentRecipe(string? filePath = null) => true;
            public Recipe CreateNewRecipe(string? name = null) => new();
            public bool DeleteRecipe(string filePath) => true;
            public List<RecipeInfo> GetRecipeList()
            {
                var list = new List<RecipeInfo>();
                foreach (var file in Directory.GetFiles(_folder, "*.json"))
                {
                    var recipe = ReadRecipeFile(file);
                    if (recipe == null) continue;
                    list.Add(new RecipeInfo
                    {
                        Id = recipe.Id,
                        Name = recipe.Name,
                        Author = recipe.Author,
                        FilePath = file,
                        WebRecipeId = recipe.WebRecipeId
                    });
                }
                return list;
            }
            public bool ExportRecipe(Recipe recipe, string exportPath) => true;
            public Recipe? ImportRecipe(string importPath) => null;
            public InspectionStep? AddStep(Recipe? recipe = null, string? cameraId = null) => null;
            public void AddStep(Recipe recipe, InspectionStep step) { }
            public bool RemoveStep(Recipe? recipe, string stepId) => false;
            public bool MoveStep(Recipe? recipe, string stepId, int newSequence) => false;
            public InspectionStep? FindStepByRobotNode(Recipe? recipe, int nodeIndex, string? cameraId = null) => null;
            public bool AddToolToStep(InspectionStep step, ToolConfig tool) => false;
            public bool AddToolToStep(Recipe recipe, string stepId, ToolConfig tool) => false;
            public bool AddToolToStep(InspectionStep step, VisionToolBase tool) => false;
            public bool RemoveToolFromStep(InspectionStep step, string toolId) => false;
            public bool RemoveToolFromStep(Recipe recipe, string stepId, string toolId) => false;
            public List<VisionToolBase> GetToolsFromRecipe(Recipe? recipe = null) => new();
            public List<VisionToolBase> GetToolsFromStep(InspectionStep step) => new();
            public void SetCurrentRecipe(Recipe? recipe) => CurrentRecipe = recipe;
            public void CloseCurrentRecipe() => CurrentRecipe = null;
        }

        private sealed class FakeDialogService : IDialogService
        {
            public void ShowInformation(string message, string title) { }
            public void ShowWarning(string message, string title) { }
            public void ShowError(string message, string title) { }
            public bool ShowConfirmation(string message, string title) => false;
            public string? ShowOpenFileDialog(string title, string filter) => null;
            public string? ShowFolderBrowserDialog(string description) => null;
            public string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null) => null;
            public string? ShowRenameDialog(string currentName) => null;
            public void ShowCameraManagerDialog() { }
            public Recipe? ShowRecipeManagerDialog() => null;
            public void ShowSequenceEditorDialog(IEnumerable<SequenceDeviceEntry>? extraDevices = null) { }
            public void ShowCalibrationManagerDialog() { }
            public RecipeTemplate? ShowTemplateGalleryDialog() => null;
        }

        private sealed class FakeSyncService : IParameterSyncService
        {
            public event Action<bool, int>? SyncCompleted { add { } remove { } }
            public event Action<int, string, int>? RecipeLoaded { add { } remove { } }
            public event Action<List<RecipeSummaryDto>>? RecipeListChanged;
            public event Action<WorkOrderProgressDto>? WorkOrderProgressed { add { } remove { } }
            public event Action<WorkOrderProgressDto>? WorkOrderCompleted { add { } remove { } }

            public DateTime? LastSyncedAt => null;
            public int CurrentRecipeId => 0;
            public int CachedItemCount => 0;
            public int PendingUploadCount => 0;
            public List<RecipeSummaryDto> Recipes { get; } = new();
            public int? WorkOrderId { get; set; }
            public int? LotId { get; set; }
            public int? OperatorId { get; set; }
            public string? SerialNumber { get; set; }

            public bool HasSubscriber => RecipeListChanged != null;
            public void Raise() => RecipeListChanged?.Invoke(Recipes);

            public List<string> RegisteredNames { get; } = new();
            public int? NextRegisterId { get; set; }

            public Task<bool> SyncRecipesAsync() => Task.FromResult(true);
            public Task<int?> RegisterRecipeAsync(string name, string? description = null)
            {
                RegisteredNames.Add(name);
                return Task.FromResult(NextRegisterId);
            }
            public Task<bool> LoadRecipeAsync(int recipeId) => Task.FromResult(true);
            public Task<bool> SyncAsync() => Task.FromResult(true);
            public void StartPeriodicSync(int intervalSeconds = 60) { }
            public double ResolveValue(int paramCode, double defaultValue = 0.0) => defaultValue;
            public bool ValidateCode(int paramCode) => false;
            public (bool exists, bool synced) CheckCodeStatus(int paramCode, double currentSetupValue) => (false, false);
            public List<RecipeParameterDto> GetAll() => new();
            public Task<bool> UploadResultsAsync(
                int recipeId,
                List<ParameterResultDto> results,
                InspectionFeatureMetrics? featureMetrics = null,
                string? correlationKey = null,
                bool? overallPass = null) => Task.FromResult(true);
            public void Dispose() { }
        }
    }
}
