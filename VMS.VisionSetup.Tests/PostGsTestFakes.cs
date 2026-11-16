using System;
using System.Collections.Generic;
using System.Linq;
using VMS.Camera.Models;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Tests
{
    /// <summary>GS 이후 수정(VS-) 시험용 대역 — 메모리만 쓰는 레시피 · 카메라 서비스.</summary>
    internal sealed class MemoryRecipeService : IRecipeService
    {
        private Recipe? _current;
        public Recipe? CurrentRecipe
        {
            get => _current;
            set { _current = value; CurrentRecipeChanged?.Invoke(this, value); }
        }
        public string RecipeFolderPath => string.Empty;
        public event EventHandler<Recipe?>? CurrentRecipeChanged;
        public int SaveCount { get; private set; }
        public Recipe? LoadRecipe(string filePath) => null;
        public Recipe? ReadRecipeFile(string filePath) => null;
        public bool SaveRecipe(Recipe recipe, string? filePath = null) { SaveCount++; return true; }
        public bool SaveCurrentRecipe(string? filePath = null) { SaveCount++; return true; }
        public Recipe CreateNewRecipe(string? name = null) => new() { Name = name ?? "New" };
        public bool DeleteRecipe(string filePath) => true;
        public List<RecipeInfo> GetRecipeList() => new();
        public bool ExportRecipe(Recipe recipe, string exportPath) => true;
        public Recipe? ImportRecipe(string importPath) => null;
        public Recipe? DuplicateRecipe(string filePath) => null;
        public bool RenameRecipe(string filePath, string newName) => false;
        public InspectionStep? AddStep(Recipe? recipe = null, string? cameraId = null)
        {
            recipe ??= CurrentRecipe;
            if (recipe == null) return null;
            var step = new InspectionStep { CameraId = cameraId ?? string.Empty };
            AddStep(recipe, step);
            return step;
        }
        public void AddStep(Recipe recipe, InspectionStep step)
        {
            step.Sequence = recipe.Steps.Count(s => s.CameraId == step.CameraId) + 1;
            recipe.Steps.Add(step);
        }
        public bool RemoveStep(Recipe? recipe, string stepId) => false;
        public bool MoveStep(Recipe? recipe, string stepId, int newSequence) => false;
        public InspectionStep? DuplicateStep(Recipe recipe, string stepId) => null;
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

    internal sealed class ListCameraService : ICameraService
    {
        private readonly List<CameraInfo> _cameras;
        public ListCameraService(params CameraInfo[] cameras) => _cameras = cameras.ToList();
        public List<CameraInfo> LoadCameraRegistry() => _cameras.ToList();
        public bool SaveCameraRegistry(List<CameraInfo>? cameras = null) => true;
        public bool AddCamera(CameraInfo camera) => true;
        public bool UpdateCamera(CameraInfo camera) => true;
        public bool RemoveCamera(string id) => true;
        public CameraInfo? GetCamera(string id) => _cameras.FirstOrDefault(c => c.Id == id);
        public List<CameraInfo> GetAllCameras() => _cameras.ToList();
        public List<CameraInfo> GetEnabledCameras() => _cameras.ToList();
        public CameraInfo CreateNewCamera() => new();
        public string GetAppDataFolderPath() => System.IO.Path.GetTempPath();
    }
}
