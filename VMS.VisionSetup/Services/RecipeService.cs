using VMS.VisionSetup.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 레시피 관리 서비스
    /// </summary>
    public class RecipeService
    {
        private static readonly Lazy<RecipeService> _instance = new(() => new RecipeService());
        public static RecipeService Instance => _instance.Value;

        private readonly string _appDataPath;
        private readonly string _recipeFolderPath;
        private Recipe? _currentRecipe;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public event EventHandler<Recipe?>? CurrentRecipeChanged;

        private RecipeService()
        {
            _appDataPath = CameraService.Instance.GetAppDataFolderPath();
            _recipeFolderPath = Path.Combine(_appDataPath, "Recipes");
            EnsureDirectoryExists();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_recipeFolderPath))
            {
                Directory.CreateDirectory(_recipeFolderPath);
            }
        }

        /// <summary>
        /// 현재 활성화된 레시피
        /// </summary>
        public Recipe? CurrentRecipe
        {
            get => _currentRecipe;
            set
            {
                _currentRecipe = value;
                CurrentRecipeChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// 레시피 폴더 경로
        /// </summary>
        public string RecipeFolderPath => _recipeFolderPath;

        #region Load/Save

        /// <summary>
        /// 파일에서 레시피 로드
        /// </summary>
        public Recipe? LoadRecipe(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"레시피 파일을 찾을 수 없음: {filePath}");
                    return null;
                }

                var json = File.ReadAllText(filePath);
                var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonOptions);

                if (recipe != null)
                {
                    CurrentRecipe = recipe;
                }

                return recipe;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 로드 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 레시피를 파일에 저장
        /// </summary>
        public bool SaveRecipe(Recipe recipe, string? filePath = null)
        {
            try
            {
                EnsureDirectoryExists();

                recipe.ModifiedAt = DateTime.UtcNow;

                // 파일 경로가 없으면 기본 경로 사용
                if (string.IsNullOrEmpty(filePath))
                {
                    var safeFileName = GetSafeFileName(recipe.Name);
                    filePath = Path.Combine(_recipeFolderPath, $"recipe_{safeFileName}.json");
                }

                var json = JsonSerializer.Serialize(recipe, JsonOptions);
                File.WriteAllText(filePath, json);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 저장 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 현재 레시피 저장
        /// </summary>
        public bool SaveCurrentRecipe(string? filePath = null)
        {
            if (CurrentRecipe == null) return false;
            return SaveRecipe(CurrentRecipe, filePath);
        }

        #endregion

        #region Recipe Management

        /// <summary>
        /// 새 레시피 생성
        /// </summary>
        public Recipe CreateNewRecipe(string? name = null)
        {
            var recipe = new Recipe
            {
                Id = Guid.NewGuid().ToString(),
                Name = name ?? $"New Recipe {DateTime.Now:yyyyMMdd_HHmmss}",
                Description = string.Empty,
                Version = "1.0.0",
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Author = Environment.UserName,
                UsedCameraIds = new List<string>(),
                Steps = new List<InspectionStep>(),
                Criteria = new PassFailCriteria { RequireAllToolsPass = true }
            };

            CurrentRecipe = recipe;
            return recipe;
        }

        /// <summary>
        /// 레시피 삭제
        /// </summary>
        public bool DeleteRecipe(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 삭제 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 레시피 목록 조회
        /// </summary>
        public List<RecipeInfo> GetRecipeList()
        {
            var recipes = new List<RecipeInfo>();

            try
            {
                EnsureDirectoryExists();

                var files = Directory.GetFiles(_recipeFolderPath, "*.json");
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
                                Version = recipe.Version,
                                ModifiedAt = recipe.ModifiedAt,
                                Author = recipe.Author,
                                FilePath = file,
                                StepCount = recipe.Steps.Count,
                                ToolCount = recipe.Steps.Sum(s => s.Tools.Count)
                            });
                        }
                    }
                    catch
                    {
                        // 개별 파일 로드 실패는 무시
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 목록 조회 실패: {ex.Message}");
            }

            return recipes.OrderByDescending(r => r.ModifiedAt).ToList();
        }

        /// <summary>
        /// 레시피 내보내기 (다른 경로로 복사)
        /// </summary>
        public bool ExportRecipe(Recipe recipe, string exportPath)
        {
            try
            {
                var json = JsonSerializer.Serialize(recipe, JsonOptions);
                File.WriteAllText(exportPath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 내보내기 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 레시피 가져오기 (외부 파일에서)
        /// </summary>
        public Recipe? ImportRecipe(string importPath)
        {
            try
            {
                var recipe = LoadRecipeFromPath(importPath);
                if (recipe == null) return null;

                // 새 ID 할당하여 중복 방지
                recipe.Id = Guid.NewGuid().ToString();
                recipe.CreatedAt = DateTime.UtcNow;
                recipe.ModifiedAt = DateTime.UtcNow;

                // 내부 폴더에 저장
                SaveRecipe(recipe);

                return recipe;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 가져오기 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 경로에서 레시피 로드 (CurrentRecipe 설정 없이)
        /// </summary>
        private Recipe? LoadRecipeFromPath(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Recipe>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Step Management

        /// <summary>
        /// 레시피에 새 스텝 추가
        /// </summary>
        public InspectionStep? AddStep(Recipe? recipe = null, string? cameraId = null)
        {
            recipe ??= CurrentRecipe;
            if (recipe == null) return null;

            // Per-camera sequence: count only steps with same cameraId
            var effectiveCameraId = cameraId ?? string.Empty;
            int cameraStepCount = recipe.Steps.Count(s => s.CameraId == effectiveCameraId);
            int sequence = cameraStepCount + 1;

            var step = new InspectionStep
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Step {sequence}",
                Sequence = sequence,
                CameraId = effectiveCameraId,
                Exposure = 1000,
                Gain = 1.0,
                LightingChannel = 0,
                LightingIntensity = 100,
                Tools = new List<ToolConfig>(),
                IsEnabled = true
            };

            recipe.Steps.Add(step);
            recipe.ModifiedAt = DateTime.UtcNow;

            // 카메라 ID가 있으면 UsedCameraIds에 추가
            if (!string.IsNullOrEmpty(cameraId) && !recipe.UsedCameraIds.Contains(cameraId))
            {
                recipe.UsedCameraIds.Add(cameraId);
            }

            return step;
        }

        /// <summary>
        /// 레시피에 기존 스텝 추가
        /// </summary>
        public void AddStep(Recipe recipe, InspectionStep step)
        {
            if (recipe == null || step == null) return;

            // Per-camera sequence
            int cameraStepCount = recipe.Steps.Count(s => s.CameraId == step.CameraId);
            step.Sequence = cameraStepCount + 1;
            recipe.Steps.Add(step);
            recipe.ModifiedAt = DateTime.UtcNow;

            // 카메라 ID가 있으면 UsedCameraIds에 추가
            if (!string.IsNullOrEmpty(step.CameraId) && !recipe.UsedCameraIds.Contains(step.CameraId))
            {
                recipe.UsedCameraIds.Add(step.CameraId);
            }
        }

        /// <summary>
        /// 스텝 제거
        /// </summary>
        public bool RemoveStep(Recipe? recipe, string stepId)
        {
            recipe ??= CurrentRecipe;
            if (recipe == null) return false;

            var step = recipe.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return false;

            var cameraId = step.CameraId;
            recipe.Steps.Remove(step);

            // 같은 카메라의 스텝만 순서 재정렬
            ResequenceCameraSteps(recipe, cameraId);

            recipe.ModifiedAt = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// 스텝 순서 변경
        /// </summary>
        public bool MoveStep(Recipe? recipe, string stepId, int newSequence)
        {
            recipe ??= CurrentRecipe;
            if (recipe == null) return false;

            var step = recipe.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return false;

            // 같은 카메라의 스텝 내에서만 이동
            var cameraSteps = recipe.Steps
                .Where(s => s.CameraId == step.CameraId)
                .OrderBy(s => s.Sequence)
                .ToList();

            cameraSteps.Remove(step);
            newSequence = Math.Clamp(newSequence, 1, cameraSteps.Count + 1);
            cameraSteps.Insert(newSequence - 1, step);

            for (int i = 0; i < cameraSteps.Count; i++)
            {
                cameraSteps[i].Sequence = i + 1;
            }

            recipe.ModifiedAt = DateTime.UtcNow;
            return true;
        }

        #endregion

        #region Tool Management

        /// <summary>
        /// 스텝에 도구 추가
        /// </summary>
        public bool AddToolToStep(InspectionStep step, ToolConfig tool)
        {
            if (step == null || tool == null) return false;

            tool.Sequence = step.Tools.Count + 1;
            step.Tools.Add(tool);

            if (CurrentRecipe != null)
                CurrentRecipe.ModifiedAt = DateTime.UtcNow;

            return true;
        }

        /// <summary>
        /// 레시피 내 특정 스텝에 도구 추가
        /// </summary>
        public bool AddToolToStep(Recipe recipe, string stepId, ToolConfig tool)
        {
            if (recipe == null || tool == null) return false;

            var step = recipe.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return false;

            tool.Sequence = step.Tools.Count + 1;
            step.Tools.Add(tool);
            recipe.ModifiedAt = DateTime.UtcNow;

            return true;
        }

        /// <summary>
        /// VisionToolBase를 스텝에 추가
        /// </summary>
        public bool AddToolToStep(InspectionStep step, VisionToolBase tool)
        {
            if (step == null || tool == null) return false;

            var config = ToolSerializer.SerializeTool(tool);
            return AddToolToStep(step, config);
        }

        /// <summary>
        /// 스텝에서 도구 제거
        /// </summary>
        public bool RemoveToolFromStep(InspectionStep step, string toolId)
        {
            if (step == null) return false;

            var tool = step.Tools.FirstOrDefault(t => t.Id == toolId);
            if (tool == null) return false;

            step.Tools.Remove(tool);

            // 순서 재정렬
            for (int i = 0; i < step.Tools.Count; i++)
            {
                step.Tools[i].Sequence = i + 1;
            }

            if (CurrentRecipe != null)
                CurrentRecipe.ModifiedAt = DateTime.UtcNow;

            return true;
        }

        /// <summary>
        /// 레시피 내 특정 스텝에서 도구 제거
        /// </summary>
        public bool RemoveToolFromStep(Recipe recipe, string stepId, string toolId)
        {
            if (recipe == null) return false;

            var step = recipe.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step == null) return false;

            var tool = step.Tools.FirstOrDefault(t => t.Id == toolId);
            if (tool == null) return false;

            step.Tools.Remove(tool);

            // 순서 재정렬
            for (int i = 0; i < step.Tools.Count; i++)
            {
                step.Tools[i].Sequence = i + 1;
            }

            recipe.ModifiedAt = DateTime.UtcNow;
            return true;
        }

        #endregion

        #region VisionTool Conversion

        /// <summary>
        /// 레시피의 모든 도구를 VisionToolBase 인스턴스로 변환
        /// </summary>
        public List<VisionToolBase> GetToolsFromRecipe(Recipe? recipe = null)
        {
            recipe ??= CurrentRecipe;
            if (recipe == null) return new List<VisionToolBase>();

            var tools = new List<VisionToolBase>();

            foreach (var step in recipe.Steps.OrderBy(s => s.Sequence))
            {
                if (!step.IsEnabled) continue;

                foreach (var toolConfig in step.Tools.OrderBy(t => t.Sequence))
                {
                    if (!toolConfig.IsEnabled) continue;

                    var tool = ToolSerializer.DeserializeTool(toolConfig);
                    if (tool != null)
                    {
                        tools.Add(tool);
                    }
                }
            }

            return tools;
        }

        /// <summary>
        /// 특정 스텝의 도구들을 VisionToolBase 인스턴스로 변환
        /// </summary>
        public List<VisionToolBase> GetToolsFromStep(InspectionStep step)
        {
            var tools = new List<VisionToolBase>();

            foreach (var toolConfig in step.Tools.OrderBy(t => t.Sequence))
            {
                if (!toolConfig.IsEnabled) continue;

                var tool = ToolSerializer.DeserializeTool(toolConfig);
                if (tool != null)
                {
                    tools.Add(tool);
                }
            }

            return tools;
        }

        #endregion

        #region Helpers

        private void ResequenceCameraSteps(Recipe recipe, string cameraId)
        {
            var cameraSteps = recipe.Steps
                .Where(s => s.CameraId == cameraId)
                .OrderBy(s => s.Sequence)
                .ToList();
            for (int i = 0; i < cameraSteps.Count; i++)
            {
                cameraSteps[i].Sequence = i + 1;
            }
        }

        private static string GetSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safeName = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
            return safeName.ToLowerInvariant().Replace(" ", "_");
        }

        /// <summary>
        /// 현재 레시피 설정 (외부에서 직접 설정 시 사용)
        /// </summary>
        public void SetCurrentRecipe(Recipe? recipe)
        {
            CurrentRecipe = recipe;
        }

        /// <summary>
        /// 현재 레시피 닫기
        /// </summary>
        public void CloseCurrentRecipe()
        {
            CurrentRecipe = null;
        }

        #endregion
    }
}
