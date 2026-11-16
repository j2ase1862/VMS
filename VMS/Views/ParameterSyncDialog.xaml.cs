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

            // 운전에 연결된 레시피만 캐시를 새로 받는다. 다른 레시피는 보기만 한다 — 이 창에서 고르는 것만으로
            // 검사 판정 기준값 · 결과 업로드 대상 Web 레시피가 바뀌면 안 된다 (VM-35).
            if (recipeId == _syncService.CurrentRecipeId)
            {
                await _syncService.LoadRecipeAsync(recipeId);
                ShowItems(_syncService.GetAll());

                txtSyncStatus.Text = _syncService.LastSyncedAt.HasValue
                    ? $"Synced: {_syncService.LastSyncedAt.Value:yyyy-MM-dd HH:mm:ss} (Recipe ID={recipeId})"
                    : "Not synced";
                return;
            }

            var items = await _syncService.FetchRecipeParametersAsync(recipeId);
            ShowItems(items ?? System.Array.Empty<RecipeParameterDto>());
            txtSyncStatus.Text = items == null
                ? $"불러오지 못했습니다 (Recipe ID={recipeId})"
                : $"보기 전용 (Recipe ID={recipeId}) — 검사에 쓰는 레시피 연결은 바뀌지 않습니다";
        }

        private void ShowItems(System.Collections.Generic.IReadOnlyCollection<RecipeParameterDto> items)
        {
            dgParameters.ItemsSource = items;
            txtItemCount.Text = $"{items.Count} parameters";

            // Web 동기화 응답은 규격 노출을 줄이려 분류를 보내지 않는다 — 늘 빈 칸이던 Category 열은
            // 값이 하나라도 있을 때만 보인다 (VM-36)
            colCategory.Visibility = items.Any(i => !string.IsNullOrWhiteSpace(i.Category))
                ? Visibility.Visible
                : Visibility.Collapsed;
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
