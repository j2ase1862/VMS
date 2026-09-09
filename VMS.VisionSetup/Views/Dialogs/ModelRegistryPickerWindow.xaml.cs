using System.Windows;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Dialogs
{
    /// <summary>
    /// MLOps 모델 레지스트리에서 참조(<c>model://…</c>)를 고르는 창.
    /// 상태·조회·검증은 <see cref="ModelRegistryPickerViewModel"/> 이 담당하고, 이 코드는 창을 열고 닫는 것만 한다.
    /// 여는 곳은 <see cref="Services.DialogService.ShowModelRegistryPickerDialog"/> — 설정 확인과 안내도 거기서.
    /// </summary>
    public partial class ModelRegistryPickerWindow : Window
    {
        private readonly ModelRegistryPickerViewModel _viewModel;

        public ModelRegistryPickerWindow(ModelRegistryPickerViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += (_, ok) => DialogResult = ok;
            Loaded += async (_, _) => await viewModel.LoadModelsCommand.ExecuteAsync(null);
        }

        /// <summary>[선택] 으로 닫혔을 때의 참조 문자열</summary>
        public string? SelectedReference => _viewModel.SelectedReference;
    }
}
