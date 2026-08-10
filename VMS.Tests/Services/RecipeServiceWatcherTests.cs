using System;
using System.IO;
using System.Linq;
using System.Threading;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// RecipeService 외부 변경 감시 검증 (2026-08-10 현장: VisionSetup 저장이
    /// 실행 중인 VMS 에 반영되지 않던 문제의 감지 계층).
    ///
    /// 디바운스·자기 저장 억제는 SimulateRecipeFileEvent 로 결정적으로 검증하고,
    /// 실제 FileSystemWatcher 는 재시도 쓰기 스모크 1건으로만 확인한다
    /// (CI 러너에서 단발 쓰기 이벤트가 늦게 도달하는 플레이크 회피).
    /// </summary>
    public class RecipeServiceWatcherTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly RecipeService _service;

        public RecipeServiceWatcherTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"recipe_watch_{Guid.NewGuid():N}");
            _service = new RecipeService(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        // ─── 실제 FileSystemWatcher 스모크 (재시도 쓰기로 CI 타이밍 흡수) ───

        [Fact]
        public void ExternalWrite_RaisesEvent()
        {
            using var raised = new ManualResetEventSlim(false);
            string? changedPath = null;
            _service.ExternalRecipeFileChanged += p => { changedPath = p; raised.Set(); };
            _service.StartWatchingRecipeFiles();
            _service.StartWatchingRecipeFiles(); // idempotent — 중복 호출에도 예외 없음

            // VisionSetup 저장 흉내 — CI 러너의 FSW 지연을 감안해 이벤트가 올 때까지 반복 쓰기
            var path = Path.Combine(_tempDir, "recipe_external.json");
            for (int i = 0; i < 20 && !raised.IsSet; i++)
            {
                File.WriteAllText(path, $"{{\"name\":\"External{i}\"}}");
                raised.Wait(TimeSpan.FromMilliseconds(500));
            }

            Assert.True(raised.IsSet, "외부 쓰기 이벤트가 발생해야 한다");
            Assert.EndsWith("recipe_external.json", changedPath, StringComparison.OrdinalIgnoreCase);
        }

        // ─── 결정적 검증 (SimulateRecipeFileEvent — FSW 타이밍 무관) ───

        [Fact]
        public void SelfSave_SuppressedWithinWindow()
        {
            var count = 0;
            _service.ExternalRecipeFileChanged += _ => count++;

            var recipe = new Recipe { Name = "Self" };
            _service.SaveRecipe(recipe);
            var savedPath = _service.GetRecipeList().Single().FilePath;

            _service.SimulateRecipeFileEvent(savedPath);   // 자기 저장 직후 워처 콜백 흉내

            Assert.Equal(0, count); // 자기 저장은 이벤트를 발생시키면 안 된다 (재로드 루프 방지)
        }

        [Fact]
        public void SelfDelete_SuppressedWithinWindow()
        {
            var recipe = new Recipe { Name = "ToDelete" };
            _service.SaveRecipe(recipe);
            var savedPath = _service.GetRecipeList().Single().FilePath;

            var count = 0;
            _service.ExternalRecipeFileChanged += _ => count++;

            _service.DeleteRecipe(recipe.Id);
            _service.SimulateRecipeFileEvent(savedPath);

            Assert.Equal(0, count);
        }

        [Fact]
        public void Debounce_SecondEventWithinWindowIgnored()
        {
            var count = 0;
            _service.ExternalRecipeFileChanged += _ => count++;

            var path = Path.Combine(_tempDir, "recipe_debounce.json");
            _service.SimulateRecipeFileEvent(path);   // 한 저장이 만드는 다중 FS 이벤트 흉내
            _service.SimulateRecipeFileEvent(path);
            _service.SimulateRecipeFileEvent(path);

            Assert.Equal(1, count);
        }

        [Fact]
        public void ExternalEvent_DifferentFiles_EachRaised()
        {
            var count = 0;
            _service.ExternalRecipeFileChanged += _ => count++;

            _service.SimulateRecipeFileEvent(Path.Combine(_tempDir, "recipe_a.json"));
            _service.SimulateRecipeFileEvent(Path.Combine(_tempDir, "recipe_b.json"));

            Assert.Equal(2, count); // 디바운스는 파일 단위 — 서로 다른 파일은 각각 통지
        }
    }
}
