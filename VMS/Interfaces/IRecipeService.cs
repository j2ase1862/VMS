using System.Collections.Generic;
using VMS.Models;

namespace VMS.Interfaces
{
    public interface IRecipeService
    {
        Recipe? CurrentRecipe { get; }
        string RecipesDirectory { get; }
        Recipe? LoadRecipe(string filePath);
        bool SaveRecipe(Recipe recipe, string? filePath = null);
        List<RecipeInfo> GetRecipeList();
        Recipe CreateNewRecipe(string name = "New Recipe");
        bool DeleteRecipe(string id);
        bool ExportRecipe(Recipe recipe, string exportPath);
        Recipe? ImportRecipe(string importPath);
        void SetCurrentRecipe(Recipe? recipe);
    }
}
