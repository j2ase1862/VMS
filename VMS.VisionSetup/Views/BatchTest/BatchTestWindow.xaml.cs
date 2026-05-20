using System.Windows;
using System.Windows.Input;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Services.BatchTesting;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.BatchTest
{
    public partial class BatchTestWindow : Window
    {
        public BatchTestWindow(IVisionService visionService, IRecipeService recipeService, ICameraService cameraService)
        {
            InitializeComponent();
            DataContext = new BatchTestViewModel(visionService, recipeService, cameraService);
            Loaded += BatchTestWindow_Loaded;
        }

        private async void BatchTestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= BatchTestWindow_Loaded;
            if (DataContext is BatchTestViewModel vm)
            {
                await vm.InitializeAsync();
            }
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is BatchTestViewModel vm &&
                ResultsGrid.SelectedItem is BatchImageResult r)
            {
                // 인-앱 Failure Browser로 오픈 (좌우 split, 키보드 네비 지원)
                vm.OpenBrowserAt(r);
            }
        }
    }
}
