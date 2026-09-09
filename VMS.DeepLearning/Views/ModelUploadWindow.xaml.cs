using System.Windows;
using System.Windows.Controls;
using VMS.Core.ViewModels;

namespace VMS.DeepLearning.Views
{
    /// <summary>
    /// 학습 결과를 MLOps 레지스트리에 올리는 창.
    ///
    /// <para>
    /// 코드 비하인드는 두 가지만 한다 — <see cref="PasswordBox"/> 의 값을 뷰모델에 넘기는 것과
    /// 창을 닫는 것. 비밀번호는 의존 속성이 아니라서 바인딩할 수 없어 여기를 거칠 수밖에 없다.
    /// 로그인·업로드는 전부 <see cref="ModelUploadViewModel"/> 안에 있다.
    /// </para>
    /// </summary>
    public partial class ModelUploadWindow : Window
    {
        private readonly ModelUploadViewModel _viewModel;

        public ModelUploadWindow(ModelUploadViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
            => _viewModel.Password = PasswordInput.Password;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
