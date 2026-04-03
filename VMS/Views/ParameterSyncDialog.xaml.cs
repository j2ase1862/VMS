using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VMS.Core.Interfaces;
using VMS.Core.Models.ParameterSync;

namespace VMS.Views
{
    public partial class ParameterSyncDialog : Window
    {
        private readonly IParameterSyncService _syncService;
        private bool _initializing = true;

        public ParameterSyncDialog(IParameterSyncService syncService)
        {
            InitializeComponent();
            _syncService = syncService;
            _ = InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            txtSyncStatus.Text = "Loading recipes...";

            await _syncService.SyncRecipesAsync();

            var recipes = _syncService.Recipes;
            cboRecipes.ItemsSource = recipes;

            var currentRecipeId = _syncService.CurrentRecipeId;
            var selected = recipes.FirstOrDefault(r => r.Id == currentRecipeId)
                        ?? recipes.FirstOrDefault();

            _initializing = true;
            cboRecipes.SelectedItem = selected;
            _initializing = false;

            if (selected != null)
                await LoadRecipeParameters(selected.Id);
            else
                txtSyncStatus.Text = "No recipes found";
        }

        private async System.Threading.Tasks.Task LoadRecipeParameters(int recipeId)
        {
            txtSyncStatus.Text = "Loading...";

            await _syncService.LoadRecipeAsync(recipeId);
            RefreshGrid();

            txtSyncStatus.Text = _syncService.LastSyncedAt.HasValue
                ? $"Synced: {_syncService.LastSyncedAt.Value:yyyy-MM-dd HH:mm:ss} (Recipe ID={recipeId})"
                : "Not synced";
        }

        private void RefreshGrid()
        {
            var items = _syncService.GetAll();
            dgParameters.ItemsSource = items;
            txtItemCount.Text = $"{items.Count} parameters";
        }

        private async void CboRecipes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;

            var selected = cboRecipes.SelectedItem as RecipeSummaryDto;
            if (selected == null) return;

            await LoadRecipeParameters(selected.Id);
        }

        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            btnSync.IsEnabled = false;
            txtSyncStatus.Text = "Refreshing recipes...";

            // 레시피 목록 새로고침 (Web에서 추가된 레시피 반영)
            await _syncService.SyncRecipesAsync();
            var recipes = _syncService.Recipes;
            var previousSelected = cboRecipes.SelectedItem as RecipeSummaryDto;

            _initializing = true;
            cboRecipes.ItemsSource = recipes;

            // 이전 선택 유지 또는 첫 번째 항목 선택
            var reselect = recipes.FirstOrDefault(r => r.Id == previousSelected?.Id)
                        ?? recipes.FirstOrDefault();
            cboRecipes.SelectedItem = reselect;
            _initializing = false;

            if (reselect != null)
                await LoadRecipeParameters(reselect.Id);
            else
                txtSyncStatus.Text = "No recipes found";

            btnSync.IsEnabled = true;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
