using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.SynthData
{
    public partial class SynthDataWindow : Window
    {
        public SynthDataWindow(IDialogService dialogService)
        {
            InitializeComponent();
            DataContext = new SynthDataViewModel(dialogService);
        }
    }
}
