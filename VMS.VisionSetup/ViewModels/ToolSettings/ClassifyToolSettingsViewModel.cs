using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class ClassifyToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private ClassifyTool TypedTool => (ClassifyTool)Tool;

        public ClassifyToolSettingsViewModel(ClassifyTool tool) : base(tool) { }

        // Model
        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputWidth { get => TypedTool.InputWidth; set => TypedTool.InputWidth = value; }
        public int InputHeight { get => TypedTool.InputHeight; set => TypedTool.InputHeight = value; }

        // Classification
        public double ConfidenceThreshold { get => TypedTool.ConfidenceThreshold; set => TypedTool.ConfidenceThreshold = value; }
        public string ClassNamesText { get => TypedTool.ClassNamesText; set => TypedTool.ClassNamesText = value; }
        public bool UseImageNetNormalization { get => TypedTool.UseImageNetNormalization; set => TypedTool.UseImageNetNormalization = value; }

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }
    }
}
