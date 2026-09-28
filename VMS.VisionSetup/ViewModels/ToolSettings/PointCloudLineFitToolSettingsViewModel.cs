using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PointCloudLineFitToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PointCloudLineFitTool TypedTool => (PointCloudLineFitTool)Tool;

        public PointCloudLineFitToolSettingsViewModel(PointCloudLineFitTool tool) : base(tool) { }

        public float DistanceThreshold { get => TypedTool.DistanceThreshold; set { TypedTool.DistanceThreshold = value; OnPropertyChanged(); } }
        public int Iterations { get => TypedTool.Iterations; set { TypedTool.Iterations = value; OnPropertyChanged(); } }
        public int MinInliers { get => TypedTool.MinInliers; set { TypedTool.MinInliers = value; OnPropertyChanged(); } }
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set { TypedTool.DrawOverlay = value; OnPropertyChanged(); } }

        // ── 판정 — 직진도 ──
        public bool EnableJudgment { get => TypedTool.EnableJudgment; set { TypedTool.EnableJudgment = value; OnPropertyChanged(); } }
        public double MaxStraightness { get => TypedTool.MaxStraightness; set { TypedTool.MaxStraightness = value; OnPropertyChanged(); } }
    }
}
