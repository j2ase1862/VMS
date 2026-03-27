using System.IO;
using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create services
            IVisionService visionService = VisionService.Instance;
            IRecipeService recipeService = RecipeService.Instance;
            ICameraService cameraService = CameraService.Instance;
            IDialogService dialogService = new DialogService(cameraService, recipeService);

            // If recipe file path is passed as argument, pre-load it
            if (e.Args.Length > 0)
            {
                var recipePath = e.Args[0];
                if (File.Exists(recipePath))
                {
                    recipeService.LoadRecipe(recipePath);
                }
            }

            // Create MainViewModel with DI
            var viewModel = new MainViewModel(
                visionService,
                recipeService,
                cameraService,
                dialogService,
                () => Shutdown());

            // Create and show main window
            var mainView = new MainView();
            mainView.DataContext = viewModel;
            mainView.Show();
        }
    }
}
