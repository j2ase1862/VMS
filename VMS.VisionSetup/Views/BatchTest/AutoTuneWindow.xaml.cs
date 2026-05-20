using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.BatchTest
{
    public partial class AutoTuneWindow : Window
    {
        public AutoTuneWindow(IVisionService visionService, IRecipeService recipeService,
                              VMS.VisionSetup.Models.Recipe? recipe, string initialImageFolder)
        {
            InitializeComponent();
            DataContext = new AutoTuneViewModel(visionService, recipeService, recipe, initialImageFolder);
        }
    }
}
