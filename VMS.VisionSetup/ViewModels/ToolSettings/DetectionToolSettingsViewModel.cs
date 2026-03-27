using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class DetectionToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private DetectionTool TypedTool => (DetectionTool)Tool;

        public DetectionToolSettingsViewModel(DetectionTool tool) : base(tool) { }

        // Model
        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputSize { get => TypedTool.InputSize; set => TypedTool.InputSize = value; }

        // Detection
        public double ConfidenceThreshold { get => TypedTool.ConfidenceThreshold; set => TypedTool.ConfidenceThreshold = value; }
        public double IouThreshold { get => TypedTool.IouThreshold; set => TypedTool.IouThreshold = value; }
        public string ClassNamesText { get => TypedTool.ClassNamesText; set => TypedTool.ClassNamesText = value; }

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }
    }
}
