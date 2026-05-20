using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.BatchTest
{
    public partial class ThresholdEditorWindow : Window
    {
        public ThresholdEditorWindow(IRecipeService recipeService, IVisionService visionService, VMS.VisionSetup.Models.Recipe recipe)
        {
            InitializeComponent();
            var vm = new ThresholdEditorViewModel(recipeService, visionService, recipe);
            vm.Saved += () => { DialogResult = true; Close(); };
            vm.CancelRequested += () => { DialogResult = false; Close(); };
            DataContext = vm;
        }
    }
}
