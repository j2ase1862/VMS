using System.Windows;
using VMS.VisionSetup.Interfaces;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.BatchTest
{
    public partial class BatchTestWindow : Window
    {
        public BatchTestWindow(IVisionService visionService)
        {
            InitializeComponent();
            DataContext = new BatchTestViewModel(visionService);
        }
    }
}
