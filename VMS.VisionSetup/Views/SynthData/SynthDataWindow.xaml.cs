using System.Windows;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.SynthData
{
    public partial class SynthDataWindow : Window
    {
        public SynthDataWindow()
        {
            InitializeComponent();
            DataContext = new SynthDataViewModel();
        }
    }
}
