using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PointCloudClusterToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PointCloudClusterTool TypedTool => (PointCloudClusterTool)Tool;

        public PointCloudClusterToolSettingsViewModel(PointCloudClusterTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public float Tolerance { get => TypedTool.Tolerance; set => TypedTool.Tolerance = value; }
        public int MinPoints { get => TypedTool.MinPoints; set => TypedTool.MinPoints = value; }
        public int MaxPoints { get => TypedTool.MaxPoints; set => TypedTool.MaxPoints = value; }
        public int MaxReportedClusters { get => TypedTool.MaxReportedClusters; set => TypedTool.MaxReportedClusters = value; }
        public float XyScale { get => TypedTool.XyScale; set => TypedTool.XyScale = value; }
        public bool DrawOverlay { get => TypedTool.DrawOverlay; set => TypedTool.DrawOverlay = value; }
        public PointCloudClusterTool.DimensionScaleMode ScaleMode
        {
            get => TypedTool.ScaleMode;
            set => TypedTool.ScaleMode = value;
        }
        public PointCloudClusterTool.ClusterOutputMode OutputMode
        {
            get => TypedTool.OutputMode;
            set => TypedTool.OutputMode = value;
        }

        public System.Array OutputModes => System.Enum.GetValues(typeof(PointCloudClusterTool.ClusterOutputMode));

        // ── 판정 (Judgment) — 개수 · 치수 ──
        public bool EnableJudgment { get => TypedTool.EnableJudgment; set { TypedTool.EnableJudgment = value; OnPropertyChanged(); } }
        public bool UseCountJudgment { get => TypedTool.UseCountJudgment; set { TypedTool.UseCountJudgment = value; OnPropertyChanged(); } }
        public PointCloudClusterTool.ClusterCountMode CountMode
        {
            get => TypedTool.CountMode;
            set { TypedTool.CountMode = value; OnPropertyChanged(); }
        }
        public int ExpectedCount { get => TypedTool.ExpectedCount; set { TypedTool.ExpectedCount = value; OnPropertyChanged(); } }
        public int ExpectedCountMax { get => TypedTool.ExpectedCountMax; set { TypedTool.ExpectedCountMax = value; OnPropertyChanged(); } }
        public bool UseSizeJudgment { get => TypedTool.UseSizeJudgment; set { TypedTool.UseSizeJudgment = value; OnPropertyChanged(); } }
        public double ExpectedLength { get => TypedTool.ExpectedLength; set { TypedTool.ExpectedLength = value; OnPropertyChanged(); } }
        public double ExpectedWidth { get => TypedTool.ExpectedWidth; set { TypedTool.ExpectedWidth = value; OnPropertyChanged(); } }
        public double SizeToleranceMinus { get => TypedTool.SizeToleranceMinus; set { TypedTool.SizeToleranceMinus = value; OnPropertyChanged(); } }
        public double SizeTolerancePlus { get => TypedTool.SizeTolerancePlus; set { TypedTool.SizeTolerancePlus = value; OnPropertyChanged(); } }
    }
}
