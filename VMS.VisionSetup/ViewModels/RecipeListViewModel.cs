using VMS.Core.Interfaces;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace VMS.VisionSetup.ViewModels
{
    public partial class RecipeListViewModel : ObservableObject
    {
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly IParameterSyncService? _parameterSyncService;

        [ObservableProperty]
        private ObservableCollection<RecipeInfo> _recipes = new();

        [ObservableProperty]
        private RecipeInfo? _selectedRecipe;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _hasSelection;

        private List<RecipeInfo> _allRecipes = new();

        public event EventHandler<RecipeInfo>? RecipeSelected;
        public event EventHandler<Recipe>? RecipeLoaded;

        public RecipeListViewModel(IRecipeService recipeService, IDialogService dialogService,
            IParameterSyncService? parameterSyncService = null)
        {
            _recipeService = recipeService;
            _dialogService = dialogService;
            _parameterSyncService = parameterSyncService;

            // 60초 폴링이 Web 레시피 변경을 감지하면 창이 열려 있는 동안에도 자동 갱신.
            // 기존에는 생성 시 1회만 동기화라 창을 닫았다 열어야 보였다 (2026-08-10).
            if (_parameterSyncService != null)
                _parameterSyncService.RecipeListChanged += OnWebRecipeListChanged;

            RefreshRecipeList();
        }

        // 폴링 타이머 스레드에서 호출됨 — UI 반영은 SyncWebRecipesToLocalAsync 내부에서 dispatch
        private void OnWebRecipeListChanged(List<VMS.Core.Models.ParameterSync.RecipeSummaryDto> recipes)
            => _ = SyncWebRecipesToLocalAsync();

        /// <summary>
        /// 창이 닫힐 때 호출 (RecipeManagerWindow.Closed). VM 은 창마다 새로 생성되므로
        /// 구독을 해제하지 않으면 폴링 서비스에 죽은 구독이 누적된다.
        /// </summary>
        public void Detach()
        {
            if (_parameterSyncService != null)
                _parameterSyncService.RecipeListChanged -= OnWebRecipeListChanged;
        }

        public void RefreshRecipeList()
        {
            _allRecipes = _recipeService.GetRecipeList();
            ApplyFilter();

            // Web에서 추가된 레시피를 로컬에 동기화
            if (_parameterSyncService != null)
            {
                _ = SyncWebRecipesToLocalAsync();
            }
        }

        private async Task SyncWebRecipesToLocalAsync()
        {
            try
            {
                await _parameterSyncService!.SyncRecipesAsync();
                var webRecipes = _parameterSyncService.Recipes;
                var recipeFolderPath = _recipeService.RecipeFolderPath;
                var localRecipes = _recipeService.GetRecipeList();
                var localNames = localRecipes.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool anyNew = false;

                foreach (var web in webRecipes)
                {
                    var webFilePath = Path.Combine(recipeFolderPath, $"web_{web.Id}.json");
                    if (!File.Exists(webFilePath) && !localNames.Contains(web.Name))
                    {
                        var recipe = new Recipe
                        {
                            Id = $"web_{web.Id}",
                            Name = web.Name,
                            Description = web.Description,
                            CreatedAt = DateTime.UtcNow,
                            ModifiedAt = DateTime.UtcNow,
                            Author = "Web"
                        };
                        _recipeService.SaveRecipe(recipe, webFilePath);
                        anyNew = true;
                    }
                }

                // Web 파일 정리: 서버에서 삭제된 레시피 또는 로컬에 동일 이름이 있는 중복 파일 제거
                var webIds = webRecipes.Select(r => r.Id).ToHashSet();
                var webNameById = webRecipes.ToDictionary(r => r.Id, r => r.Name);
                var nonWebLocalNames = localRecipes
                    .Where(r => !r.FilePath.Contains("web_"))
                    .Select(r => r.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var webFiles = Directory.GetFiles(recipeFolderPath, "web_*.json");
                foreach (var file in webFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (fileName.StartsWith("web_") && int.TryParse(fileName.Substring(4), out var fileWebId))
                    {
                        // 서버에서 삭제되었거나, 로컬에 동일 이름 레시피가 이미 존재하면 삭제
                        bool deletedOnServer = !webIds.Contains(fileWebId);
                        bool duplicateName = webNameById.TryGetValue(fileWebId, out var webName)
                            && nonWebLocalNames.Contains(webName);
                        if (deletedOnServer || duplicateName)
                        {
                            File.Delete(file);
                            anyNew = true;
                        }
                    }
                }

                if (anyNew)
                {
                    void Reload()
                    {
                        _allRecipes = _recipeService.GetRecipeList();
                        ApplyFilter();
                    }
                    // 헤드리스(테스트) 환경엔 Application 이 없다
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher == null) Reload();
                    else dispatcher.Invoke(Reload);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecipeList] Web recipe sync failed: {ex.Message}");
            }
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
