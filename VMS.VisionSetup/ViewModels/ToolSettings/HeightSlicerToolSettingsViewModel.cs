using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class HeightSlicerToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private HeightSlicerTool TypedTool => (HeightSlicerTool)Tool;

        public HeightSlicerToolSettingsViewModel(HeightSlicerTool tool) : base(tool) { }

        public float MinZ { get => TypedTool.MinZ; set => TypedTool.MinZ = value; }
        public float MaxZ { get => TypedTool.MaxZ; set => TypedTool.MaxZ = value; }
    }
}
