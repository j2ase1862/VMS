using System;
using System.Collections.Generic;
using VMS.Models;

namespace VMS.Interfaces
{
    public interface IRecipeService
    {
        Recipe? CurrentRecipe { get; }
        string RecipesDirectory { get; }

        /// <summary>
        /// 외부 프로세스(VisionSetup 등)가 Recipes 폴더의 파일을 저장/변경했을 때 발생.
        /// 자기 저장은 제외. 워처 스레드에서 발생 — 구독측에서 UI 디스패치 필요.
        /// </summary>
        event Action<string>? ExternalRecipeFileChanged;

        /// <summary>Recipes 폴더 외부 변경 감시 시작 (idempotent).</summary>
        void StartWatchingRecipeFiles();
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
