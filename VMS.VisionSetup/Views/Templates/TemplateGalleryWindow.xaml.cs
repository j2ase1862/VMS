using System.Windows;
using System.Windows.Input;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Templates
{
    /// <summary>
    /// 예제 템플릿 갤러리 다이얼로그.
    /// 코드비하인드는 다이얼로그 결과 변환과 더블클릭 제스처 연결만 담당 (로직은 ViewModel).
    /// </summary>
    public partial class TemplateGalleryWindow : Window
    {
        private readonly TemplateGalleryViewModel _viewModel;

        public TemplateGalleryWindow()
        {
            InitializeComponent();
            _viewModel = new TemplateGalleryViewModel();
            DataContext = _viewModel;
            _viewModel.CloseRequested += (_, create) => DialogResult = create;
        }

        public Models.RecipeTemplate? SelectedTemplate => _viewModel.SelectedTemplate;

        private void TemplateList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.CreateCommand.CanExecute(null))
                _viewModel.CreateCommand.Execute(null);
        }
    }
}
