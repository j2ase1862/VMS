using System.Collections.Generic;
using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Sequence
{
    public partial class SequenceEditorWindow : Window
    {
        public SequenceEditorWindow(
            IRecipeService recipeService,
            ICameraService cameraService,
            IDialogService dialogService,
            IEnumerable<SequenceDeviceEntry>? extraDevices = null)
        {
            InitializeComponent();
            DataContext = new SequenceEditorViewModel(recipeService, cameraService, dialogService, extraDevices);
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
