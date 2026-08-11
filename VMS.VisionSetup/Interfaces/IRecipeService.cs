using System;
using System.Collections.Generic;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Interfaces
{
    public interface IRecipeService
    {
        Recipe? CurrentRecipe { get; set; }
        string RecipeFolderPath { get; }
        event EventHandler<Recipe?>? CurrentRecipeChanged;

        Recipe? LoadRecipe(string filePath);
        /// <summary>파일에서 레시피를 읽기만 — CurrentRecipe 불변 (백그라운드 메타 갱신용).</summary>
        Recipe? ReadRecipeFile(string filePath);
        bool SaveRecipe(Recipe recipe, string? filePath = null);
        bool SaveCurrentRecipe(string? filePath = null);
        Recipe CreateNewRecipe(string? name = null);
        bool DeleteRecipe(string filePath);
        List<RecipeInfo> GetRecipeList();
        bool ExportRecipe(Recipe recipe, string exportPath);
        Recipe? ImportRecipe(string importPath);
        InspectionStep? AddStep(Recipe? recipe = null, string? cameraId = null);
        void AddStep(Recipe recipe, InspectionStep step);
        bool RemoveStep(Recipe? recipe, string stepId);
        bool MoveStep(Recipe? recipe, string stepId, int newSequence);
        InspectionStep? FindStepByRobotNode(Recipe? recipe, int nodeIndex, string? cameraId = null);
        bool AddToolToStep(InspectionStep step, ToolConfig tool);
        bool AddToolToStep(Recipe recipe, string stepId, ToolConfig tool);
        bool AddToolToStep(InspectionStep step, VisionToolBase tool);
        bool RemoveToolFromStep(InspectionStep step, string toolId);
        bool RemoveToolFromStep(Recipe recipe, string stepId, string toolId);
        List<VisionToolBase> GetToolsFromRecipe(Recipe? recipe = null);
        List<VisionToolBase> GetToolsFromStep(InspectionStep step);
        void SetCurrentRecipe(Recipe? recipe);
        void CloseCurrentRecipe();
    }
}
