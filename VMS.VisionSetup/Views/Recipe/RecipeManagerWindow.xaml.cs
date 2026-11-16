using VMS.Core.Interfaces;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels;
using System;
using System.Windows;

namespace VMS.VisionSetup.Views.Recipe
{
    /// <summary>
    /// RecipeManagerWindow.xaml 코드 비하인드
    /// </summary>
    public partial class RecipeManagerWindow : Window
    {
        private readonly IRecipeService _recipeService;
        private readonly RecipeListViewModel _listViewModel;
        private readonly RecipeEditorViewModel _editorViewModel;

        public event EventHandler<Models.Recipe>? RecipeLoaded;

        public RecipeManagerWindow(IRecipeService recipeService, ICameraService cameraService,
            IDialogService dialogService, IParameterSyncService? parameterSyncService = null)
        {
            InitializeComponent();

            _recipeService = recipeService;

            // Create ViewModels with injected services
            _listViewModel = new RecipeListViewModel(recipeService, dialogService, parameterSyncService);
            _editorViewModel = new RecipeEditorViewModel(recipeService, cameraService, dialogService);

            // Set DataContext on child controls
            RecipeListPanel.DataContext = _listViewModel;
            RecipeEditorPanel.DataContext = _editorViewModel;

            // Wire up cross-communication
            _listViewModel.RecipeLoaded += OnRecipeLoaded;
            _editorViewModel.RecipeSaved += OnRecipeSaved;

            // 폴링 서비스의 RecipeListChanged 구독 해제 — VM 은 창마다 새로 생성됨
            Closed += (_, _) => _listViewModel.Detach();
        }

        private void OnRecipeLoaded(object? sender, Models.Recipe recipe)
        {
            // Load into editor view
            RecipeEditorPanel.LoadRecipe(recipe);

            // Set as current recipe in service — [Load] · [New] 는 이미 서비스의 현재 레시피로
            // 올려 두었다. 같은 레시피를 다시 넣으면 CurrentRecipeChanged 가 두 번 나가
            // 미등록 카메라 재연결 창 · 경고가 두 번 뜬다 (VS-06).
            if (!ReferenceEquals(_recipeService.CurrentRecipe, recipe))
                _recipeService.SetCurrentRecipe(recipe);

            // Notify parent
            RecipeLoaded?.Invoke(this, recipe);
        }

        private void OnRecipeSaved(object? sender, EventArgs e)
        {
            // Refresh recipe list after save
            _listViewModel.RefreshRecipeList();
        }
    }
}
