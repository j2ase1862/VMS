using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class YoloSegToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private YoloSegTool TypedTool => (YoloSegTool)Tool;

        public YoloSegToolSettingsViewModel(YoloSegTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputSize { get => TypedTool.InputSize; set => TypedTool.InputSize = value; }
        public float ConfidenceThreshold { get => TypedTool.ConfidenceThreshold; set => TypedTool.ConfidenceThreshold = value; }
        public float IouThreshold { get => TypedTool.IouThreshold; set => TypedTool.IouThreshold = value; }
        public float MaskThreshold { get => TypedTool.MaskThreshold; set => TypedTool.MaskThreshold = value; }
        public bool ShowOverlay { get => TypedTool.ShowOverlay; set => TypedTool.ShowOverlay = value; }
        public double OverlayOpacity { get => TypedTool.OverlayOpacity; set => TypedTool.OverlayOpacity = value; }
        public bool DrawBoxes { get => TypedTool.DrawBoxes; set => TypedTool.DrawBoxes = value; }
        public bool OutputMaskImage { get => TypedTool.OutputMaskImage; set => TypedTool.OutputMaskImage = value; }
    }
}
