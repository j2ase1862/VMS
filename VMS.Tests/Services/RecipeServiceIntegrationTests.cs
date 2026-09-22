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

        // ─── SaveRecipe setAsCurrent — Web stub 백그라운드 저장의 CurrentRecipe 보호 ───

        [Fact]
        public void SaveRecipe_SetAsCurrentFalse_DoesNotReplaceCurrentRecipe()
        {
            // 검사 중 Web 레시피가 동기화되면 빈 stub 저장이 CurrentRecipe 를
            // 조용히 교체하던 버그 (2026-08-10) 회귀 테스트
            var working = NewSampleRecipe("Working");
            _service.SaveRecipe(working);
            Assert.Same(working, _service.CurrentRecipe);

            var webStub = NewSampleRecipe("WebStub");
            var stubPath = Path.Combine(_tempDir, "web_5.json");
            Assert.True(_service.SaveRecipe(webStub, stubPath, setAsCurrent: false));

            Assert.True(File.Exists(stubPath));                 // 저장은 정상 수행
            Assert.Same(working, _service.CurrentRecipe);       // 현재 레시피는 유지
        }

        [Fact]
        public void SaveRecipe_Default_SetsCurrentRecipe()
        {
            var recipe = NewSampleRecipe();
            _service.SaveRecipe(recipe);
            Assert.Same(recipe, _service.CurrentRecipe);
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
        // ─── 레시피 왕복 무손실 (모델 통일) ─────────────────────────

        // VMS 가 VisionSetup 의 축소 사본 모델로 레시피를 읽던 시절, VMS 가 한 번 저장하면
        // 사본에 없는 필드가 파일에서 영구히 지워졌다 — 회전 ROI 각도, 스텝 Resolution,
        // 캘리브레이션, 판정 기준, Web 레시피 Id, 툴 캔버스 좌표 등 (2026-09-22 현장).

        [Fact]
        public void SaveThenLoad_PreservesFieldsVmsUsedToDrop()
        {
            var recipe = NewSampleRecipe("RoundTrip");
            recipe.WebRecipeId = 42;
            recipe.Tags.Add("line-1");
            recipe.Calibration = new VMS.VisionSetup.Models.CalibrationMetadata
            {
                Mode = VMS.VisionSetup.Models.CalibrationMode.SinglePointScale,
                PixelSizeMm = 0.0112,
                SourceToolName = "CalibrationTool"
            };
            recipe.Criteria = new PassFailCriteria
            {
                ToolCriteria =
                {
                    ["tool-1"] = new VMS.VisionSetup.Models.ToolPassCriteria
                    {
                        Ranges = { ["TotalArea"] = new VMS.VisionSetup.Models.RangeCriteria { Min = 8000, Max = 14200 } }
                    }
                }
            };
            recipe.Steps.Add(new InspectionStep
            {
                Id = "step-1",
                Name = "1-1",
                Sequence = 1,
                CameraId = "cam-1",
                Resolution = 0.05,
                IsEnabled = false,
                Tools =
                {
                    new ToolConfig
                    {
                        Id = "tool-1",
                        ToolType = "CaliperTool",
                        Name = "Caliper B",
                        X = 146, Y = 355,
                        UseROI = true,
                        ROIX = 770, ROIY = 872, ROIWidth = 316, ROIHeight = 125,
                        ROIAngle = 179.67071753615346,
                        ROICenterX = 928.99988426959, ROICenterY = 935.11904691049,
                        PlcMappings =
                        {
                            new VMS.VisionSetup.Models.PlcResultMapping
                            {
                                ResultKey = "Success", DeviceId = "ADLink_1", PlcAddress = "3"
                            }
                        }
                    }
                }
            });

            Assert.True(_service.SaveRecipe(recipe));
            var path = Path.Combine(_tempDir, $"recipe_{recipe.Id}.json");
            var loaded = _service.LoadRecipe(path);

            Assert.NotNull(loaded);
            Assert.Equal(42, loaded!.WebRecipeId);
            Assert.Equal(new[] { "line-1" }, loaded.Tags);
            Assert.Equal(0.0112, loaded.Calibration!.PixelSizeMm, 6);
            Assert.Equal(8000, loaded.Criteria!.ToolCriteria["tool-1"].Ranges["TotalArea"].Min);

            var step = Assert.Single(loaded.Steps);
            Assert.Equal(0.05, step.Resolution, 6);
            Assert.False(step.IsEnabled);

            var tool = Assert.Single(step.Tools);
            Assert.Equal(179.67071753615346, tool.ROIAngle, 9);
            Assert.Equal(928.99988426959, tool.ROICenterX, 6);
            Assert.Equal(146, tool.X, 6);
            Assert.Equal("ADLink_1", Assert.Single(tool.PlcMappings).DeviceId);
        }

        [Fact]
        public void LoadRecipe_AcceptsStringEnums_AndRewritesThemAsNumbers()
        {
            // 과거 VMS 가 JsonStringEnumConverter 로 저장한 레시피는 VisionSetup 이
            // 읽다 예외를 내 통째로 열리지 않았다. 공유 옵션은 문자열·숫자를 모두 읽고,
            // 구버전 VisionSetup 이 계속 열 수 있도록 숫자로 쓴다.
            var path = Path.Combine(_tempDir, "legacy_string_enum.json");
            File.WriteAllText(path, """
                {
                  "id": "legacy-1",
                  "name": "Legacy",
                  "eulerConvention": "UR_RotationVector",
                  "steps": [
                    { "id": "s1", "tools": [ { "id": "t1", "toolType": "BlobTool", "resultDataType": "Int16" } ] }
                  ]
                }
                """);

            var loaded = _service.LoadRecipe(path);

            Assert.NotNull(loaded);
            var tool = Assert.Single(Assert.Single(loaded!.Steps).Tools);
            Assert.Equal(VMS.PLC.Models.PlcDataType.Int16, tool.ResultDataType);

            var rewritten = Path.Combine(_tempDir, "rewritten.json");
            Assert.True(_service.ExportRecipe(loaded, rewritten));
            var json = File.ReadAllText(rewritten);
            Assert.Contains("\"resultDataType\":", json);
            Assert.DoesNotContain("\"Int16\"", json);
        }
    }
}
