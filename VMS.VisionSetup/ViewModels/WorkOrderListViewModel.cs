using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Data;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;

namespace VMS.VisionSetup.ViewModels
{
    public partial class WorkOrderListViewModel : ObservableObject
    {
        private readonly WorkOrderClient _client;

        public WorkOrderListViewModel(WorkOrderClient client)
        {
            _client = client;
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            SelectCommand = new RelayCommand(Select, () => SelectedItem != null);

            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterPredicate;

            // 첫 로드
            _ = RefreshAsync();
        }

        public ObservableCollection<WorkOrderDto> Items { get; } = new();
        public ICollectionView ItemsView { get; }

        [ObservableProperty] private WorkOrderDto? _selectedItem;
        [ObservableProperty] private string _statusFilter = "활성";  // 활성/전체/Planned/InProgress/Completed
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusMessage = "";

        public IAsyncRelayCommand RefreshCommand { get; }
        public IRelayCommand SelectCommand { get; }

        public WorkOrderDto? Result { get; private set; }
        public event Action<bool>? Finished;  // true=selected, false=cancelled

        partial void OnSelectedItemChanged(WorkOrderDto? value) => SelectCommand.NotifyCanExecuteChanged();

        partial void OnStatusFilterChanged(string value) => ItemsView.Refresh();

        private bool FilterPredicate(object obj)
        {
            if (obj is not WorkOrderDto wo) return false;
            return StatusFilter switch
            {
                "전체" => true,
                "활성" => wo.Status is "Planned" or "InProgress",
                _ => string.Equals(wo.Status, StatusFilter, StringComparison.OrdinalIgnoreCase)
            };
        }

        private async Task RefreshAsync()
        {
            IsLoading = true;
            StatusMessage = "Web에서 작업지시 목록 가져오는 중...";
            try
            {
                Items.Clear();
                // 서버 측에서는 status 필터 사용 안 함 — 클라이언트 측에서 필터링
                var list = await _client.GetByClientAsync();
                foreach (var w in list) Items.Add(w);
                StatusMessage = $"{list.Count}건";
            }
            catch (Exception ex)
            {
                StatusMessage = $"로드 실패: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void Select()
        {
            if (SelectedItem == null) return;
            Result = SelectedItem;
            Finished?.Invoke(true);
        }
    }
}
