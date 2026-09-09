using System.Windows;
using Microsoft.Win32;
using VMS.Core.ViewModels;

namespace VMS.DeepLearning.Views
{
    /// <summary>
    /// 웹에서 라벨링한 데이터셋을 받는 창.
    ///
    /// <para>
    /// 코드 비하인드는 화면 장치만 다룬다 — <see cref="System.Windows.Controls.PasswordBox"/> 값 넘기기,
    /// 폴더 고르기, 닫기. 로그인·목록·내려받기는 전부 <see cref="DatasetDownloadViewModel"/> 안에 있다.
    /// </para>
    /// </summary>
    public partial class DatasetDownloadWindow : Window
    {
        private readonly DatasetDownloadViewModel _viewModel;

        public DatasetDownloadWindow(DatasetDownloadViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
            => _viewModel.Password = PasswordInput.Password;

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "데이터셋을 풀어 넣을 폴더" };
            if (dialog.ShowDialog() == true) _viewModel.TargetDirectory = dialog.FolderName;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
