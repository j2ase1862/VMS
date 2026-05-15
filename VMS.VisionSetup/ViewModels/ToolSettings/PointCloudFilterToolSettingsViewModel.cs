using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PointCloudFilterToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PointCloudFilterTool TypedTool => (PointCloudFilterTool)Tool;

        public PointCloudFilterToolSettingsViewModel(PointCloudFilterTool tool) : base(tool)
        {
            LoadAvailableParamCodes();
        }

        public bool EnableVoxelGrid { get => TypedTool.EnableVoxelGrid; set => TypedTool.EnableVoxelGrid = value; }
        public float VoxelSize { get => TypedTool.VoxelSize; set => TypedTool.VoxelSize = value; }
        public bool EnableSor { get => TypedTool.EnableSor; set => TypedTool.EnableSor = value; }
        public int SorK { get => TypedTool.SorK; set => TypedTool.SorK = value; }
        public double SorStddev { get => TypedTool.SorStddev; set => TypedTool.SorStddev = value; }
    }
}
