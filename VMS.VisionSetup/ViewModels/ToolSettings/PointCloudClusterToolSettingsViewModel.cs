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
        public PointCloudClusterTool.ClusterOutputMode OutputMode
        {
            get => TypedTool.OutputMode;
            set => TypedTool.OutputMode = value;
        }

        public System.Array OutputModes => System.Enum.GetValues(typeof(PointCloudClusterTool.ClusterOutputMode));
    }
}
