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
            var selected = cboRecipes.SelectedItem as RecipeSummaryDto;
            if (selected == null) return;

            btnSync.IsEnabled = false;
            await LoadRecipeParameters(selected.Id);
            btnSync.IsEnabled = true;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
