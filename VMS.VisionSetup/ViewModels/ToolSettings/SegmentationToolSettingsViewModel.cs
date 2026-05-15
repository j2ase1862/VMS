using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class SegmentationToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private SegmentationTool TypedTool => (SegmentationTool)Tool;

        public SegmentationToolSettingsViewModel(SegmentationTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputSize { get => TypedTool.InputSize; set => TypedTool.InputSize = value; }
        public bool UseImageNetNormalization { get => TypedTool.UseImageNetNormalization; set => TypedTool.UseImageNetNormalization = value; }
        public bool ShowOverlay { get => TypedTool.ShowOverlay; set => TypedTool.ShowOverlay = value; }
        public double OverlayOpacity { get => TypedTool.OverlayOpacity; set => TypedTool.OverlayOpacity = value; }
        public int BackgroundClass { get => TypedTool.BackgroundClass; set => TypedTool.BackgroundClass = value; }
    }
}
