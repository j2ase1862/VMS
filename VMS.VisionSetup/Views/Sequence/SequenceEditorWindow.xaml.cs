using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Sequence
{
    public partial class SequenceEditorWindow : Window
    {
        public SequenceEditorWindow(IRecipeService recipeService, ICameraService cameraService, IDialogService dialogService)
        {
            InitializeComponent();
            DataContext = new SequenceEditorViewModel(recipeService, cameraService, dialogService);
            Closed += OnClosed;
        }

        private async void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is SequenceEditorViewModel vm)
            {
                await vm.DisconnectPlcAsync();
            }
        }
    }
}
