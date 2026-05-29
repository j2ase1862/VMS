using System;
using System.IO;
using System.Linq;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 임시 디렉토리로 격리된 RecipeService 인스턴스로 Load/Save/Delete/Export/Import
    /// 전체 흐름을 검증. 싱글톤(Instance) 이 아닌 internal ctor 사용.
    ///
    /// 각 테스트는 IDisposable 의 Dispose 에서 임시 디렉토리를 정리.
    /// </summary>
    public class RecipeServiceIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly RecipeService _service;

        public RecipeServiceIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"recipe_int_{Guid.NewGuid():N}");
            _service = new RecipeService(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // 테스트 정리 실패는 무시.
            }
        }

        private static Recipe NewSampleRecipe(string? name = null)
        {
            return new Recipe
            {
                Name = name ?? "Sample-A",
                Description = "통합 테스트용 레시피",
                Version = "1.0.0",
                Author = "test"
            };
        }

        // ─── Constructor — 디렉토리 자동 생성 ────────────────────────

        [Fact]
        public void Ctor_CreatesDirectoryIfMissing()
        {
            var newDir = Path.Combine(_tempDir, "nested", "recipes");
            Assert.False(Directory.Exists(newDir));

            var svc = new RecipeService(newDir);
            Assert.True(Directory.Exists(newDir));
        }

        // ─── SaveRecipe → LoadRecipe round-trip ──────────────────────

        [Fact]
        public void SaveRecipe_DefaultPath_WritesFile()
        {
            var r = NewSampleRecipe();
            var ok = _service.SaveRecipe(r);
            Assert.True(ok);

            // 기본 경로는 _recipesDirectory + Id + .json 형태 — 파일 하나 생성 확인.
            var files = Directory.GetFiles(_tempDir, "*.json");
            Assert.Single(files);
        }

        [Fact]
        public void SaveRecipe_ThenLoadByPath_RoundTrip()
        {
            var r = NewSampleRecipe("RoundTrip");
            r.Steps.Add(new InspectionStep
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Step1"
            });

            _service.SaveRecipe(r);
            var savedFile = Directory.GetFiles(_tempDir, "*.json").Single();

            var loaded = _service.LoadRecipe(savedFile);
            Assert.NotNull(loaded);
            Assert.Equal(r.Id, loaded!.Id);
            Assert.Equal("RoundTrip", loaded.Name);
            Assert.Single(loaded.Steps);
            Assert.Equal("Step1", loaded.Steps[0].Name);
        }

        [Fact]
        public void SaveRecipe_SetsCurrentRecipe()
        {
            var r = NewSampleRecipe();
            _service.SaveRecipe(r);
            Assert.NotNull(_service.CurrentRecipe);
            Assert.Equal(r.Id, _service.CurrentRecipe!.Id);
        }

        [Fact]
        public void SaveRecipe_UpdatesModifiedAt()
        {
            var r = NewSampleRecipe();
            var before = r.ModifiedAt;
            // 1ms 이상의 차이 보장 — ModifiedAt 갱신 검증.
            System.Threading.Thread.Sleep(5);
            _service.SaveRecipe(r);
            Assert.True(r.ModifiedAt > before);
        }

        [Fact]
        public void SaveRecipe_ExplicitPath_WritesToThatPath()
        {
            var r = NewSampleRecipe();
            var customPath = Path.Combine(_tempDir, "custom_name.json");
            var ok = _service.SaveRecipe(r, customPath);
            Assert.True(ok);
            Assert.True(File.Exists(customPath));
        }

        [Fact]
        public void LoadRecipe_MissingFile_ReturnsNull()
        {
            var notExist = Path.Combine(_tempDir, "missing.json");
            var loaded = _service.LoadRecipe(notExist);
            Assert.Null(loaded);
        }

        [Fact]
        public void LoadRecipe_MalformedJson_ReturnsNull_NoThrow()
        {
            var bad = Path.Combine(_tempDir, "bad.json");
            File.WriteAllText(bad, "{ this is not json");
            var loaded = _service.LoadRecipe(bad);
            Assert.Null(loaded);
        }

        // ─── DeleteRecipe ─────────────────────────────────────────

        [Fact]
        public void DeleteRecipe_ById_RemovesFile()
        {
            var r = NewSampleRecipe();
            _service.SaveRecipe(r);
            Assert.Single(Directory.GetFiles(_tempDir, "*.json"));

            var ok = _service.DeleteRecipe(r.Id);
            Assert.True(ok);
            Assert.Empty(Directory.GetFiles(_tempDir, "*.json"));
        }

        [Fact]
        public void DeleteRecipe_NonExistingId_ReturnsFalse()
        {
            var ok = _service.DeleteRecipe("does-not-exist-1234");
            Assert.False(ok);
        }

        [Fact]
        public void DeleteRecipe_CurrentRecipeId_ClearsCurrent()
        {
            var r = NewSampleRecipe();
            _service.SaveRecipe(r);
            Assert.NotNull(_service.CurrentRecipe);

            _service.DeleteRecipe(r.Id);
            Assert.Null(_service.CurrentRecipe);
        }

        // ─── ExportRecipe ─────────────────────────────────────────

        [Fact]
        public void ExportRecipe_WritesToArbitraryPath()
        {
            var r = NewSampleRecipe("Exportable");
            var outDir = Path.Combine(_tempDir, "exports");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "exported.json");

            var ok = _service.ExportRecipe(r, outPath);
            Assert.True(ok);
            Assert.True(File.Exists(outPath));

            // export 된 파일에서 다시 로드하면 동일 객체.
            var loaded = _service.LoadRecipe(outPath);
            Assert.NotNull(loaded);
            Assert.Equal("Exportable", loaded!.Name);
        }

        [Fact]
        public void ExportRecipe_InvalidPath_ReturnsFalse_NoThrow()
        {
            var r = NewSampleRecipe();
            // 존재하지 않는 디렉토리 — File.WriteAllText 가 throw, ExportRecipe 가 catch 후 false 반환.
            var bad = Path.Combine(_tempDir, "does", "not", "exist", "x.json");
            var ok = _service.ExportRecipe(r, bad);
            Assert.False(ok);
        }

        // ─── GetRecipeList ────────────────────────────────────────

        [Fact]
        public void GetRecipeList_Empty_ReturnsEmpty()
        {
            var list = _service.GetRecipeList();
            Assert.Empty(list);
        }

        [Fact]
        public void GetRecipeList_MultipleRecipes_ReturnsAll()
        {
            _service.SaveRecipe(NewSampleRecipe("R-1"));
            _service.SaveRecipe(NewSampleRecipe("R-2"));
            _service.SaveRecipe(NewSampleRecipe("R-3"));

            var list = _service.GetRecipeList();
            Assert.Equal(3, list.Count);
            Assert.Contains(list, ri => ri.Name == "R-1");
            Assert.Contains(list, ri => ri.Name == "R-2");
            Assert.Contains(list, ri => ri.Name == "R-3");
        }

        [Fact]
        public void GetRecipeList_IgnoresNonJsonFiles()
        {
            _service.SaveRecipe(NewSampleRecipe());
            File.WriteAllText(Path.Combine(_tempDir, "garbage.txt"), "not a recipe");
            File.WriteAllText(Path.Combine(_tempDir, "notes.md"), "# notes");

            var list = _service.GetRecipeList();
            Assert.Single(list);  // 레시피 1개만
        }

        // ─── ImportRecipe ────────────────────────────────────────

        [Fact]
        public void ImportRecipe_AssignsNewId_AndSavesCopy()
        {
            var src = NewSampleRecipe("Source");
            var externalPath = Path.Combine(_tempDir, "external.json");
            _service.ExportRecipe(src, externalPath);

            var imported = _service.ImportRecipe(externalPath);
            Assert.NotNull(imported);
            Assert.NotEqual(src.Id, imported!.Id);  // 새 Id 부여 (충돌 회피)
            Assert.Equal("Source", imported.Name);  // 이름 유지

            // import 후 _recipesDirectory 에 사본 저장됨.
            var list = _service.GetRecipeList();
            Assert.Contains(list, ri => ri.Id == imported.Id);
        }
    }
}
