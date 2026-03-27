using System.Windows.Controls;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Annotation
{
    /// <summary>
    /// AnnotationView.xaml code-behind.
    /// ImageCanvas ROI 이벤트를 ViewModel에 위임합니다.
    /// </summary>
    public partial class AnnotationView : UserControl
    {
        public AnnotationView()
        {
            InitializeComponent();
        }

        private AnnotationViewModel? ViewModel => DataContext as AnnotationViewModel;

        private void LabelingCanvas_ROICreated(object? sender, ROIShape e)
        {
            ViewModel?.OnROICreated(e);
        }

        private void LabelingCanvas_ROIModified(object? sender, ROIShape e)
        {
            ViewModel?.OnROIModified(e);
        }

        private void LabelingCanvas_ROIDeleted(object? sender, ROIShape e)
        {
            ViewModel?.OnROIDeleted(e);
        }

        private void LabelingCanvas_ROISelectionChanged(object? sender, ROIShape? e)
        {
            ViewModel?.OnROISelectionChanged(e);
        }
    }
}
