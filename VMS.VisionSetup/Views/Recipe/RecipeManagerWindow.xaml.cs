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

        public RecipeManagerWindow(IRecipeService recipeService, ICameraService cameraService, IDialogService dialogService)
        {
            InitializeComponent();

            _recipeService = recipeService;

            // Create ViewModels with injected services
            _listViewModel = new RecipeListViewModel(recipeService, dialogService);
            _editorViewModel = new RecipeEditorViewModel(recipeService, cameraService, dialogService);

            // Set DataContext on child controls
            RecipeListPanel.DataContext = _listViewModel;
            RecipeEditorPanel.DataContext = _editorViewModel;

            // Wire up cross-communication
            _listViewModel.RecipeLoaded += OnRecipeLoaded;
            _editorViewModel.RecipeSaved += OnRecipeSaved;
        }

        private void OnRecipeLoaded(object? sender, Models.Recipe recipe)
        {
            // Load into editor view
            RecipeEditorPanel.LoadRecipe(recipe);

            // Set as current recipe in service
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
