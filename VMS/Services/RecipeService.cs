using VMS.Core.Security;
using VMS.Interfaces;
using VMS.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.Services
{
    /// <summary>
    /// Service for managing recipes (load, save, list)
    /// </summary>
    public class RecipeService : IRecipeService
    {
        private static RecipeService? _instance;
        public static RecipeService Instance => _instance ??= new RecipeService();

        private readonly string _recipesDirectory;
        private Recipe? _currentRecipe;

        // 외부 프로세스(VisionSetup)의 레시피 저장 감지 — 현장 사례(2026-08-10):
        // VisionSetup 에서 파라미터를 고쳐 저장해도 VMS 는 구버전 사본으로 계속 검사했다.
        private FileSystemWatcher? _watcher;
        private readonly ConcurrentDictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _lastRaised = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan SelfWriteSuppressWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

        public event Action<string>? ExternalRecipeFileChanged;

        // 레시피 직렬화 옵션은 VisionSetup 과 공유 — 정의처는 RecipeJson 하나뿐이다.
        // 종전에는 이쪽만 JsonStringEnumConverter 를 달고 있어 VMS 가 저장한 레시피의
        // 문자열 enum 을 VisionSetup 이 읽다 예외를 내고 레시피가 열리지 않았다.
        private static JsonSerializerOptions JsonOptions => VMS.VisionSetup.Services.RecipeJson.Options;

        public Recipe? CurrentRecipe => _currentRecipe;

        /// <summary>
        /// 현재 레시피를 읽어 온 파일 경로. 저장은 이 경로로 되돌려 써야 한다 —
        /// 레시피 파일 이름이 항상 recipe_&lt;Id&gt;.json 인 것은 아니어서(VisionSetup 은
        /// 이름 기반 파일도 만든다), Id 로 경로를 새로 만들면 같은 레시피가 파일만
        /// 하나 더 생긴다. 그러면 편집은 실행 중인 레시피에 영원히 반영되지 않고
        /// 목록에 중복만 늘어난다 (VisionSetup 쪽에서 먼저 겪은 현장 사고 2026-08-19).
        /// </summary>
        public string? CurrentRecipeFilePath { get; private set; }

        private RecipeService()
            : this(Path.Combine(ConfigurationService.Instance.ConfigDirectory, "Recipes"))
        {
        }

        /// <summary>
        /// 임의 디렉토리로 별도 인스턴스를 만들 수 있는 테스트 친화 ctor.
        /// internal — VMS.Tests 통합 테스트에서만 호출. 운영 코드는
        /// 반드시 <see cref="Instance"/> 싱글톤 사용.
        /// </summary>
        internal RecipeService(string recipesDirectory)
        {
            _recipesDirectory = recipesDirectory;
            if (!Directory.Exists(_recipesDirectory))
            {
                Directory.CreateDirectory(_recipesDirectory);
            }
        }

        /// <summary>
        /// Recipes 폴더의 외부 변경 감시 시작 (idempotent). 자기 저장(SaveRecipe/DeleteRecipe)은
        /// 무시하고, 한 저장이 만드는 다중 파일시스템 이벤트는 디바운스한다.
        /// 이벤트는 워처 스레드에서 발생 — 구독측에서 UI 디스패치 필요.
        /// </summary>
        public void StartWatchingRecipeFiles()
        {
            if (_watcher != null) return;

            _watcher = new FileSystemWatcher(_recipesDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            };
            _watcher.Changed += OnRecipeFileEvent;
            _watcher.Created += OnRecipeFileEvent;
            _watcher.Renamed += (s, e) => OnRecipeFileEvent(s, e);
            _watcher.EnableRaisingEvents = true;
        }

        private void OnRecipeFileEvent(object sender, FileSystemEventArgs e)
        {
            var now = DateTime.UtcNow;

            // 자기 저장 무시 — SaveRecipe/Web stub 동기화가 워처를 되받아 재로드 루프가 되면 안 된다
            if (_selfWrites.TryGetValue(e.FullPath, out var written) && now - written < SelfWriteSuppressWindow)
                return;

            if (_lastRaised.TryGetValue(e.FullPath, out var raised) && now - raised < DebounceWindow)
                return;
            _lastRaised[e.FullPath] = now;

            ExternalRecipeFileChanged?.Invoke(e.FullPath);
        }

        private void MarkSelfWrite(string path) => _selfWrites[path] = DateTime.UtcNow;

        /// <summary>
        /// 테스트 전용 — 워처 콜백을 직접 구동해 디바운스/자기 저장 억제 로직을
        /// FileSystemWatcher 타이밍(CI 플레이크 원인)과 무관하게 결정적으로 검증한다.
        /// </summary>
        internal void SimulateRecipeFileEvent(string fullPath) =>
            OnRecipeFileEvent(this, new FileSystemEventArgs(
                WatcherChangeTypes.Changed, Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath)));

        /// <summary>
        /// Load a recipe from file
        /// </summary>
        public Recipe? LoadRecipe(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonOptions);
                    if (recipe != null)
                    {
                        _currentRecipe = recipe;
                        CurrentRecipeFilePath = Path.GetFullPath(filePath);
                        AuditLogger.Instance.Log(
                            AuditCategory.RecipeChange, "LoadRecipe", AuditOutcome.Success,
                            source: nameof(RecipeService),
                            details: $"Id={recipe.Id}, Name='{recipe.Name}', Path='{filePath}'");
                        return recipe;
                    }
                }
                AuditLogger.Instance.Log(
                    AuditCategory.RecipeChange, "LoadRecipe", AuditOutcome.Failure,
                    source: nameof(RecipeService),
                    details: $"Path='{filePath}' — 파일 없음 또는 deserialize null");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recipe: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.RecipeChange, "LoadRecipe", AuditOutcome.Failure,
                    source: nameof(RecipeService),
                    details: $"Path='{filePath}', {ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Save a recipe to file.
        /// <paramref name="setAsCurrent"/> false — Web stub 동기화처럼 백그라운드 저장이
        /// 실행 중인 CurrentRecipe(검사가 참조)를 교체하면 안 되는 경로용 (2026-08-10).
        /// </summary>
        public bool SaveRecipe(Recipe recipe, string? filePath = null, bool setAsCurrent = true)
        {
            try
            {
                recipe.ModifiedAt = DateTime.UtcNow;

                var path = filePath ?? ResolveSavePath(recipe);
                var json = JsonSerializer.Serialize(recipe, JsonOptions);
                MarkSelfWrite(path);
                File.WriteAllText(path, json);

                if (setAsCurrent)
                {
                    _currentRecipe = recipe;
                    CurrentRecipeFilePath = Path.GetFullPath(path);
                }
                AuditLogger.Instance.Log(
                    AuditCategory.RecipeChange, "SaveRecipe", AuditOutcome.Success,
                    source: nameof(RecipeService),
                    details: $"Id={recipe.Id}, Name='{recipe.Name}', Path='{path}'");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving recipe: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.RecipeChange, "SaveRecipe", AuditOutcome.Failure,
                    source: nameof(RecipeService),
                    details: $"Id={recipe.Id}, Name='{recipe.Name}', {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get list of all available recipes
        /// </summary>
        public List<RecipeInfo> GetRecipeList()
        {
            var recipes = new List<RecipeInfo>();

            try
            {
                var files = Directory.GetFiles(_recipesDirectory, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonOptions);
                        if (recipe != null)
                        {
                            recipes.Add(new RecipeInfo
                            {
                                Id = recipe.Id,
                                Name = recipe.Name,
                                Author = recipe.Author,
                                Version = recipe.Version,
                                ModifiedAt = recipe.ModifiedAt,
                                FilePath = file,
                                StepCount = recipe.Steps.Count,
                                ToolCount = recipe.TotalToolCount,
                                WebRecipeId = recipe.WebRecipeId
                            });
                        }
                    }
                    catch
                    {
                        // Skip invalid recipe files
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error listing recipes: {ex.Message}");
            }

            return recipes;
        }

        /// <summary>
        /// Create a new empty recipe
        /// </summary>
        public Recipe CreateNewRecipe(string name = "New Recipe")
        {
            var recipe = new Recipe
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            _currentRecipe = recipe;
            CurrentRecipeFilePath = null;   // 아직 파일이 없다 — 첫 저장에서 Id 기반 경로로 생성
            return recipe;
        }

        /// <summary>
        /// Delete a recipe by ID
        /// </summary>
        public bool DeleteRecipe(string id)
        {
            try
            {
                var path = GetRecipeFilePath(id);
                if (File.Exists(path))
                {
                    MarkSelfWrite(path);
                    File.Delete(path);
                    if (_currentRecipe?.Id == id)
                    {
                        _currentRecipe = null;
                        CurrentRecipeFilePath = null;
                    }
                    AuditLogger.Instance.Log(
                        AuditCategory.RecipeChange, "DeleteRecipe", AuditOutcome.Success,
                        source: nameof(RecipeService),
                        details: $"Id={id}, Path='{path}'");
                    return true;
                }

                // Try to find by filename pattern
                var files = Directory.GetFiles(_recipesDirectory, $"*{id}*.json");
                foreach (var file in files)
                {
                    MarkSelfWrite(file);
                    File.Delete(file);
                }
                if (files.Length > 0)
                {
                    AuditLogger.Instance.Log(
                        AuditCategory.RecipeChange, "DeleteRecipe", AuditOutcome.Success,
                        source: nameof(RecipeService),
                        details: $"Id={id}, MatchedFiles={files.Length}");
                }
                return files.Length > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting recipe: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.RecipeChange, "DeleteRecipe", AuditOutcome.Failure,
                    source: nameof(RecipeService),
                    details: $"Id={id}, {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export a recipe to a specified path
        /// </summary>
        public bool ExportRecipe(Recipe recipe, string exportPath)
        {
            try
            {
                var json = JsonSerializer.Serialize(recipe, JsonOptions);
                File.WriteAllText(exportPath, json);
                AuditLogger.Instance.Log(
                    AuditCategory.RecipeChange, "ExportRecipe", AuditOutcome.Success,
                    source: nameof(RecipeService),
                    details: $"Id={recipe.Id}, Name='{recipe.Name}', ExportPath='{exportPath}'");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting recipe: {ex.Message}");
                AuditLogger.Instance.Log(
                    AuditCategory.RecipeChange, "ExportRecipe", AuditOutcome.Failure,
                    source: nameof(RecipeService),
                    details: $"Id={recipe.Id}, ExportPath='{exportPath}', {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Import a recipe from an external path
        /// </summary>
        public Recipe? ImportRecipe(string importPath)
        {
            var recipe = LoadRecipe(importPath);
            if (recipe != null)
            {
                // Generate new ID to avoid conflicts
                recipe.Id = Guid.NewGuid().ToString();
                // 가져오기는 원본 파일이 아니라 Recipes 폴더에 사본을 만든다 —
                // 경로를 명시하지 않으면 방금 읽은 외부 경로로 되쓸 수 있다.
                SaveRecipe(recipe, GetRecipeFilePath(recipe.Id));
            }
            return recipe;
        }

        /// <summary>
        /// Set the current active recipe
        /// </summary>
        public void SetCurrentRecipe(Recipe? recipe)
        {
            if (!ReferenceEquals(_currentRecipe, recipe))
                CurrentRecipeFilePath = null;
            _currentRecipe = recipe;
        }

        /// <summary>
        /// 경로를 주지 않은 저장의 대상 파일. 지금 로드돼 있는 바로 그 레시피면
        /// 읽어 온 파일로 되돌려 쓰고, 아니면 Id 기반 새 경로를 만든다.
        /// 추적 경로가 Recipes 폴더 밖이면(외부에서 가져온 파일) 쓰지 않는다.
        /// </summary>
        private string ResolveSavePath(Recipe recipe)
        {
            if (ReferenceEquals(recipe, _currentRecipe)
                && !string.IsNullOrEmpty(CurrentRecipeFilePath)
                && string.Equals(
                    Path.GetDirectoryName(CurrentRecipeFilePath),
                    Path.GetFullPath(_recipesDirectory).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return CurrentRecipeFilePath!;
            }

            return GetRecipeFilePath(recipe.Id);
        }

        private string GetRecipeFilePath(string recipeId)
        {
            return Path.Combine(_recipesDirectory, $"recipe_{recipeId}.json");
        }

        /// <summary>
        /// Get the recipes directory path
        /// </summary>
        public string RecipesDirectory => _recipesDirectory;
    }
}
