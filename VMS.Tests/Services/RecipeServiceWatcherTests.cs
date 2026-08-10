using System;
using System.IO;
using System.Threading;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// RecipeService 외부 변경 감시 검증 (2026-08-10 현장: VisionSetup 저장이
    /// 실행 중인 VMS 에 반영되지 않던 문제의 감지 계층).
    /// - 외부 프로세스의 파일 쓰기 → ExternalRecipeFileChanged 발생
    /// - 자기 저장(SaveRecipe/DeleteRecipe)은 무시 (재로드 루프 방지)
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

        [Fact]
        public void ExternalWrite_RaisesEvent()
        {
            using var raised = new ManualResetEventSlim(false);
            string? changedPath = null;
            _service.ExternalRecipeFileChanged += p => { changedPath = p; raised.Set(); };
            _service.StartWatchingRecipeFiles();

            var path = Path.Combine(_tempDir, "recipe_external.json");
            File.WriteAllText(path, "{\"name\":\"External\"}");   // VisionSetup 저장 흉내

            Assert.True(raised.Wait(TimeSpan.FromSeconds(5)), "외부 쓰기 이벤트가 발생해야 한다");
            Assert.Equal(path, changedPath, ignoreCase: true);
        }

        [Fact]
        public void OwnSaveRecipe_DoesNotRaise()
        {
            using var raised = new ManualResetEventSlim(false);
            _service.ExternalRecipeFileChanged += _ => raised.Set();
            _service.StartWatchingRecipeFiles();

            _service.SaveRecipe(new Recipe { Name = "Self" });

            Assert.False(raised.Wait(TimeSpan.FromSeconds(1)), "자기 저장은 이벤트를 발생시키면 안 된다");
        }

        [Fact]
        public void OwnDeleteRecipe_DoesNotRaise()
        {
            var recipe = new Recipe { Name = "ToDelete" };
            _service.SaveRecipe(recipe);
            Thread.Sleep(600); // 저장 억제창(2s)과 별개로 디바운스 상태 분리

            using var raised = new ManualResetEventSlim(false);
            _service.ExternalRecipeFileChanged += _ => raised.Set();
            _service.StartWatchingRecipeFiles();

            _service.DeleteRecipe(recipe.Id);

            Assert.False(raised.Wait(TimeSpan.FromSeconds(1)), "자기 삭제는 이벤트를 발생시키면 안 된다");
        }

        [Fact]
        public void StartWatching_IsIdempotent()
        {
            _service.StartWatchingRecipeFiles();
            _service.StartWatchingRecipeFiles(); // 중복 호출에도 예외 없음

            using var raised = new ManualResetEventSlim(false);
            var count = 0;
            _service.ExternalRecipeFileChanged += _ => { Interlocked.Increment(ref count); raised.Set(); };

            File.WriteAllText(Path.Combine(_tempDir, "recipe_x.json"), "{}");

            Assert.True(raised.Wait(TimeSpan.FromSeconds(5)));
            Thread.Sleep(600); // 디바운스 창 내 중복 이벤트 흡수 확인
            Assert.Equal(1, count);
        }
    }
}
