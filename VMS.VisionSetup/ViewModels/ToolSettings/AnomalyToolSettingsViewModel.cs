using VMS.VisionSetup.VisionTools.DeepLearning;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class AnomalyToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private AnomalyTool TypedTool => (AnomalyTool)Tool;

        public AnomalyToolSettingsViewModel(AnomalyTool tool) : base(tool) { }

        // Model
        public string ModelPath { get => TypedTool.ModelPath; set => TypedTool.ModelPath = value; }
        public int InputSize { get => TypedTool.InputSize; set => TypedTool.InputSize = value; }

        // Anomaly Detection
        public double AnomalyThreshold { get => TypedTool.AnomalyThreshold; set => TypedTool.AnomalyThreshold = value; }

        // Display
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }
        public bool ShowHeatmap { get => TypedTool.ShowHeatmap; set => TypedTool.ShowHeatmap = value; }
        public double HeatmapOpacity { get => TypedTool.HeatmapOpacity; set => TypedTool.HeatmapOpacity = value; }
    }
}
