using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using System;
using System.Windows;

namespace VMS.VisionSetup.Views.Recipe
{
    /// <summary>
    /// RecipeManagerWindow.xaml 코드 비하인드
    /// </summary>
    public partial class RecipeManagerWindow : Window
    {
        public event EventHandler<Models.Recipe>? RecipeLoaded;

        public RecipeManagerWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 레시피 목록에서 레시피가 로드됨
        /// </summary>
        private void RecipeListPanel_RecipeLoaded(object? sender, Models.Recipe recipe)
        {
            // 레시피 에디터에 로드
            RecipeEditorPanel.LoadRecipe(recipe);

            // 서비스에 현재 레시피 설정
            RecipeService.Instance.CurrentRecipe = recipe;

            // 부모 창에 이벤트 전달
            RecipeLoaded?.Invoke(this, recipe);
        }

        /// <summary>
        /// 레시피 목록에서 레시피가 선택됨 (미리보기용)
        /// </summary>
        private void RecipeListPanel_RecipeSelected(object? sender, RecipeInfo recipeInfo)
        {
            // 선택된 레시피 정보 표시 (필요 시 구현)
        }

        /// <summary>
        /// 레시피 에디터에서 레시피가 저장됨
        /// </summary>
        private void RecipeEditorPanel_RecipeSaved(object? sender, EventArgs e)
        {
            // 레시피 목록 새로고침
            RecipeListPanel.RefreshRecipeList();
        }
    }
}
