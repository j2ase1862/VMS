using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace VMS.VisionSetup.ViewModels
{
    public partial class RecipeListViewModel : ObservableObject
    {
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private ObservableCollection<RecipeInfo> _recipes = new();

        [ObservableProperty]
        private RecipeInfo? _selectedRecipe;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _hasSelection;

        private System.Collections.Generic.List<RecipeInfo> _allRecipes = new();

        public event EventHandler<RecipeInfo>? RecipeSelected;
        public event EventHandler<Recipe>? RecipeLoaded;

        public RecipeListViewModel(IRecipeService recipeService, IDialogService dialogService)
        {
            _recipeService = recipeService;
            _dialogService = dialogService;
            RefreshRecipeList();
        }

        public void RefreshRecipeList()
        {
            _allRecipes = _recipeService.GetRecipeList();
            ApplyFilter();
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedRecipeChanged(RecipeInfo? value)
        {
            HasSelection = value != null;
            if (value != null)
            {
                RecipeSelected?.Invoke(this, value);
            }
        }

        private void ApplyFilter()
        {
            var searchText = SearchText?.ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                Recipes = new ObservableCollection<RecipeInfo>(_allRecipes);
            }
            else
            {
                Recipes = new ObservableCollection<RecipeInfo>(_allRecipes.Where(r =>
                    r.Name.ToLowerInvariant().Contains(searchText) ||
                    r.Author.ToLowerInvariant().Contains(searchText) ||
                    r.Version.ToLowerInvariant().Contains(searchText)
                ));
            }
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
        }

        [RelayCommand]
        private void NewRecipe()
        {
            // The NewRecipeDialog is a UI concern; for now, we show it via a simple approach
            // This will be further refined when dialog is converted to XAML
            var dialog = new Views.Recipe.NewRecipeDialog();
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                var recipe = _recipeService.CreateNewRecipe(dialog.RecipeName);
                recipe.Description = dialog.RecipeDescription;
                recipe.Author = dialog.RecipeAuthor;

                _recipeService.SaveRecipe(recipe);
                RefreshRecipeList();

                RecipeLoaded?.Invoke(this, recipe);
            }
        }

        [RelayCommand]
        private void LoadRecipe()
        {
            if (SelectedRecipe == null) return;

            var recipe = _recipeService.LoadRecipe(SelectedRecipe.FilePath);
            if (recipe != null)
            {
                RecipeLoaded?.Invoke(this, recipe);
            }
            else
            {
                _dialogService.ShowError("레시피를 로드할 수 없습니다.", "Error");
            }
        }

        [RelayCommand]
        private void DeleteRecipe()
        {
            if (SelectedRecipe == null) return;

            if (_dialogService.ShowConfirmation(
                $"'{SelectedRecipe.Name}' 레시피를 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                "Delete Recipe"))
            {
                if (_recipeService.DeleteRecipe(SelectedRecipe.FilePath))
                {
                    RefreshRecipeList();
                }
                else
                {
                    _dialogService.ShowError("레시피 삭제에 실패했습니다.", "Error");
                }
            }
        }

        [RelayCommand]
        private void ImportRecipe()
        {
            var filePath = _dialogService.ShowOpenFileDialog(
                "Import Recipe",
                "Recipe Files (*.json)|*.json|All Files (*.*)|*.*");

            if (filePath != null)
            {
                var recipe = _recipeService.ImportRecipe(filePath);
                if (recipe != null)
                {
                    RefreshRecipeList();
                    _dialogService.ShowInformation(
                        $"레시피 '{recipe.Name}'를 성공적으로 가져왔습니다.",
                        "Import Successful");
                }
                else
                {
                    _dialogService.ShowError("레시피 가져오기에 실패했습니다.", "Import Failed");
                }
            }
        }

        [RelayCommand]
        private void ExportRecipe()
        {
            if (SelectedRecipe == null) return;

            var recipe = _recipeService.LoadRecipe(SelectedRecipe.FilePath);
            if (recipe == null)
            {
                _dialogService.ShowError("레시피를 로드할 수 없습니다.", "Error");
                return;
            }

            var filePath = _dialogService.ShowSaveFileDialog(
                "Recipe Files (*.json)|*.json",
                ".json",
                $"recipe_{recipe.Name.Replace(" ", "_").ToLower()}");

            if (filePath != null)
            {
                if (_recipeService.ExportRecipe(recipe, filePath))
                {
                    _dialogService.ShowInformation(
                        $"레시피를 '{filePath}'에 내보냈습니다.",
                        "Export Successful");
                }
                else
                {
                    _dialogService.ShowError("레시피 내보내기에 실패했습니다.", "Export Failed");
                }
            }
        }

        [RelayCommand]
        private void OpenFolder()
        {
            try
            {
                var folderPath = _recipeService.RecipeFolderPath;
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"폴더를 열 수 없습니다: {ex.Message}", "Error");
            }
        }
    }
}
