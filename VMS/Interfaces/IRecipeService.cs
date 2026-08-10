using System.Collections.Generic;
using VMS.Models;

namespace VMS.Interfaces
{
    public interface IRecipeService
    {
        Recipe? CurrentRecipe { get; }
        string RecipesDirectory { get; }
        Recipe? LoadRecipe(string filePath);
        /// <summary>
        /// 레시피 저장. <paramref name="setAsCurrent"/> false 는 백그라운드 저장
        /// (Web stub 동기화 등)이 CurrentRecipe 를 교체하지 않도록 하는 경로.
        /// </summary>
        bool SaveRecipe(Recipe recipe, string? filePath = null, bool setAsCurrent = true);
        List<RecipeInfo> GetRecipeList();
        Recipe CreateNewRecipe(string name = "New Recipe");
        bool DeleteRecipe(string id);
        bool ExportRecipe(Recipe recipe, string exportPath);
        Recipe? ImportRecipe(string importPath);
        void SetCurrentRecipe(Recipe? recipe);
    }
}
