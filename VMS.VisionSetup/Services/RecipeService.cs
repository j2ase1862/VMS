using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.DeepLearning;
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
    public class RecipeService : IRecipeService
    {
        private static readonly Lazy<RecipeService> _instance = new(() => new RecipeService());
        public static RecipeService Instance => _instance.Value;

        private readonly string _appDataPath;
        private readonly string _recipeFolderPath;
        private Recipe? _currentRecipe;

        // AppSetup에서 로드한 기본 로봇 설정
        private string _defaultRobotIpAddress = "192.168.1.100";
        private int _defaultRobotPort = 30003;
        private EulerConvention _defaultEulerConvention = EulerConvention.UR_RotationVector;

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
                // 다른 레시피로 교체되면 파일 경로 추적도 초기화 — LoadRecipe 가 로드 직후
                // 다시 채운다. 새 레시피(파일 없음)는 첫 저장에서 이름 기반 경로가 기록됨.
                if (!ReferenceEquals(_currentRecipe, value))
                    CurrentRecipeFilePath = null;

                _currentRecipe = value;
                // 레시피 전환 시 캘리브레이션 메타데이터를 런타임 슬롯으로 동기화.
                // null 레시피이거나 캘리브레이션이 없으면 슬롯도 클리어.
                VisionService.Instance.CurrentCalibrationMetadata = value?.Calibration;
                CurrentRecipeChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// 현재 레시피가 로드된(또는 마지막으로 저장된) 파일 경로.
        /// 저장 시 이 경로로 되돌려 써야 VMS 의 파일 워처가 변경을 감지한다 —
        /// 이름 기반 새 파일로 저장하면 VMS 가 로드한 원본(recipe_&lt;GUID&gt;.json 등)과
        /// 파일명이 달라 수정이 영원히 반영되지 않고 중복 레시피만 늘어난다 (2026-08-19 현장).
        /// </summary>
        public string? CurrentRecipeFilePath { get; private set; }

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
                    CurrentRecipeFilePath = Path.GetFullPath(filePath);
                    // 레시피에 포함된 모든 DL 도구의 ONNX 모델을 Step Load 이전에 미리 백그라운드 워밍업.
                    // 사용자가 Step/Image/Run 조작을 하는 동안 엔진이 준비되므로 첫 Run 지연이 크게 줄어든다.
                    PrefetchDeepLearningModels(recipe);
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
        /// 레시피 내 모든 InspectionStep → Tool 중 DetectionTool/ClassifyTool/AnomalyTool의
        /// ModelPath를 추출해 OnnxEngineCache에 비동기 프리페치 요청.
        /// </summary>
        private static void PrefetchDeepLearningModels(Recipe recipe)
        {
            foreach (var step in recipe.Steps)
            {
                foreach (var tool in step.Tools)
                {
                    if (!tool.Parameters.TryGetValue("ModelPath", out var mpObj))
                        continue;
                    var modelPath = CoerceString(mpObj);
                    if (string.IsNullOrEmpty(modelPath)) continue;

                    switch (tool.ToolType)
                    {
                        case "DetectionTool":
                            OnnxEngineCache.PrefetchYolo(modelPath,
                                CoerceInt(tool.Parameters, "InputSize", 640));
                            break;
                        case "ClassifyTool":
                            OnnxEngineCache.PrefetchClassifier(modelPath,
                                CoerceInt(tool.Parameters, "InputWidth", 224),
                                CoerceInt(tool.Parameters, "InputHeight", 224),
                                CoerceBool(tool.Parameters, "UseImageNetNormalization", true));
                            break;
                        case "AnomalyTool":
                            OnnxEngineCache.PrefetchAnomaly(modelPath,
                                CoerceInt(tool.Parameters, "InputSize", 224));
                            break;
                        case "SegmentationTool":
                            OnnxEngineCache.PrefetchSegmentation(modelPath,
                                CoerceInt(tool.Parameters, "InputSize", 512),
                                CoerceBool(tool.Parameters, "UseImageNetNormalization", true));
                            break;
                        case "YoloSegTool":
                            OnnxEngineCache.PrefetchYoloSeg(modelPath,
                                CoerceInt(tool.Parameters, "InputSize", 640));
                            break;
                    }
                }
            }
        }

        // JSON 역직렬화는 Parameters 값을 JsonElement 또는 박싱된 원시 타입으로 남긴다.
        // 두 경로를 모두 안전하게 처리한다.
        private static string CoerceString(object? v) => v switch
        {
            null => string.Empty,
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString() ?? string.Empty,
            _ => v.ToString() ?? string.Empty
        };

        private static int CoerceInt(Dictionary<string, object> p, string key, int defaultValue)
        {
            if (!p.TryGetValue(key, out var v) || v == null) return defaultValue;
            return v switch
            {
                int i => i,
                long l => (int)l,
                JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
                _ => int.TryParse(v.ToString(), out var parsed) ? parsed : defaultValue
            };
        }

        private static bool CoerceBool(Dictionary<string, object> p, string key, bool defaultValue)
        {
            if (!p.TryGetValue(key, out var v) || v == null) return defaultValue;
            return v switch
            {
                bool b => b,
                JsonElement je when je.ValueKind == JsonValueKind.True => true,
                JsonElement je when je.ValueKind == JsonValueKind.False => false,
                _ => bool.TryParse(v.ToString(), out var parsed) ? parsed : defaultValue
            };
        }

        /// <summary>
        /// 파일에서 레시피를 읽기만 한다 — CurrentRecipe 를 바꾸지 않음.
        /// (Web 등록/이름 연결 등 백그라운드 메타 갱신용. LoadRecipe 는 로드 부작용 있음)
        /// </summary>
        public Recipe? ReadRecipeFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Recipe>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 파일 읽기 실패: {ex.Message}");
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

                // 파일 경로가 없으면: 현재 레시피는 로드된 원본 경로로 되돌려 쓴다 —
                // VMS 가 recipe_<GUID>.json / web_<id>.json 을 로드한 상태에서 이름 기반
                // 새 파일로 저장하면 워처 경로 필터에 걸려 수정이 영원히 미반영 (2026-08-19 현장).
                // 원본 경로가 없을 때만(새 레시피) 이름 기반 기본 경로.
                if (string.IsNullOrEmpty(filePath))
                {
                    if (ReferenceEquals(recipe, _currentRecipe) && !string.IsNullOrEmpty(CurrentRecipeFilePath))
                    {
                        filePath = CurrentRecipeFilePath;
                    }
                    else
                    {
                        var safeFileName = GetSafeFileName(recipe.Name);
                        filePath = Path.Combine(_recipeFolderPath, $"recipe_{safeFileName}.json");
                    }
                }

                var json = JsonSerializer.Serialize(recipe, JsonOptions);
                File.WriteAllText(filePath, json);

                // 현재 레시피 저장이면 이후 저장도 같은 파일로 가도록 경로 기록
                if (ReferenceEquals(recipe, _currentRecipe))
                    CurrentRecipeFilePath = Path.GetFullPath(filePath);

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
        /// AppSetup에서 로드한 기본 로봇 설정을 적용
        /// </summary>
        public void SetRobotDefaults(string ipAddress, int port, EulerConvention convention)
        {
            _defaultRobotIpAddress = ipAddress;
            _defaultRobotPort = port;
            _defaultEulerConvention = convention;
        }

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
                Criteria = new PassFailCriteria { RequireAllToolsPass = true },
                RobotIpAddress = _defaultRobotIpAddress,
                RobotPort = _defaultRobotPort,
                EulerConvention = _defaultEulerConvention
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
                                ToolCount = recipe.Steps.Sum(s => s.Tools.Count),
                                WebRecipeId = recipe.WebRecipeId
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
        /// 레시피 복제 — 파일을 읽어 새 ID·이름의 별도 파일로 저장한다 (CurrentRecipe 불변).
        /// WebRecipeId 는 비운다: Web 원장은 이름 기준 멱등이라 복사본은 새 이름으로
        /// 별도 등록되어야 하며, ID 를 물려받으면 두 로컬 레시피가 같은 Web 레시피에 얽힌다.
        /// 스텝/툴 ID 도 재발급해 StepPoseStore 등 ID 기반 런타임 상태가 원본과 섞이지 않게 한다.
        /// </summary>
        public Recipe? DuplicateRecipe(string filePath)
        {
            try
            {
                var recipe = LoadRecipeFromPath(filePath);
                if (recipe == null) return null;

                var existingNames = GetRecipeList()
                    .Select(r => r.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                recipe.Id = Guid.NewGuid().ToString();
                recipe.Name = MakeUniqueName($"{recipe.Name} - 복사본", existingNames);
                recipe.WebRecipeId = null;
                recipe.CreatedAt = DateTime.UtcNow;
                recipe.ModifiedAt = DateTime.UtcNow;

                foreach (var step in recipe.Steps)
                    RegenerateStepIds(step);

                var safeFileName = GetSafeFileName(recipe.Name);
                var newPath = Path.Combine(_recipeFolderPath, $"recipe_{safeFileName}.json");
                int n = 2;
                while (File.Exists(newPath))
                    newPath = Path.Combine(_recipeFolderPath, $"recipe_{safeFileName}_{n++}.json");

                return SaveRecipe(recipe, newPath) ? recipe : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"레시피 복제 실패: {ex.Message}");
                return null;
            }
        }

        private static string MakeUniqueName(string baseName, ISet<string> existingNames)
        {
            if (!existingNames.Contains(baseName)) return baseName;
            int n = 2;
            while (existingNames.Contains($"{baseName} ({n})")) n++;
            return $"{baseName} ({n})";
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

        /// <summary>
        /// 스텝 복제 — JSON 왕복 깊은 복사 후 스텝/툴 ID 재발급, 원본 바로 뒤 순번으로 삽입.
        /// 이름·순번 표시는 호출자가 StepNaming.RecomputeNames 로 재계산한다.
        /// </summary>
        public InspectionStep? DuplicateStep(Recipe recipe, string stepId)
        {
            if (recipe == null) return null;
            var source = recipe.Steps.FirstOrDefault(s => s.Id == stepId);
            if (source == null) return null;

            // JSON 왕복 깊은 복사 — 레시피 저장/로드와 동일 경로라 직렬화 대상 전부가 복사되고,
            // [JsonIgnore] 런타임 계산 속성만 제외된다
            var json = JsonSerializer.Serialize(source, JsonOptions);
            var copy = JsonSerializer.Deserialize<InspectionStep>(json, JsonOptions);
            if (copy == null) return null;

            RegenerateStepIds(copy);
            AddStep(recipe, copy);                            // 같은 카메라 그룹 끝에 추가
            MoveStep(recipe, copy.Id, source.Sequence + 1);   // 원본 바로 뒤로 이동
            return copy;
        }

        /// <summary>스텝·툴 ID 재발급 + 툴 연결(SourceToolId) 재매핑 — 복제 공용.</summary>
        private static void RegenerateStepIds(InspectionStep step)
        {
            step.Id = Guid.NewGuid().ToString();

            var idMap = new Dictionary<string, string>();
            foreach (var tool in step.Tools)
            {
                var newId = Guid.NewGuid().ToString();
                idMap[tool.Id] = newId;
                tool.Id = newId;
            }

            foreach (var tool in step.Tools)
                foreach (var conn in tool.Connections)
                    if (idMap.TryGetValue(conn.SourceToolId, out var mapped))
                        conn.SourceToolId = mapped;
        }

        /// <summary>
        /// 로봇이 보내온 노드 번호에 해당하는 스텝을 찾는다.
        /// RobotNodeIndex 가 명시된 스텝이 우선이고, 없으면 카메라별 순번(Sequence)으로
        /// 폴백한다. cameraId 를 주면 해당 카메라의 스텝으로 한정한다.
        /// </summary>
        public InspectionStep? FindStepByRobotNode(Recipe? recipe, int nodeIndex, string? cameraId = null)
        {
            recipe ??= CurrentRecipe;
            if (recipe == null) return null;

            var candidates = (cameraId == null
                ? recipe.Steps
                : recipe.Steps.Where(s => s.CameraId == cameraId)).ToList();

            var explicitMatch = candidates
                .Where(s => s.RobotNodeIndex == nodeIndex)
                .OrderBy(s => s.Sequence)
                .FirstOrDefault();
            if (explicitMatch != null) return explicitMatch;

            return candidates
                .Where(s => s.RobotNodeIndex == null && s.Sequence == nodeIndex)
                .OrderBy(s => s.Sequence)
                .FirstOrDefault();
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
